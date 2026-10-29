using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Engine.Core.Diagnostics;
using Engine.Runtime.Hosting;
using Engine.UI;
using Game;

internal static class GameplayStabilityChecks
{
    internal static void Run(string root)
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); checks++; }
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "stability-checks-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        string report = Path.Combine(folder, "startup.json");
        var profile = new LoadProfile(report);
        long ms = Stopwatch.Frequency / 1000;
        profile.Record("startup-world", ms * 12);
        profile.Record("startup-physics", ms * 5);
        profile.ObserveFrame(.2, .1f, false, false, false);
        profile.Record("startup-world", ms * 3);
        profile.ObserveFrame(.02, .02f, true, true, false);
        for (int i = 0; i < 300; i++) profile.ObserveFrame(.01, .01f, true, true, true);
        profile.Finish("first-playable-presented", null);
        string original = File.ReadAllText(report);
        profile.Record("startup-world", ms * 99);
        profile.ObserveFrame(.5, .1f, false, false, false);
        profile.Finish("failed", null);
        Check(profile.Complete && profile.Succeeded && File.ReadAllText(report) == original, "Completed startup report changed.");
        using (var doc = JsonDocument.Parse(original))
        {
            var frames = doc.RootElement.GetProperty("Frames");
            Check(frames.GetArrayLength() == 256 && doc.RootElement.GetProperty("StartupFrames").GetInt32() == 302, "Frame retention is not bounded.");
            Check(Math.Abs(frames[0].GetProperty("HostMilliseconds").GetProperty("startup-world").GetDouble() - 12) < .01, "First-frame stage total is wrong.");
            Check(Math.Abs(frames[1].GetProperty("HostMilliseconds").GetProperty("startup-world").GetDouble() - 3) < .01, "Per-frame stage delta is cumulative.");
            Check(frames[1].GetProperty("HostMilliseconds").GetProperty("startup-physics").GetDouble() == 0, "Missing frame work carried forward.");
            Check(doc.RootElement.GetProperty("ClampedFrames").GetInt32() == 1, "Clamp diagnostics changed.");
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var savesProperty = typeof(HL2GameModule).GetProperty("SaveDirectory", flags)!;
        Check((string)savesProperty.GetValue(new HL2GameModule())! == Path.Combine(AppContext.BaseDirectory, "Saves"), "Default save directory changed.");
        using var host = new EngineHost(800, 600, "Save menu regression checks");
        using var game = new HL2GameModule(Path.Combine(root, "Game/Content/Levels/atmosphereTest.json"), saveDirectory: Path.Combine(folder, "Saves"));
        game.Initialize(host.Context);
        Check((string)savesProperty.GetValue(game)! == Path.Combine(folder, "Saves"), "QA save isolation failed.");
        var prepare = typeof(HL2GameModule).GetMethod("PrepareSaveSerialization", flags)!;
        var inventory = (Game.Inventory.InventoryContainer)PbrBaselineChecks.Field(game, "_inventory");
        string beforeInventory = JsonSerializer.Serialize(inventory.ToSaveData());
        PbrBaselineChecks.Set(game, "_loadingOverlayTimer", 0f);
        prepare.Invoke(game, null);
        Check(!(bool)PbrBaselineChecks.Field(game, "_saveSerializationPrepared"), "Save preparation ran outside loading.");
        PbrBaselineChecks.Set(game, "_loadingOverlayTimer", .5f);
        PbrBaselineChecks.Set(game, "_editorEnabled", true);
        prepare.Invoke(game, null);
        Check(!(bool)PbrBaselineChecks.Field(game, "_saveSerializationPrepared"), "Save preparation ran in F2.");
        PbrBaselineChecks.Set(game, "_editorEnabled", false);
        prepare.Invoke(game, null);
        prepare.Invoke(game, null);
        Check((bool)PbrBaselineChecks.Field(game, "_saveSerializationPrepared") && !Directory.Exists(Path.Combine(folder, "Saves")) &&
            JsonSerializer.Serialize(inventory.ToSaveData()) == beforeInventory, "Save preparation wrote files or changed live inventory.");
        var ui = (GameplayUiLayer)PbrBaselineChecks.Field(game, "_gameplayUi");
        ui.Profile = new UiWorkProfile();
        GameplayUiState State()
        {
            ui.Profile.BeginFrame();
            return (GameplayUiState)typeof(HL2GameModule).GetMethod("BuildGameplayUiState", flags)!.Invoke(game, null)!;
        }
        string slot = Path.Combine(folder, "Saves/slot_1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(slot)!);
        void WriteSlot(string area) => File.WriteAllText(slot, JsonSerializer.Serialize(new { SaveVersion = 1, SaveSlot = 1, AreaName = area, LevelName = "atmosphereTest.json", PlayerHealth = 100 }));
        WriteSlot("First area");
        Check(State().SaveSlots.Count == 0 && !ui.Profile.Snapshot().ContainsKey("save-slot-summaries"), "Closed menus read save files.");
        foreach (string menu in new[] { "_saveSlotPanelOpen", "_loadSlotPanelOpen" })
        {
            PbrBaselineChecks.Set(game, menu, true);
            WriteSlot("First area");
            var first = State();
            Check(first.SaveSlots.Count == 4 && first.SaveSlots[0].AreaName == "First area" && !first.SaveSlots[0].IsEmpty && first.SaveSlots[1].IsEmpty, "Open menu lost valid/empty slots.");
            WriteSlot("Changed externally");
            Check(State().SaveSlots[0].AreaName == "Changed externally", "Open menu returned stale metadata.");
            File.WriteAllText(slot, "not valid JSON");
            Check(State().SaveSlots[0].IsEmpty, "Corrupt save was not handled.");
            File.Delete(slot);
            Check(State().SaveSlots.All(s => s.IsEmpty), "Deleted save was cached.");
            PbrBaselineChecks.Set(game, "_editorEnabled", true);
            Check(State().SaveSlots.Count == 0 && !ui.Profile.Snapshot().ContainsKey("save-slot-summaries"), "F2 read hidden save slots.");
            PbrBaselineChecks.Set(game, "_editorEnabled", false);
            PbrBaselineChecks.Set(game, menu, false);
            Check(State().SaveSlots.Count == 0 && !ui.Profile.Snapshot().ContainsKey("save-slot-summaries"), "Closing menu retained file reads.");
        }
        Console.WriteLine($"PASS: {checks} gameplay/startup regression checks. {folder}");
    }
}

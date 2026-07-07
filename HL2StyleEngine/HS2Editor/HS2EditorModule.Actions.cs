using Engine.Editor.Level;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace HS2Editor;

internal sealed partial class HS2EditorModule
{
    private void DrawUiPanel()
    {
        ApplyDefaultWindowPlacement("UI Manager");
        ImGui.Begin("UI Manager");
        MarkUiHover();
        Vector2 available = ImGui.GetContentRegionAvail();
        float leftWidth = MathF.Max(220f, available.X * 0.32f);

        ImGui.BeginChild("uiFiles", new Vector2(leftWidth, 0f), ImGuiChildFlags.Borders);
        if (ImGui.Button("New RML")) CreateUiFile("new_ui.rml", "<rml>\n  <body>\n    <div>New UI</div>\n  </body>\n</rml>\n");
        ImGui.SameLine();
        if (ImGui.Button("New RCSS")) CreateUiFile("new_style.rcss", "body {\n  color: #f0e6b0;\n}\n");
        ImGui.Separator();
        foreach (string path in Directory.EnumerateFiles(_uiRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".rml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".rcss", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            bool selected = string.Equals(path, _selectedUiPath, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(Path.GetRelativePath(_uiRoot, path), selected)) LoadUiFile(path);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("uiEditor", new Vector2(0f, 0f), ImGuiChildFlags.Borders);
        if (string.IsNullOrWhiteSpace(_selectedUiPath))
        {
            ImGui.TextDisabled("Select or create a UI file.");
        }
        else
        {
            ImGui.Text(Path.GetRelativePath(_uiRoot, _selectedUiPath));
            ImGui.SameLine();
            if (_uiDirty) ImGui.TextColored(new Vector4(1f, 0.8f, 0.25f, 1f), "modified");
            ImGui.SameLine();
            if (ImGui.Button("Save UI")) SaveUiFile();
            ImGui.Separator();
            Vector2 editorSize = new(-1f, MathF.Max(180f, ImGui.GetContentRegionAvail().Y * 0.55f));
            if (ImGui.InputTextMultiline("##uiSource", ref _uiSourceText, 128 * 1024, editorSize)) _uiDirty = true;
            ImGui.Separator();
            ImGui.Text("Preview");
            ImGui.BeginChild("uiPreview", new Vector2(0f, 0f), ImGuiChildFlags.Borders);
            ImGui.TextWrapped(_uiSourceText);
            ImGui.EndChild();
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private void LoadLevel(string path)
    {
        path = Path.GetFullPath(path);
        _editor.LoadOrCreate(path, CreateEmptyLevel);
        _project.StartupLevel = MakeProjectRelative(path);
        _project.AddRecentLevel(_project.StartupLevel);
        _renameLevelName = Path.GetFileNameWithoutExtension(path);
        _duplicateLevelName = Path.GetFileNameWithoutExtension(path) + "_copy";
        SaveProject();
        FrameLevel();
        _status = $"Loaded level {MakeProjectRelative(path)}.";
    }

    private void SaveCurrentLevel()
    {
        _editor.Save();
        _status = $"Saved {MakeProjectRelative(_editor.LevelPath)}.";
    }

    private void CreateLevel(string rawName)
    {
        string safeName = MakeSafeFileName(rawName, "new_level");
        string path = Path.Combine(_levelsRoot, safeName + ".json");
        if (File.Exists(path)) { _status = "Level already exists."; return; }
        LevelIO.Save(path, CreateEmptyLevel());
        LoadLevel(path);
    }

    private void DuplicateCurrentLevel()
    {
        string safeName = MakeSafeFileName(_duplicateLevelName, Path.GetFileNameWithoutExtension(_editor.LevelPath) + "_copy");
        string destination = Path.Combine(_levelsRoot, safeName + ".json");
        if (File.Exists(destination)) { _status = "Duplicate destination already exists."; return; }
        _editor.Save();
        File.Copy(_editor.LevelPath, destination);
        LoadLevel(destination);
    }

    private void RenameCurrentLevel()
    {
        string safeName = MakeSafeFileName(_renameLevelName, Path.GetFileNameWithoutExtension(_editor.LevelPath));
        string destination = Path.Combine(_levelsRoot, safeName + ".json");
        if (File.Exists(destination)) { _status = "Rename destination already exists."; return; }
        _editor.Save();
        File.Move(_editor.LevelPath, destination);
        LoadLevel(destination);
    }

    private void SaveSelectedPrefab()
    {
        if (!_editor.TryGetSelectedEntity(out LevelEntityDef entity)) { _status = "Select an entity before creating a prefab."; return; }
        string safeName = MakeSafeFileName(_prefabName, entity.Name ?? entity.Type);
        string path = Path.Combine(_prefabsRoot, safeName + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(entity, EditorJson.Options));
        _status = $"Prefab saved: {MakeProjectRelative(path)}.";
    }

    private void PlacePrefab(string path)
    {
        try
        {
            LevelEntityDef? prefab = JsonSerializer.Deserialize<LevelEntityDef>(File.ReadAllText(path), EditorJson.Options);
            if (prefab == null) { _status = "Prefab could not be loaded."; return; }
            _editor.AddEntityFromTemplate(prefab, "placed");
            _status = $"Placed prefab {Path.GetFileNameWithoutExtension(path)}.";
        }
        catch (Exception ex) { _status = $"Prefab load failed: {ex.Message}"; }
    }

    private void CreateUiFile(string fileName, string defaultText)
    {
        string path = Path.Combine(_uiRoot, fileName);
        if (!File.Exists(path)) File.WriteAllText(path, defaultText);
        LoadUiFile(path);
    }

    private void LoadUiFile(string path)
    {
        if (_uiDirty) SaveUiFile();
        _selectedUiPath = path;
        _uiSourceText = File.Exists(path) ? File.ReadAllText(path) : "";
        _uiDirty = false;
    }

    private void SaveUiFile()
    {
        if (string.IsNullOrWhiteSpace(_selectedUiPath)) return;
        File.WriteAllText(_selectedUiPath, _uiSourceText);
        _uiDirty = false;
        _status = $"Saved UI file {MakeProjectRelative(_selectedUiPath)}.";
    }

    private void LaunchAssetImporter()
    {
        StartDotnetProject(ToAbsolutePath(_project.AssetImporterProject), []);
        _status = "Launching asset importer.";
    }

    private void LaunchGameFromCurrentLevel()
    {
        _editor.Save();
        StartDotnetProject(ToAbsolutePath(_project.GameProject), ["--", "--level", _editor.LevelPath]);
        _status = $"Launching game from {MakeProjectRelative(_editor.LevelPath)}.";
    }

    private void StartDotnetProject(string projectPath, IReadOnlyList<string> extraArgs)
    {
        try
        {
            var info = new ProcessStartInfo("dotnet") { WorkingDirectory = _projectRoot, UseShellExecute = false };
            info.ArgumentList.Add("run");
            info.ArgumentList.Add("--project");
            info.ArgumentList.Add(projectPath);
            foreach (string arg in extraArgs) info.ArgumentList.Add(arg);
            Process.Start(info);
        }
        catch (Exception ex) { _status = $"Launch failed: {ex.Message}"; }
    }
}

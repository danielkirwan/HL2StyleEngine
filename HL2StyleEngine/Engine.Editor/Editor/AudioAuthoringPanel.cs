using System.Numerics;
using System.Text.Json;
using Engine.Runtime.Audio;
using ImGuiNET;

namespace Engine.Editor.Editor;

public sealed class AudioAuthoringPanel : IDisposable
{
    private readonly string root;
    private SoundCueFile? document;
    private AudioService? audition;
    private int selection;
    private bool dirty;
    private string status = "";
    private string? pendingAudition;
    public string Root => root;
    private readonly float[] mixGains = [1, .65f, .5f, .8f, 1];
    public bool HasCue(string id) => document?.Cues.Any(c => c.Id == id) == true;
    public AudioAuthoringPanel(string audioRoot) { root = audioRoot; Reload(); }
    private void Reload()
    {
        pendingAudition = null;
        try { document = SoundCueFile.Load(Path.Combine(root, "cues.json")); status = ""; dirty = false; }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { status = ex.Message; }
    }
    public void Draw(LevelEditorController editor)
    {
        if (ImGui.Button("Reload Cues")) { audition?.Dispose(); audition = null; Reload(); }
        if (document == null) { ImGui.TextWrapped(status); return; }
        ImGui.SameLine();
        if (ImGui.Button("Save Cues"))
        {
            try
            {
                document.Validate(root);
                string path = Path.Combine(root, "cues.json"), temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(document, SoundCueFile.Json));
                File.Move(temp, path, true); dirty = false; status = "Saved";
                audition?.Dispose(); audition = null; pendingAudition = null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { status = ex.Message; }
        }
        if (ImGui.Button("New Cue") && document.Cues.Count < 128)
        {
            string id = "new_cue"; int suffix = 1;
            while (document.Cues.Any(c => c.Id == id)) id = "new_cue_" + suffix++;
            document.Cues.Add(new() { Id = id, Variants = [new()] }); selection = document.Cues.Count - 1; dirty = true;
        }
        if (document.Cues.Count == 0) return;
        selection = Math.Clamp(selection, 0, document.Cues.Count - 1);
        var cue = document.Cues[selection];
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##audioCueList", cue.Id))
        {
            for (int i = 0; i < document.Cues.Count; ++i)
                if (ImGui.Selectable(document.Cues[i].Id, i == selection)) selection = i;
            ImGui.EndCombo();
        }
        cue = document.Cues[selection];
        if (ImGui.Button("Audition") && !dirty)
        {
            audition ??= new(root);
            foreach (var b in Enum.GetValues<AudioBus>()) audition.SetBusGain(b, mixGains[(int)b]);
            pendingAudition = cue.Id;
        }
        ImGui.SameLine(); if (ImGui.Button("Stop")) { pendingAudition = null; audition?.Reset(); }
        ImGui.SameLine(); if (ImGui.Button("Assign To Selected"))
            status = editor.AssignSelectedAudio(cue.Id) ? "Assigned" : "Select an entity";
        if (audition is { } service)
        {
            var d = service.Diagnostics;
            if (d.Loaded && pendingAudition != null)
            { service.Play(new(pendingAudition), new Vector3(0, 0, 2)); pendingAudition = null; }
            ImGui.TextUnformatted(d.Loaded ? $"Voices {d.Voices} / 32 | Output {(d.DeviceReady ? "ready" : "unavailable")}" : "Loading audio");
            if (d.Error.Length > 0) ImGui.TextWrapped(d.Error);
            if (ImGui.Button("Reconnect Output")) service.ReconnectOutput();
            if (ImGui.TreeNode("Audition Mix"))
            {
                foreach (var b in Enum.GetValues<AudioBus>())
                {
                    ImGui.TextUnformatted(b.ToString()); ImGui.SetNextItemWidth(-1);
                    if (ImGui.SliderFloat("##mix" + b, ref mixGains[(int)b], 0, 1)) service.SetBusGain(b, mixGains[(int)b]);
                }
                ImGui.TreePop();
            }
        }
        string idValue = cue.Id; Text("Cue ID", "##cueId", ref idValue); cue.Id = idValue;
        int bus = (int)cue.Bus; ImGui.TextUnformatted("Bus"); ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##cueBus", ref bus, "Effects\0Ambience\0Music\0UI\0Dialogue\0")) { cue.Bus = (AudioBus)bus; dirty = true; }
        bool spatial = cue.Spatial, loop = cue.Loop, stream = cue.Stream;
        dirty |= ImGui.Checkbox("Positional (Mono)", ref spatial); cue.Spatial = spatial;
        dirty |= ImGui.Checkbox("Loop", ref loop); cue.Loop = loop;
        dirty |= ImGui.Checkbox("Stream PCM", ref stream); cue.Stream = stream;
        float gain = cue.Gain, near = cue.MinDistance, far = cue.MaxDistance, min = cue.MinPitch, max = cue.MaxPitch;
        Slider("Gain", "##cueGain", ref gain, 0, 1); cue.Gain = gain;
        Slider("Near Distance", "##cueNear", ref near, .1f, 100); cue.MinDistance = near;
        Slider("Far Distance", "##cueFar", ref far, .2f, 500); cue.MaxDistance = far;
        Slider("Minimum Pitch", "##cuePitchMin", ref min, .5f, 2); cue.MinPitch = min;
        Slider("Maximum Pitch", "##cuePitchMax", ref max, .5f, 2); cue.MaxPitch = max;
        int priority = cue.Priority, voices = cue.MaxVoices;
        ImGui.TextUnformatted("Priority"); ImGui.SetNextItemWidth(-1); dirty |= ImGui.SliderInt("##cuePriority", ref priority, 0, 100); cue.Priority = priority;
        ImGui.TextUnformatted("Concurrency"); ImGui.SetNextItemWidth(-1); dirty |= ImGui.SliderInt("##cueVoices", ref voices, 1, 32); cue.MaxVoices = voices;
        for (int i = 0; i < cue.Variants.Count; ++i)
        {
            ImGui.PushID(i); var variant = cue.Variants[i];
            string file = variant.File; Text("WAV File", "##variantFile", ref file); variant.File = file;
            float weight = variant.Weight; Slider("Weight", "##weight", ref weight, .001f, 100); variant.Weight = weight;
            if (ImGui.Button("Remove Variant")) { cue.Variants.RemoveAt(i--); dirty = true; }
            ImGui.PopID();
        }
        if (cue.Variants.Count < 16 && ImGui.Button("Add Variant")) { cue.Variants.Add(new()); dirty = true; }
        if (dirty) ImGui.TextUnformatted("Unsaved cue changes");
        if (status.Length > 0) ImGui.TextWrapped(status);
    }
    private void Text(string label, string id, ref string value)
    { ImGui.TextUnformatted(label); ImGui.SetNextItemWidth(-1); dirty |= ImGui.InputText(id, ref value, 256); }
    private void Slider(string label, string id, ref float value, float min, float max)
    { ImGui.TextUnformatted(label); ImGui.SetNextItemWidth(-1); dirty |= ImGui.SliderFloat(id, ref value, min, max); }
    public void Dispose() { audition?.Dispose(); audition = null; }
}

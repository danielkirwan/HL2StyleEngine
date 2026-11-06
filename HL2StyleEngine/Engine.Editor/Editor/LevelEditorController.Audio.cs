using Engine.Runtime.Audio;
using Engine.Editor.Level;
using ImGuiNET;

namespace Engine.Editor.Editor;
public sealed partial class LevelEditorController
{
    public bool AssignSelectedAudio(string cue)
    {
        if (!TryGetSelectedEntity(out var entity)) return false;
        BeginEdit(); entity.AudioEmitter = new() { Cue = cue }; Dirty = true; EndEditIfAny(); return true;
    }
    private void DrawAudioInspector(LevelEntityDef entity)
    {
        if (!ImGui.CollapsingHeader("Audio Emitter")) return;
        if (entity.AudioEmitter == null)
        {
            if (ImGui.Button("Add Audio Emitter"))
            { BeginEdit(); entity.AudioEmitter = new(); Dirty = true; EndEditIfAny(); }
            return;
        }
        var emitter = entity.AudioEmitter;
        string cue = emitter.Cue; bool auto = emitter.AutoPlay; float gain = emitter.Gain;
        ImGui.TextUnformatted("Cue ID"); ImGui.SetNextItemWidth(-1);
        bool changed = ImGui.InputText("##audioCue", ref cue, 80);
        changed |= ImGui.Checkbox("Play On Level Start", ref auto);
        ImGui.TextUnformatted("Gain"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.SliderFloat("##emitterGain", ref gain, 0, 1);
        if (AudioAuthoring != null && !AudioAuthoring.HasCue(cue)) ImGui.TextWrapped("Missing cue: " + cue);
        if (changed)
        { BeginEdit(); emitter.Cue = cue; emitter.AutoPlay = auto; emitter.Gain = gain; Dirty = true; EndEditIfAny(); }
        if (ImGui.Button("Remove Audio Emitter"))
        { BeginEdit(); entity.AudioEmitter = null; Dirty = true; EndEditIfAny(); }
        if (AudioAuthoring != null && ImGui.TreeNode("Cue Library"))
        { AudioAuthoring.Draw(this); ImGui.TreePop(); }
    }
}

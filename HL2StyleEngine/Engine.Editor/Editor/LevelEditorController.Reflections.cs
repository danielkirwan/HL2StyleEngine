using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;
using ImGuiNET;

namespace Engine.Editor.Editor;

public sealed partial class LevelEditorController
{
    public ReflectionProbeSession? RoomReflections { get; set; }

    public void AddReflectionProbe(Vector3 position)
    {
        BeginEdit();
        var probe = new LevelEntityDef { Id = Guid.NewGuid().ToString("N"), Type = EntityTypes.ReflectionProbe,
            Name = "Room Reflection", LocalPosition = position, ReflectionProbe = new() };
        LevelFile.Entities.Add(probe); SelectedEntityIndex = LevelFile.Entities.Count-1;
        LevelFile.ReflectionProbeId = probe.Id; LevelFile.ReflectionEnvironment = ReflectionPreset.None;
        Dirty = true; RebuildRuntimeFromLevel(); EndEditIfAny();
    }

    private void DrawReflectionInspector(LevelEntityDef entity)
    {
        if (entity.Type != EntityTypes.ReflectionProbe) return;
        var settings = entity.ReflectionProbe ?? new();
        Vector3 influence = settings.InfluenceSize, projection = settings.ProjectionSize;
        int resolution = settings.Resolution == 32 ? 0 : settings.Resolution == 64 ? 1 : 2, priority = settings.Priority;
        ImGui.Separator(); ImGui.TextUnformatted("Room Reflection");
        ImGui.TextUnformatted("Influence Size"); ImGui.SetNextItemWidth(-1);
        bool changed = ImGui.DragFloat3("##reflectionInfluence", ref influence,.1f,.1f,500);
        ImGui.TextUnformatted("Projection Size"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.DragFloat3("##reflectionProjection", ref projection,.1f,.1f,500);
        ImGui.TextUnformatted("Resolution"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.Combo("##reflectionResolution", ref resolution,"32\0 64\0 128\0");
        ImGui.TextUnformatted("Priority"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.InputInt("##reflectionPriority", ref priority);
        ImGui.TextUnformatted("Lighting Preset: Invariant Only");
        var mask = settings.CaptureMask;
        foreach (var flag in new[] { ReflectionCaptureMask.FixedLights, ReflectionCaptureMask.InvariantEmission, ReflectionCaptureMask.BakedDiffuse })
        {
            bool on = mask.HasFlag(flag);
            if (ImGui.Checkbox(flag.ToString(), ref on)) { changed = true; mask = on ? mask | flag : mask & ~flag; }
        }
        if (changed)
        {
            BeginEdit(); entity.ReflectionProbe = new() { Resolution = new[]{32,64,128}[resolution], InfluenceSize = influence,
                ProjectionSize = projection, Priority = priority, CaptureMask = mask | ReflectionCaptureMask.FixedGeometry };
            Dirty = true; RebuildRuntimeFromLevel(); EndEditIfAny();
        }
        if (ImGui.Button("Preview This Probe")) SelectReflectionProbe(entity.Id);
    }

    private void SelectReflectionProbe(string? id)
    {
        BeginEdit(); LevelFile.ReflectionProbeId = id;
        if (id != null) LevelFile.ReflectionEnvironment = ReflectionPreset.None;
        Dirty = true; EndEditIfAny();
    }

    private void DrawReflectionToolbar()
    {
        if (!ImGui.TreeNode("Room Reflections")) return;
        if (ImGui.Button("Add Reflection Probe"))
        {
            Vector3 position = SelectedEntityIndex >= 0 && SelectedEntityIndex < DrawBoxes.Count ? DrawBoxes[SelectedEntityIndex].Position : new(0,1.8f,0);
            AddReflectionProbe(position);
        }
        var selected = LevelFile.Entities.Find(e => e.Id == LevelFile.ReflectionProbeId);
        ImGui.TextUnformatted("Preview Probe"); ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##roomReflection", selected?.Name ?? "None"))
        {
            if (ImGui.Selectable("None", selected == null)) SelectReflectionProbe(null);
            foreach (var entity in LevelFile.Entities.Where(e => e.Type == EntityTypes.ReflectionProbe))
                if (ImGui.Selectable((entity.Name ?? "Probe")+"##"+entity.Id, entity == selected)) SelectReflectionProbe(entity.Id);
            ImGui.EndCombo();
        }
        if (RoomReflections is { } session && BakeAssetResolver is { } resolve)
        {
            ImGui.Checkbox("Show Reflection Bounds", ref session.ShowBounds);
            ImGui.Checkbox("Preview Room Reflections", ref session.PreviewEnabled);
            bool disabled = selected == null || session.Busy || LevelFile.LightingModel != SurfaceLightingModel.Pbr;
            if (disabled) ImGui.BeginDisabled();
            if (ImGui.Button("Capture / Rebuild"))
            {
                try { if (SaveActionOverride != null) SaveActionOverride(); else Save(); session.BeginCapture(this,resolve); }
                catch (Exception ex) { session.ReportError("Cannot save before capture: "+ex.Message); }
            }
            if (disabled) ImGui.EndDisabled();
            if (session.Busy && ImGui.Button("Cancel Capture")) session.Cancel();
            if (ImGui.Button("Reload Capture")) session.Reload();
            ImGui.TextWrapped(session.Status);
            ImGui.TextUnformatted($"Reflection GPU: {session.ResidentBytes/1048576.0:F2} MiB");
            if (session.CaptureGpuBytes > 0) ImGui.TextUnformatted($"Capture scratch: {session.CaptureGpuBytes/1048576.0:F2} MiB");
        }
        ImGui.TreePop();
    }

    public void DrawReflectionBounds(BasicWorldRenderer world, Renderer renderer)
    {
        if (RoomReflections?.ShowBounds != true) return;
        for (int i=0;i<LevelFile.Entities.Count;i++)
        {
            var entity = LevelFile.Entities[i];
            if (entity.ReflectionProbe is not { } settings || !TryGetEntityWorldTRS(i,out var position,out _,out var scale)) continue;
            DrawBounds((Vector3)settings.InfluenceSize * Vector3.Abs(scale),new(.2f,.8f,1,1));
            DrawBounds((Vector3)settings.ProjectionSize * Vector3.Abs(scale),new(1,.6f,.15f,1));
            void DrawBounds(Vector3 size, Vector4 colour)
            {
                for(int axis=0;axis<3;axis++) for(int a=-1;a<=1;a+=2) for(int b=-1;b<=1;b+=2)
                {
                    Vector3 thickness = new(.015f), offset;
                    if(axis==0) {thickness.X=size.X;offset=new(0,a*size.Y/2,b*size.Z/2);}
                    else if(axis==1) {thickness.Y=size.Y;offset=new(a*size.X/2,0,b*size.Z/2);}
                    else {thickness.Z=size.Z;offset=new(a*size.X/2,b*size.Y/2,0);}
                    world.DrawBox(renderer.CommandList,Matrix4x4.CreateScale(thickness)*Matrix4x4.CreateTranslation(position+offset),colour);
                }
            }
        }
    }
}

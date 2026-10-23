using Engine.Editor.Level;
using ImGuiNET;
using System.Numerics;

namespace Engine.Editor.Editor;

public sealed partial class LevelEditorController
{
    public string ReflectionStatus { get; internal set; } = "Off";
    private void DrawBakeEntityInspector(LevelEntityDef entity)
    {
        if (entity.Type == EntityTypes.PointLight)
        {
            bool bake = entity.BakeIndirect;
            if (ImGui.Checkbox("Fixed Light: Bake Indirect", ref bake))
            { BeginEdit(); entity.BakeIndirect = bake; Dirty = true; EndEditIfAny(); }
        }
        else if (!string.IsNullOrWhiteSpace(entity.MeshPath))
        {
            bool bake = entity.StaticLighting;
            if (ImGui.Checkbox("Static Lighting", ref bake))
            { BeginEdit(); entity.StaticLighting = bake; Dirty = true; EndEditIfAny(); }
            if (bake && !IndirectLightBaker.IsStaticGeometry(entity))
                ImGui.TextColored(new Vector4(1,.6f,.3f,1), "Ineligible: movable or interactive model");
        }
    }

    private void DrawBakeToolbar()
    {
        if (!ImGui.TreeNode("Indirect Lighting")) return;
        var settings = LevelFile.Bake ?? new();
        bool enabled = settings.Enabled; int texels = settings.TexelsPerTriangle, samples = settings.Samples;
        float distance = settings.RayDistance;
        bool probes = settings.Probes != null;
        float spacing = settings.Probes?.Spacing ?? 1.5f;
        bool changed = ImGui.Checkbox("Enable Baked Indirect", ref enabled);
        ImGui.TextUnformatted("Texels Per Triangle"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.SliderInt("##bakeTexels", ref texels, 4, 32);
        ImGui.TextUnformatted("Samples Per Texel"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.SliderInt("##bakeSamples", ref samples, 8, 256);
        ImGui.TextUnformatted("Ray Distance (m)"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.SliderFloat("##bakeDistance", ref distance, 1, 200);
        changed |= ImGui.Checkbox("Bake Light Probes", ref probes);
        if (probes)
        {
            ImGui.TextUnformatted("Probe Spacing (m)"); ImGui.SetNextItemWidth(-1);
            changed |= ImGui.SliderFloat("##probeSpacing", ref spacing, .5f, 8);
        }
        if (changed)
        {
            BeginEdit(); LevelFile.Bake = new() { Enabled = enabled, TexelsPerTriangle = texels, Samples = samples, RayDistance = distance,
                Probes = probes ? new() { Spacing = spacing } : null };
            Dirty = true; EndEditIfAny();
        }
        if (LightingBake is { } bake && BakeAssetResolver is { } resolve)
        {
            if (LevelFile.LightingModel == Engine.Render.SurfaceLightingModel.Pbr)
            {
                ImGui.TextUnformatted("PBR Diagnostic"); ImGui.SetNextItemWidth(-1);
                int diagnostic = (int)bake.Diagnostic;
                if (ImGui.Combo("##pbrDiagnostic", ref diagnostic, "Full\0Direct\0Diffuse Indirect\0Specular Indirect\0Emission\0"))
                    bake.Diagnostic = (Engine.Render.LightingDiagnostic)diagnostic;
            }
            ImGui.TextUnformatted("Lighting View"); ImGui.SetNextItemWidth(-1);
            int view = (int)bake.View;
            if (ImGui.Combo("##lightingView", ref view, "Full\0Direct Only\0Indirect Only\0")) bake.View = (IndirectLightingView)view;
            ImGui.Checkbox("Preview Light Probes", ref bake.PreviewProbes);
            ImGui.Checkbox("Show Probe Markers", ref bake.ShowProbes);
            ImGui.TextColored(bake.Ready ? new(.2f,1,.4f,1) : new(1,.65f,.2f,1), bake.Validity);
            if (bake.Probes is { } volume)
                ImGui.TextUnformatted($"Probes: {volume.Valid.Count(v=>v)} / {volume.Valid.Length}; sample CPU: {bake.ProbeMilliseconds:F3} ms");
            DrawBakeActions(bake, resolve, enabled);
        }
        ImGui.TreePop();
    }

    private void DrawBakeActions(LevelBakeSession bake, Func<string, string> resolve, bool enabled)
    {
        bool disabled = !enabled || bake.Busy;
        if (disabled) ImGui.BeginDisabled();
        if (ImGui.Button("Bake Lighting"))
        {
            try
            {
                if (SaveActionOverride != null) SaveActionOverride(); else Save();
                bake.BeginBake(this, resolve);
            }
            catch (Exception ex) { bake.ReportError("Cannot save before baking: " + ex.Message); }
        }
        if (disabled) ImGui.EndDisabled();
        if (bake.Busy)
        {
            ImGui.ProgressBar(bake.Progress, new Vector2(-1,0));
            if (ImGui.Button("Cancel Bake")) bake.Cancel();
        }
        ImGui.TextWrapped(bake.Status);
        if (bake.ResidentBytes > 0) ImGui.TextUnformatted($"Bake GPU: {bake.ResidentBytes / 1048576.0:F2} MiB");
    }
}

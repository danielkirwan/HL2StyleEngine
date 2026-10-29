using System.Numerics;
using Engine.Render;
using ImGuiNET;

namespace Engine.Editor.Editor;

public sealed partial class LevelEditorController
{
    public void SetPostProcessing(PostProcessSettings? settings)
    {
        settings = settings?.Sanitize();
        if (settings == LevelFile.PostProcessing) return;
        BeginEdit();
        LevelFile.PostProcessing = settings;
        Dirty = true;
        EndEditIfAny();
    }

    private void DrawAtmosphereToolbar()
    {
        if (!ImGui.TreeNode("Atmosphere")) return;
        var s = LevelFile.PostProcessing ?? new PostProcessSettings();
        bool bloom=s.BloomEnabled, grading=s.GradingEnabled, fog=s.FogEnabled;
        bool changed=ImGui.Checkbox("Bloom",ref bloom);
        float intensity=s.BloomIntensity,threshold=s.BloomThreshold,contrast=s.Contrast,saturation=s.Saturation;
        float density=s.FogDensity,start=s.FogStart,height=s.FogBaseHeight,falloff=s.FogHeightFalloff,opacity=s.FogMaxOpacity;
        Vector3 filter=s.ColorFilter,color=s.FogColor;
        int quality=s.BloomDownsample==2?1:0;
        if(bloom)
        {
            changed|=Slider("Bloom intensity",ref intensity,0,1);
            changed|=Slider("Bloom threshold (HDR)",ref threshold,.1f,10);
            ImGui.TextUnformatted("Bloom resolution"); ImGui.SetNextItemWidth(-1);
            changed|=ImGui.Combo("##BloomResolution",ref quality,"Quarter\0Half\0");
        }
        changed|=ImGui.Checkbox("Colour grading",ref grading);
        if(grading)
        {
            changed|=Slider("Contrast",ref contrast,.5f,1.5f);
            changed|=Slider("Saturation",ref saturation,0,2);
            ImGui.TextUnformatted("Colour filter (linear)"); ImGui.SetNextItemWidth(-1);
            changed|=ImGui.ColorEdit3("##ColourFilter",ref filter,ImGuiColorEditFlags.Float|ImGuiColorEditFlags.HDR);
        }
        changed|=ImGui.Checkbox("Distance / height fog",ref fog);
        if(fog)
        {
            ImGui.TextUnformatted("Fog colour (linear)"); ImGui.SetNextItemWidth(-1);
            changed|=ImGui.ColorEdit3("##FogColour",ref color,ImGuiColorEditFlags.Float);
            changed|=Slider("Fog density",ref density,0,.2f);
            changed|=Slider("Fog start distance",ref start,0,100);
            changed|=Slider("Fog base height",ref height,-100,100);
            changed|=Slider("Fog height falloff",ref falloff,0,2);
            changed|=Slider("Fog maximum opacity",ref opacity,0,.95f);
        }
        if(changed) SetPostProcessing(s with {BloomEnabled=bloom,BloomIntensity=intensity,BloomThreshold=threshold,BloomDownsample=quality==1?2:4,
            GradingEnabled=grading,Contrast=contrast,Saturation=saturation,ColorFilter=filter,FogEnabled=fog,FogColor=color,
            FogDensity=density,FogStart=start,FogBaseHeight=height,FogHeightFalloff=falloff,FogMaxOpacity=opacity});
        if(ImGui.Button("Reset atmosphere")) SetPostProcessing(null);
        ImGui.TreePop();

        static bool Slider(string label,ref float value,float min,float max)
        {
            ImGui.TextUnformatted(label); ImGui.SetNextItemWidth(-1);
            return ImGui.SliderFloat("##"+label,ref value,min,max,"%.3f");
        }
    }
}

using Engine.Editor.Level;
using ImGuiNET;

namespace Engine.Editor.Editor;

public sealed partial class LevelEditorController
{
    private string _scenarioIssue = "";
    public void CreateOnOffLightingScenarios()
    {
        var lights=LevelFile.Entities.Where(e=>e.Type==EntityTypes.PointLight && e.BakeIndirect).ToArray();
        var scenarios=new[]{true,false}.Select(on=>new LightingScenarioDef { Id=on?"on":"off",
            Lights=lights.Select(e=>new LightStateOverride { EntityId=e.Id,Enabled=on }).ToList() }).ToList();
        var prior=LevelFile.LightingScenarios;
        try { LevelFile.LightingScenarios=scenarios; LightingScenarioResolver.Validate(LevelFile); }
        finally { LevelFile.LightingScenarios=prior; }
        BeginEdit(); LevelFile.LightingScenarios=scenarios; Dirty=true; EndEditIfAny();
    }
    public void SetAuthoredLightingScenario(string id)
    {
        LightingScenarioResolver.Validate(LevelFile);
        var scenario=LevelFile.LightingScenarios?.Single(s=>s.Id==id) ?? throw new InvalidDataException("Missing lighting scenario.");
        BeginEdit();
        foreach(var state in scenario.Lights) LevelFile.Entities.Single(e=>e.Id==state.EntityId).LightEnabled=state.Enabled;
        Dirty=true; EndEditIfAny();
    }
    private void DrawLightingScenarios()
    {
        if (!ImGui.TreeNode("Lighting States")) return;
        if (LevelFile.LightingScenarios is not { Count: >0 })
        {
            if (ImGui.Button("Create On / Off States"))
                try { CreateOnOffLightingScenarios(); _scenarioIssue=""; } catch(Exception ex) { _scenarioIssue=ex.Message; }
        }
        else
        {
            foreach(var scenario in LevelFile.LightingScenarios)
                if (ImGui.Button("Set Authored State: "+scenario.Id))
                    try { SetAuthoredLightingScenario(scenario.Id); _scenarioIssue=""; } catch(Exception ex) { _scenarioIssue=ex.Message; }
            if (ImGui.Button("Remove Lighting States"))
            { BeginEdit(); LevelFile.LightingScenarios=null; Dirty=true; EndEditIfAny(); }
        }
        if (_scenarioIssue.Length>0) ImGui.TextWrapped(_scenarioIssue);
        ImGui.TreePop();
    }
}

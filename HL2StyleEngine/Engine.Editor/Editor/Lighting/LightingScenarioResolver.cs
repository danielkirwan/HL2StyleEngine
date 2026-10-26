using Engine.Core.Serialization;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

// Cache-only views: runtime overrides never modify the authored scene or its interactions.
public sealed class LightingScenarioResolver
{
    private long _revision = -1;
    private string _path = "";
    private readonly Dictionary<string, LevelEditorController> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LevelEntityDef> _lights = new(StringComparer.Ordinal);
    private string _error = "";
    public string Status { get; private set; } = "Authored lighting";

    public static string PathFor(string levelPath, string id)
    {
        if (id.Length is < 1 or > 32 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidDataException("Lighting scenario IDs require 1-32 ASCII letters, digits, hyphens or underscores.");
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(levelPath))!,Path.GetFileNameWithoutExtension(levelPath)+".lighting-"+id+".json");
    }

    public static void Validate(LevelFile level)
    {
        if (level.LightingScenarios is not { Count: > 0 } scenarios) return;
        if (scenarios.Count > 4 || scenarios.Select(s=>s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=scenarios.Count)
            throw new InvalidDataException("Use 1-4 uniquely named lighting scenarios.");
        var baked = level.Entities.Where(e=>e.Type==EntityTypes.PointLight && e.BakeIndirect).ToDictionary(e=>e.Id,StringComparer.Ordinal);
        if (baked.Count is < 1 or > 32) throw new InvalidDataException("Scenarios require 1-32 fixed bake lights.");
        var signatures = new HashSet<string>();
        foreach(var scenario in scenarios)
        {
            if (string.Equals(scenario.Id,"unmatched",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The scenario ID 'unmatched' is reserved.");
            PathFor("level.json",scenario.Id);
            if (scenario.Lights.Count!=baked.Count || scenario.Lights.Select(l=>l.EntityId).Distinct().Count()!=baked.Count ||
                scenario.Lights.Any(l=>!baked.ContainsKey(l.EntityId)))
                throw new InvalidDataException("Each scenario must specify every Bake Indirect light exactly once.");
            if (!signatures.Add(string.Join('|',scenario.Lights.OrderBy(l=>l.EntityId).Select(l=>l.EntityId+":"+l.Enabled))))
                throw new InvalidDataException("Lighting scenarios must have different light states.");
        }
        if (baked.Values.Any(e=>e.FlickerAmount!=0 || !ReflectionSnapshot.Invariant(e,level)))
            throw new InvalidDataException("Scenario lights require fixed transforms without flicker or scripts.");
    }

    public static LevelFile CreateView(LevelFile level, LightingScenarioDef scenario)
    {
        Validate(level);
        var copy = StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(level));
        copy.LightingScenarios = null;
        foreach(var state in scenario.Lights) copy.Entities.Single(e=>e.Id==state.EntityId).LightEnabled=state.Enabled;
        // Only the validated fixed-light set is baked. Other lights remain realtime-only.
        foreach(var entity in copy.Entities)
            if (string.Equals(entity.Interaction?.Kind,"LightSwitch",StringComparison.OrdinalIgnoreCase)) entity.Interaction=null;
        if (!scenario.Lights.Any(l=>l.Enabled)) copy.Bake.Enabled=false;
        return copy;
    }

    public LevelEditorController Select(LevelEditorController source, LevelLightState? state)
    {
        if (source.LevelFile.LightingScenarios is not { Count: > 0 } scenarios) return source;
        if (_revision!=source.Revision || _path!=source.LevelPath)
        {
            _revision=source.Revision; _path=source.LevelPath; _error=""; _lights.Clear();
            try
            {
                Validate(source.LevelFile);
                foreach(var light in source.LevelFile.Entities.Where(e=>e.Type==EntityTypes.PointLight)) _lights.Add(light.Id,light);
                foreach(var scenario in scenarios)
                    View(scenario.Id).LoadFromMemory(PathFor(_path,scenario.Id),CreateView(source.LevelFile,scenario));
            }
            catch(Exception ex) { _error=ex.Message; }
            var fallback=StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(source.LevelFile));
            fallback.LightingScenarios=null; fallback.Bake.Enabled=false; fallback.AutomaticRoomReflections=false;
            fallback.ReflectionProbeId=null; fallback.ReflectionEnvironment=ReflectionPreset.None;
            View("unmatched").LoadFromMemory(PathFor(_path,"unmatched"),fallback);
        }
        if (_error.Length==0 && source.LevelFile.UsePointLights)
            foreach(var scenario in scenarios)
                if (scenario.Lights.All(l=>_lights.TryGetValue(l.EntityId,out var light) && (state?.IsEnabled(light)??light.LightEnabled)==l.Enabled))
                { Status="Lighting state: "+scenario.Id; return View(scenario.Id); }
        Status=_error.Length>0 ? "Invalid lighting scenarios: "+_error : "Unmatched light state: direct lighting only";
        return View("unmatched");
    }
    private LevelEditorController View(string id)
    {
        if (!_views.TryGetValue(id,out var editor)) _views[id]=editor=new();
        return editor;
    }

    public LevelEditorController? Alternate(LevelEditorController source, LevelEditorController active)
    {
        if (_error.Length != 0 || source.LevelFile.LightingScenarios is not { Count: 2 } states || active.LevelPath.EndsWith(".lighting-unmatched.json",StringComparison.Ordinal)) return null;
        return states.Select(s=>View(s.Id)).FirstOrDefault(v=>v.LevelPath!=active.LevelPath);
    }
}

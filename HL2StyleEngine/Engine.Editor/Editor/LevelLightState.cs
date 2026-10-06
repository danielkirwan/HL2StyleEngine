namespace Engine.Editor.Level;

public sealed class LightStateOverride
{
    public string EntityId { get; set; } = "";
    public bool Enabled { get; set; }
}

// Runtime overrides never mutate the authored defaults. IDs remain valid when a room is not resident.
public sealed class LevelLightState
{
    private readonly Dictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private string _levelPath = "";

    public void BindLevel(string path)
    {
        string fullPath = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);
        if (!string.Equals(_levelPath, fullPath, StringComparison.OrdinalIgnoreCase)) Clear();
        _levelPath = fullPath;
    }

    public void Clear() => _enabled.Clear();

    public bool IsEnabled(LevelEntityDef light)
        => _enabled.TryGetValue(light.Id, out bool enabled) ? enabled : light.LightEnabled;

    public static IEnumerable<LevelEntityDef> Targets(LevelFile level, LevelInteractionDef interaction)
    {
        string group = interaction.LightGroup?.Trim() ?? "";
        var targets = new HashSet<string>(interaction.Targets ?? [], StringComparer.OrdinalIgnoreCase);
        return level.Entities.Where(e => e.Type == EntityTypes.PointLight && !string.IsNullOrWhiteSpace(e.Id) &&
            ((group.Length > 0 && string.Equals(e.LightGroup?.Trim(), group, StringComparison.OrdinalIgnoreCase)) ||
             targets.Contains(e.Id) || (!string.IsNullOrWhiteSpace(e.Name) && targets.Contains(e.Name))));
    }

    public bool Toggle(LevelFile level, LevelInteractionDef interaction, out bool enabled)
    {
        var lights = Targets(level, interaction).ToArray();
        enabled = !lights.Any(IsEnabled);
        if (lights.Length == 0) return false;
        foreach (var light in lights) _enabled[light.Id] = enabled;
        return true;
    }

    public List<LightStateOverride> Capture()
        => _enabled.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new LightStateOverride { EntityId = p.Key, Enabled = p.Value }).ToList();

    public void Restore(IEnumerable<LightStateOverride>? values)
    {
        Clear();
        foreach (var value in values ?? [])
            if (value != null && !string.IsNullOrWhiteSpace(value.EntityId)) _enabled[value.EntityId] = value.Enabled;
    }

    public static float Flicker(LevelEntityDef light, double seconds)
    {
        float amount = float.IsFinite(light.FlickerAmount) ? Math.Clamp(light.FlickerAmount, 0, 1) : 0;
        if (amount == 0 || !double.IsFinite(seconds)) return 1;
        float speed = float.IsFinite(light.FlickerSpeed) ? Math.Clamp(light.FlickerSpeed, .1f, 20) : 4;
        uint seed = 2166136261;
        foreach (char c in light.Id) seed = unchecked((seed ^ c) * 16777619);
        double phase = seed / (double)uint.MaxValue * Math.PI * 2;
        double t = seconds * speed * Math.PI * 2;
        double wave = .5 + .3 * Math.Sin(t + phase) + .2 * Math.Sin(t * 2.31 + phase * 1.7);
        return 1 - amount * (float)Math.Clamp(wave, 0, 1);
    }
}

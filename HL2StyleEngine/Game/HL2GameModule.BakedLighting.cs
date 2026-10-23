namespace Game;

public sealed partial class HL2GameModule
{
    private readonly Engine.Editor.Editor.LevelBakeSession _bakedLighting = new();
    private readonly Engine.Editor.Editor.ReflectionProbeSession _roomReflections = new();
}

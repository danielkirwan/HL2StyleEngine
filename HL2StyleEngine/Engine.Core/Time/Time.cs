namespace Engine.Core.Time;

public static class Time
{
    public static float DeltaTime { get; set; }          // <-- public set
    public static float FixedDeltaTime { get; } = 1f / 60f;
    public static float TotalTime { get; set; }
}

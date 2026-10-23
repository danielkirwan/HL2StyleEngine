using System.Numerics;
using Engine.Render;

internal static class ReflectionCooker
{
    public static void Cook(string root)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var data = Build(Studio);
        string path = Path.Combine(root, "Engine.Render/Assets/SyntheticStudio.hs2env");
        data.Write(path);
        Console.WriteLine($"Cooked reflection v{ReflectionEnvironmentData.Version}: {data.PayloadBytes} bytes, {clock.Elapsed.TotalSeconds:F2}s; {path}");
    }
    public static Vector3 Studio(Vector3 direction) => ReflectionFilter.Studio(direction);
    public static Vector3 Direction(int face, float u, float v) => ReflectionFilter.Direction(face, u, v);
    public static Vector2 Integrate(float nv, float rough, int samples) => ReflectionFilter.Integrate(nv, rough, samples);
    public static ReflectionEnvironmentData Build(Func<Vector3,Vector3> environment, int size=128, int lutSize=128, int samples=256, int lutSamples=1024)
        => ReflectionFilter.Build(environment, size, lutSize, samples, lutSamples);
}

using Engine.Runtime.Hosting;

internal static class GraphicsValidationHost
{
    internal static EngineHost Create(int width, int height, string title)
    {
        string? frames = Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES");
        string? debug = Environment.GetEnvironmentVariable("HS2_PROFILE_DEBUG");
        try
        {
            // Device-only opt-in: restore before any host loop can start a frame capture.
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", "1");
            Environment.SetEnvironmentVariable("HS2_PROFILE_DEBUG", "1");
            return new EngineHost(width, height, title);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", frames);
            Environment.SetEnvironmentVariable("HS2_PROFILE_DEBUG", debug);
        }
    }
}

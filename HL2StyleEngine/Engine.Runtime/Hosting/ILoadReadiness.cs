namespace Engine.Runtime.Hosting;

public sealed record LoadReadiness(bool AssetsReady, bool LightingReady, bool InputReady,
    int FailedModels, string Level, string Status, object? Diagnostics = null);

public interface ILoadReadiness
{
    LoadReadiness GetLoadReadiness();
}

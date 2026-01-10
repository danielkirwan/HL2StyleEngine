using Veldrid;

namespace Engine.Runtime.Hosting;

public interface IGameModule : IDisposable
{
    void Initialize(EngineContext context);

    /// <summary>Per-frame update (variable dt). Read input, build intentions, etc.</summary>
    void Update(float dt, InputSnapshot input);

    /// <summary>Fixed-timestep simulation (e.g. 60Hz). Movement/physics should live here.</summary>
    void FixedUpdate(float fixedDt);

    /// <summary>Draw debug/editor UI (ImGui). Called every frame.</summary>
    void DrawImGui();
}

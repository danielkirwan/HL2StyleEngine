using Engine.Runtime.Hosting;
using ImGuiNET;
using Veldrid;

namespace Game;

public sealed class HL2GameModule : IGameModule
{
    private EngineContext _ctx = null!;

    private readonly InputState _input = new();
    private readonly FpsCamera _camera = new(new System.Numerics.Vector3(0, 1.8f, -5f));
    private UIModeController _ui = null!;

    private float _fps;

    public void Initialize(EngineContext context)
    {
        _ctx = context;

        // Start in gameplay mode (mouse captured)
        _ui = new UIModeController(_ctx.Window, _input, startInGameplay: true);

        _ctx.Window.WarpMouseToCenter();
        _input.OverrideMouseDelta(System.Numerics.Vector2.Zero);
    }

    public void Update(float dt, InputSnapshot snapshot)
    {
        _fps = dt > 0 ? 1f / dt : 0f;

        _input.Update(snapshot);

        // UI toggle (TAB) + force gameplay (F1) - matches your old GameApp behavior
        if (_input.WasPressed(Key.Tab))
            _ui.ToggleUI();

        if (_input.WasPressed(Key.F1))
            _ui.CloseUI();

        // Mouse look (FPS warp-to-center)
        var io = ImGui.GetIO();
        bool uiWantsKeyboard = io.WantCaptureKeyboard;
        bool uiWantsMouse = io.WantCaptureMouse;

        if (_ui.IsMouseCaptured && !uiWantsMouse)
        {
            var center = _ctx.Window.GetWindowCenter();
            var delta = _input.MousePosition - center;

            _camera.AddLook(delta);

            _ctx.Window.WarpMouseToCenter();
            _input.OverrideMouseDelta(System.Numerics.Vector2.Zero);
        }

        // Debug free-fly movement (same as before)
        if (_ui.IsMouseCaptured && !uiWantsKeyboard)
        {
            System.Numerics.Vector3 move = System.Numerics.Vector3.Zero;

            if (_input.IsDown(Key.W)) move += _camera.Forward;
            if (_input.IsDown(Key.S)) move -= _camera.Forward;
            if (_input.IsDown(Key.D)) move += _camera.Right;
            if (_input.IsDown(Key.A)) move -= _camera.Right;

            if (_input.IsDown(Key.Space)) move += _camera.Up;
            if (_input.IsDown(Key.ControlLeft) || _input.IsDown(Key.ControlRight)) move -= _camera.Up;

            _camera.Move(move, dt);
        }
    }

    public void FixedUpdate(float fixedDt)
    {
        // Keep empty for now.
        // We'll put Source-style movement + physics here later.
    }

    public void DrawImGui()
    {
        ImGui.Begin("Debug");
        ImGui.Text($"FPS: {_fps:F1}");

        ImGui.Separator();
        ImGui.Text(_ui.IsUIOpen ? "UI MODE (cursor visible)" : "GAME MODE (mouse captured)");
        ImGui.Text("TAB: Toggle UI mode");
        ImGui.Text("F1: Force GAME mode");

        ImGui.Separator();
        ImGui.Text("WASD: Move  |  Mouse: Look");
        ImGui.Text("Space/Ctrl: Up/Down (debug)");

        ImGui.Separator();
        ImGui.Text($"Cam Pos: {_camera.Position.X:F2}, {_camera.Position.Y:F2}, {_camera.Position.Z:F2}");
        ImGui.Text($"Yaw: {_camera.Yaw:F2} rad  Pitch: {_camera.Pitch:F2} rad");
        ImGui.End();
    }

    public void Dispose()
    {
        // Nothing to dispose (engine owns window/renderer/imgui)
    }
}

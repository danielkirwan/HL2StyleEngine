using System;
using System.Diagnostics;
using Engine.Core.Time;
using Engine.Platform;
using Engine.Render;
using Veldrid;
using Veldrid.StartupUtilities;

namespace Engine.Runtime.Hosting;

public sealed class EngineHost : IDisposable
{
    private readonly GameWindow _window;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Renderer _renderer;
    private readonly ImGuiLayer _imgui;
    private readonly FixedTimestep _fixed = new();

    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private double _prevTime;
    private int _lastW;
    private int _lastH;

    public EngineContext Context { get; }

    public EngineHost(int width, int height, string title, Engine.Core.Diagnostics.LoadProfile? loading = null)
    {
        using (loading?.Measure("host-window")) _window = new GameWindow(width, height, title);

        _lastW = _window.Window.Width;
        _lastH = _window.Window.Height;

        var gdOptions = new GraphicsDeviceOptions(
            debug: true,
            swapchainDepthFormat: PixelFormat.R32_Float,
            syncToVerticalBlank: true);

        using (loading?.Measure("host-device")) _graphicsDevice = VeldridStartup.CreateGraphicsDevice(
            _window.Window,
            gdOptions,
            GraphicsBackend.Direct3D11);

        using (loading?.Measure("host-renderer")) _renderer = new Renderer(_graphicsDevice);

        using (loading?.Measure("host-imgui")) _imgui = new ImGuiLayer(
            _renderer.GraphicsDevice,
            _renderer.GraphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
            _window.Window.Width,
            _window.Window.Height);

        Context = new EngineContext(_window, _renderer, _imgui, loading);
        loading?.Mark("host-ready");
    }

    public void Run(IGameModule module)
    {
        module.Initialize(Context);
        Context.Loading?.Mark("module-initialized");
        FrameProfile? profile = FrameProfile.FromEnvironment();

        _prevTime = _sw.Elapsed.TotalSeconds;

        while (_window.Window.Exists)
        {
            profile?.BeginInput();
            InputSnapshot snapshot = _window.PumpEvents();

            if (module is IInputConsumer consumer)
            {
                if (consumer.InputState.RelativeMouseMode)
                    consumer.InputState.SetRelativeMouseDelta(_window.ConsumeRelativeMouseDelta());

                consumer.InputState.UpdateGamepads();
            }

            int w = _window.Window.Width;
            int h = _window.Window.Height;

            if ((w != _lastW || h != _lastH) && w > 0 && h > 0)
            {
                _lastW = w;
                _lastH = h;

                _graphicsDevice.MainSwapchain.Resize((uint)w, (uint)h);
                _imgui.WindowResized(w, h);

                _renderer.Resize((uint)w, (uint)h);
            }

            if (!_window.Window.Exists) break;

            double t = _sw.Elapsed.TotalSeconds;
            double rawSeconds = t - _prevTime;
            float dt = (float)rawSeconds;
            _prevTime = t;

            if (dt > 0.1f) dt = 0.1f;
            Time.DeltaTime = dt;

            profile?.Begin();
            using (Context.Loading?.Measure("startup-update")) module.Update(dt, snapshot);
            profile?.Mark();
            using (Context.Loading?.Measure("startup-physics")) _fixed.Update(dt, () => module.FixedUpdate(Time.FixedDeltaTime));
            profile?.Mark();

            using (Context.Loading?.Measure("startup-imgui"))
            {
                _imgui.Update(dt, snapshot);
                module.DrawImGui();
            }
            profile?.Mark();

            _renderer.BeginFrame();

            if (module is IWorldRenderer world)
            {
                using (Context.Loading?.Measure("startup-world")) world.RenderWorld(_renderer);
            }

            _renderer.ResolveWorldToSwapchain();
            profile?.Mark();

            if (module is IOverlayRenderer overlay)
            {
                using (Context.Loading?.Measure("startup-overlay")) overlay.RenderOverlay(_renderer);
            }

            _imgui.Render(_renderer.GraphicsDevice, _renderer.CommandList);
            profile?.Mark();

            using (Context.Loading?.Measure("startup-submit-present")) _renderer.EndFrame();
            if (Context.Loading is { Complete: false } loading)
            {
                loading.Mark("first-present-returned");
                var ready = (module as ILoadReadiness)?.GetLoadReadiness();
                loading.ObserveFrame(rawSeconds, dt, ready?.AssetsReady == true, ready?.LightingReady == true, ready?.InputReady == true);
                if (ready?.AssetsReady == true) loading.Mark("required-models-ready");
                if (ready?.LightingReady == true) loading.Mark("lighting-ready");
                if (ready is { AssetsReady: true, LightingReady: true, InputReady: true, FailedModels: 0 })
                    loading.Finish("first-playable-presented", ready);
                else if (ready is { FailedModels: > 0 })
                    loading.Finish("required-assets-failed", ready);
                else if (loading.ElapsedMilliseconds > 60000)
                    loading.Finish("readiness-timeout", ready);
                if (loading.Complete && loading.ExitWhenComplete) break;
            }
            profile?.Mark();
            if (profile?.Finish(w, h) == true) break;
        }
        if (Context.Loading is { Complete: false } unfinished)
            unfinished.Finish("closed-before-ready", (module as ILoadReadiness)?.GetLoadReadiness());
    }

    public void Dispose()
    {
        _imgui.Dispose();
        _renderer.Dispose();
    }
}

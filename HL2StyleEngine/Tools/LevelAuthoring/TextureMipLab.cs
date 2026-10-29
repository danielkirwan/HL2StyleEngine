using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Engine.Input.Devices;
using Engine.Render;
using Engine.Runtime.Hosting;
using ImGuiNET;
using Veldrid;

// An isolated asset gallery, not a new gameplay level or an implicit quality change.
internal sealed class TextureMipLab : IGameModule, IWorldRenderer, IInputConsumer
{
    private sealed record Exhibit(string Name, Vector3 Position, Matrix4x4 Transform, long[] Costs, MipModelResidency Stream);
    private readonly List<Exhibit> _items = new();
    private readonly bool _automatic;
    private readonly Game.FpsCamera _camera = new(new(0, 2, -9));
    private EngineContext _context = null!;
    private BasicWorldRenderer _world = null!;
    private int _active = -1, _frames, _budgetMiB = 24;
    private bool _fullResolution;
    private MipPlan? _plan;
    private long _peakBytes, _steadyBytes, _fullEstimate;
    private readonly List<double> _intervals = new();
    private long _lastFrame;
    public InputState InputState { get; } = new();

    private TextureMipLab(AssetPackage package, bool automatic)
    {
        _automatic = automatic;
        foreach (var model in package.Manifest.Models.Where(m => !m.Path.Contains("Basement_Corridor") && !m.Path.Contains("Sphere_0_4")))
        {
            string path = AssetPackage.Resolve(package.Root, model.Path);
            var source = package.Load(path);
            var points = source.Parts.SelectMany(p => p.Positions).ToArray();
            Vector3 min = points.Aggregate(Vector3.Min), max = points.Aggregate(Vector3.Max);
            float size = Math.Max(.01f, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)));
            Vector3 pos = new((_items.Count % 4 - 1.5f) * 6, .2f, _items.Count / 4 * 18);
            var fit = Matrix4x4.CreateTranslation(new(-(min.X + max.X) / 2, -min.Y, -(min.Z + max.Z) / 2)) *
                Matrix4x4.CreateScale(3 / size) * Matrix4x4.CreateTranslation(pos);
            var costs = MipResidencyBudget.ModelCosts(source);
            _items.Add(new(Path.GetFileNameWithoutExtension(path), pos, fit, costs, new(m => package.LoadMipTail(path, m))));
        }
        _fullEstimate = _items.Sum(x => x.Costs[0]);
    }

    internal static void Run(string root, bool automatic)
    {
        var package = new AssetPackage(LoadingPackageChecks.Build(root));
        string? prior = Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES");
        if (automatic) Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", "3600");
        try
        {
            using var host = new EngineHost(1280, 720, "HS2 Texture Mip Lab");
            using var lab = new TextureMipLab(package, automatic);
            host.Run(lab);
            if (automatic)
            {
                if (lab._items.Any(x => x.Stream.Model == null || x.Stream.Error.Length != 0) || lab._items.Sum(x => x.Stream.Replacements) < 10)
                    throw new InvalidDataException("Mip lab did not exercise successful residency transitions.");
                string folder = Path.Combine(root, "Tools/LevelAuthoring/bin/Release/net10.0/qa"); Directory.CreateDirectory(folder);
                var values = lab._intervals.Order().ToArray();
                if (values.Length != 3600) throw new InvalidDataException("Mip soak ended before all 3600 samples were collected.");
                var report = new
                {
                    Models = lab._items.Count, Replacements = lab._items.Sum(x => x.Stream.Replacements),
                    BudgetMiB = lab._budgetMiB, FullEstimate = lab._fullEstimate, PeakTextureBytes = lab._peakBytes,
                    SteadyTextureBytes = lab._steadyBytes, Samples = values.Length, Mean = values.Average(), P95 = values[(int)(values.Length * .95)],
                    Over33ms = values.Count(x => x > 33.3), Over100ms = values.Count(x => x > 100), package.Fallbacks
                };
                string output = Path.Combine(folder, "mip-lab-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
                File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(JsonSerializer.Serialize(report)); Console.WriteLine("Mip lab report: " + output);
                if (package.Fallbacks != 0 || lab._steadyBytes <= 0 || lab._steadyBytes >= lab._fullEstimate)
                    throw new InvalidDataException("No settled memory reduction, or unhealthy package fallback.");
                if (report.Mean > 17 || report.P95 > 21 || report.Over33ms > 1 || report.Over100ms != 0)
                    throw new InvalidDataException("Mip soak exceeded the existing frame-time gates.");
            }
        }
        finally { Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", prior); }
    }

    public void Initialize(EngineContext context)
    {
        _context = context;
        _world = new(context.Renderer.GraphicsDevice, context.Renderer.WorldOutputDescription, "Shaders")
        { LightingModel = SurfaceLightingModel.Pbr, AmbientStrength = .35f, DirectionalStrength = 1 };
    }

    public void Update(float dt, InputSnapshot input)
    {
        long stamp = Stopwatch.GetTimestamp();
        if (_frames >= 120 && _lastFrame != 0) _intervals.Add(Stopwatch.GetElapsedTime(_lastFrame, stamp).TotalMilliseconds);
        _lastFrame = stamp;
        InputState.Update(input);
        if (_automatic)
        {
            int stop = _frames / 300 % 6;
            _camera.Position = new(0, 2, stop * 18 - 9);
            _camera.Yaw = stop % 2 == 0 ? 0 : MathF.PI;
        }
        else if (!ImGui.GetIO().WantCaptureKeyboard)
        {
            if (InputState.RightMouseDown && !ImGui.GetIO().WantCaptureMouse) _camera.AddLook(InputState.MouseDelta);
            Vector3 move = Vector3.Zero;
            if (InputState.IsDown(Key.W)) move += _camera.Forward;
            if (InputState.IsDown(Key.S)) move -= _camera.Forward;
            if (InputState.IsDown(Key.D)) move += _camera.Right;
            if (InputState.IsDown(Key.A)) move -= _camera.Right;
            if (InputState.IsDown(Key.E)) move += Vector3.UnitY;
            if (InputState.IsDown(Key.Q)) move -= Vector3.UnitY;
            _camera.Move(move, dt);
        }
    }
    public void FixedUpdate(float dt) { }

    public void DrawImGui()
    {
        ImGui.SetNextWindowPos(new(12, 12), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new(355, 220), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new(355, 220), new(float.MaxValue, float.MaxValue));
        ImGui.Begin("Texture Residency");
        ImGui.Checkbox("Full resolution reference", ref _fullResolution);
        ImGui.SetNextItemWidth(140);
        ImGui.SliderInt("Texture budget (MiB)", ref _budgetMiB, 8, 128);
        ImGui.Text($"Resident textures: {_world.TextureResidentBytes / 1048576.0:F2} MiB");
        ImGui.Text($"Conservative target: {(_plan?.EstimatedBytes ?? 0) / 1048576.0:F2} MiB");
        ImGui.Text($"Full-detail estimate: {_fullEstimate / 1048576.0:F2} MiB");
        ImGui.Text($"Ready models: {_items.Count(x => x.Stream.Model != null)}/{_items.Count}");
        ImGui.Text($"Replacements: {_items.Sum(x => x.Stream.Replacements)}");
        if (_plan?.OverBudget == true) ImGui.TextColored(new(1, .7f, .2f, 1), "Pinned textures exceed budget");
        foreach (var item in _items.Where(x => x.Stream.Error.Length > 0)) ImGui.TextWrapped(item.Name + ": " + item.Stream.Error);
        ImGui.End();
    }

    public void RenderWorld(Renderer renderer)
    {
        _world.BeginFrame();
        var demands = _items.Select(x =>
        {
            float distance = Vector3.Distance(_camera.Position, x.Position);
            return new MipDemand(x.Costs, MipResidencyBudget.DistanceMip(distance, x.Stream.CurrentMip), 1 / Math.Max(1, distance), _fullResolution || distance < 12);
        }).ToArray();
        _plan = MipResidencyBudget.Plan(demands, _fullResolution ? long.MaxValue : _budgetMiB * 1048576L);
        for (int i = 0; i < _items.Count; i++) _items[i].Stream.Request(_plan.Mips[i]);
        if (_active < 0 && renderer.PendingRetirementBatches == 0)
            _active = Enumerable.Range(0, _items.Count).Where(i => _items[i].Stream.CurrentMip != _items[i].Stream.RequestedMip && _items[i].Stream.Error.Length == 0)
                .OrderByDescending(i => _items[i].Stream.CurrentMip >= 0 && _items[i].Stream.RequestedMip > _items[i].Stream.CurrentMip)
                .ThenByDescending(i => demands[i].Priority).DefaultIfEmpty(-1).First();
        if (_active >= 0)
        {
            var stream = _items[_active].Stream; stream.Pump(_world, renderer);
            if (!stream.Busy) _active = -1;
        }
        var eye = _camera.Position;
        var vp = Matrix4x4.CreateLookAt(eye, eye + _camera.Forward, Vector3.UnitY) *
            Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, renderer.WorldFramebuffer.Width / (float)renderer.WorldFramebuffer.Height, .05f, 500);
        _world.UpdateCamera(vp, eye); _world.UpdatePointLights([], eye);
        using var uploads = _world.BatchObjectUploads();
        _world.DrawBox(renderer.CommandList, Matrix4x4.CreateScale(30, .2f, 130) * Matrix4x4.CreateTranslation(0, -.1f, 50), new(.12f, .14f, .16f, 1));
        foreach (var item in _items)
        {
            _world.DrawBox(renderer.CommandList, Matrix4x4.CreateScale(4, .2f, 4) * Matrix4x4.CreateTranslation(item.Position.X, .1f, item.Position.Z), new(.25f, .28f, .3f, 1));
            if (item.Stream.Model != null) _world.DrawModel(renderer.CommandList, item.Stream.Model, item.Transform, Vector4.One);
        }
        _peakBytes = Math.Max(_peakBytes, _world.TextureResidentBytes);
        if (_items.All(x => x.Stream.CurrentMip == x.Stream.RequestedMip && !x.Stream.Busy) && renderer.PendingRetirementBatches == 0)
            _steadyBytes = _world.TextureResidentBytes;
        _frames++;
    }

    public void Dispose()
    {
        if (_world == null) return;
        foreach (var item in _items) item.Stream.Retire(_world, _context.Renderer);
        _context.Renderer.DrainRetiredResources();
        if (_world.TextureResidentBytes != 0) throw new InvalidDataException("Texture lab leaked GPU textures.");
        _world.Dispose();
    }
}

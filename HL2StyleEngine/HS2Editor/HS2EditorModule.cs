using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Input.Devices;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;
using Game.World;
using Game.World.MovingPlatform;
using ImGuiNET;
using System.Globalization;
using System.Numerics;
using Veldrid;

namespace HS2Editor;

internal sealed partial class HS2EditorModule : IGameModule, IWorldRenderer, IInputConsumer
{
    private readonly InputState _input = new();
    private readonly LevelEditorController _editor = new();
    private readonly ScriptRegistry _scripts = new();

    private EngineContext _context = null!;
    private BasicWorldRenderer _world = null!;
    private EditorProjectSettings _project = null!;

    private string _projectRoot = "";
    private string _projectFilePath = "";
    private string _contentRoot = "";
    private string _levelsRoot = "";
    private string _prefabsRoot = "";
    private string _uiRoot = "";
    private string _status = "Ready.";

    private Vector3 _cameraPosition = new(0f, 4.2f, -10f);
    private float _cameraYaw;
    private float _cameraPitch = -0.22f;
    private float _cameraSpeed = 7.5f;
    private bool _mouseOverUi;
    private bool _keyboardOverUi;
    private bool _wasLeftMouseDown;
    private bool _sceneMouseDragActive;
    private bool _resetDockLayoutNextFrame;
    private int _defaultLayoutFrames;
    private Vector2 _sceneViewportMin;
    private Vector2 _sceneViewportSize = new(1f, 1f);
    private bool _sceneViewportFocused;
    private const string DockLayoutVersion = "8";
    private const int DefaultLayoutFrameCount = 480;
    private const string GlbAssetDragDropPayload = "HS2_GLB_ASSET_PATH";

    private readonly Dictionary<string, AssetPreviewEntry> _assetPreviewCache = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedAssetAbsolutePath = "";
    private string _selectedAssetProjectPath = "";
    private string _activeGlbDragPath = "";
    private bool _drawSceneGlbModels = true;
    private float _assetPreviewYaw;

    private string _newLevelName = "new_level";
    private string _duplicateLevelName = "";
    private string _renameLevelName = "";
    private string _contentFilter = "";
    private string _prefabName = "NewPrefab";
    private string _prefabVariantName = "NewVariant";
    private string _selectedPrefabPath = "";
    private string _editingPrefabPath = "";
    private string _prefabEditReturnLevelPath = "";
    private string _selectedUiPath = "";
    private string _uiSourceText = "";
    private bool _uiDirty;
    private Matrix4x4 _view = Matrix4x4.Identity;
    private Matrix4x4 _proj = Matrix4x4.Identity;

    public InputState InputState => _input;

    public void Initialize(EngineContext context)
    {
        _context = context;
        _projectRoot = FindProjectRoot();
        _projectFilePath = Path.Combine(_projectRoot, "HS2Project.json");
        _project = EditorProjectSettings.LoadOrCreate(_projectFilePath);

        _contentRoot = ToAbsolutePath(_project.ContentRoot);
        _levelsRoot = Path.Combine(_contentRoot, "Levels");
        _prefabsRoot = Path.Combine(_contentRoot, "Prefabs");
        _uiRoot = Path.Combine(_contentRoot, "UI");
        string imguiIniPath = GetImGuiIniPath();
        bool hasSavedLayout = File.Exists(imguiIniPath);
        bool savedLayoutBroken = hasSavedLayout && SavedEditorLayoutLooksBroken(imguiIniPath);
        _resetDockLayoutNextFrame =
            !hasSavedLayout ||
            savedLayoutBroken ||
            !_project.Preferences.TryGetValue("DockLayoutVersion", out string? dockVersion) ||
            dockVersion != DockLayoutVersion;
        if (_resetDockLayoutNextFrame)
        {
            TryResetImGuiLayoutFile();
            _project.Preferences["DockLayoutVersion"] = DockLayoutVersion;
            SaveProject();
        }
        if (_resetDockLayoutNextFrame)
        {
            _defaultLayoutFrames = DefaultLayoutFrameCount;
            if (savedLayoutBroken)
                _status = "Recovered editor layout from a collapsed saved window state.";
        }

        EnsureProjectFolders();
        EnsureStarterLevels();

        _scripts.Register<MovingPlatformParams>("MovingPlatform", entity => new MovingPlatform(entity));
        _editor.SetScriptRegistry(_scripts);
        _editor.SaveActionOverride = SaveActiveDocument;

        _world = new BasicWorldRenderer(
            _context.Renderer.GraphicsDevice,
            _context.Renderer.WorldOutputDescription,
            shaderDirRelativeToApp: "Shaders");

        string startupLevel = ToAbsolutePath(_project.StartupLevel);
        if (!File.Exists(startupLevel))
            startupLevel = Directory.EnumerateFiles(_levelsRoot, "*.json", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? Path.Combine(_levelsRoot, "new_level.json");

        LoadLevel(startupLevel);
    }

    public void Update(float dt, InputSnapshot input)
    {
        _input.Update(input);

        bool sceneMouse = IsMouseInsideSceneViewport(_input.MousePosition);
        bool sceneKeyboardTarget = sceneMouse || _sceneViewportFocused || _input.RelativeMouseMode;
        bool wantsMouse = (ImGui.GetIO().WantCaptureMouse || _mouseOverUi) && !sceneMouse;
        bool wantsKeyboard = (ImGui.GetIO().WantCaptureKeyboard || _keyboardOverUi) && !sceneKeyboardTarget;

        _input.RelativeMouseMode = _input.RightMouseDown && sceneMouse && !wantsMouse;
        if (_input.RelativeMouseMode)
            UpdateCameraLook();

        if (sceneKeyboardTarget && !wantsKeyboard)
            UpdateCameraMove(dt);

        UpdateEditorPicking(sceneMouse, wantsMouse);
        TryPlaceDraggedGlbOnMouseRelease(sceneMouse);
        _assetPreviewYaw += dt * 0.45f;

        _wasLeftMouseDown = _input.LeftMouseDown;
    }

    public void FixedUpdate(float fixedDt)
    {
    }

    public void DrawImGui()
    {
        _mouseOverUi = false;
        _keyboardOverUi = false;

        DrawMainMenu();
        DrawDockspace();
        DrawViewportPanel();
        DrawProjectPanel();
        DrawLevelsPanel();
        DrawContentBrowserPanel();
        DrawPrefabPanel();
        DrawUiPanel();
        ApplyDefaultWindowPlacement("Toolbar");
        _editor.DrawToolbarPanel(ref _mouseOverUi, ref _keyboardOverUi);
        ApplyDefaultWindowPlacement("Hierarchy");
        _editor.DrawHierarchyPanel(ref _mouseOverUi, ref _keyboardOverUi);
        ApplyDefaultWindowPlacement("Inspector");
        _editor.DrawInspectorPanel(ref _mouseOverUi, ref _keyboardOverUi);
        DrawStatusPanel();
        if (_defaultLayoutFrames > 0)
            _defaultLayoutFrames--;
    }

    public void Dispose()
    {
        DisposeEditorSceneModelCache();
        _world?.Dispose();
    }

    private static void TryResetImGuiLayoutFile()
    {
        try
        {
            ImGui.LoadIniSettingsFromMemory("");
            string iniPath = GetImGuiIniPath();
            if (File.Exists(iniPath))
                File.Delete(iniPath);
        }
        catch
        {
        }
    }
    private static string GetImGuiIniPath()
        => Path.Combine(AppContext.BaseDirectory, "imgui.ini");

    private static bool SavedEditorLayoutLooksBroken(string iniPath)
    {
        try
        {
            Dictionary<string, Vector2> sizes = new(StringComparer.OrdinalIgnoreCase);
            string? currentWindow = null;
            foreach (string rawLine in File.ReadLines(iniPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("[Window][", StringComparison.Ordinal))
                {
                    int start = "[Window][".Length;
                    int end = line.IndexOf(']', start);
                    currentWindow = end > start ? line[start..end] : null;
                    continue;
                }

                if (currentWindow is null || !line.StartsWith("Size=", StringComparison.Ordinal))
                    continue;

                string[] parts = line["Size=".Length..].Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                    continue;

                if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float width) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float height))
                {
                    sizes[currentWindow] = new Vector2(width, height);
                }
            }

            return IsMissingOrTiny(sizes, "Scene", 320f, 220f) ||
                   IsMissingOrTiny(sizes, "Hierarchy", 180f, 120f) ||
                   IsMissingOrTiny(sizes, "Inspector", 240f, 160f) ||
                   IsMissingOrTiny(sizes, "Content Browser", 260f, 120f);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsMissingOrTiny(Dictionary<string, Vector2> sizes, string windowName, float minWidth, float minHeight)
        => !sizes.TryGetValue(windowName, out Vector2 size) || size.X < minWidth || size.Y < minHeight;
}


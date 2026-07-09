using Engine.Editor.Level;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace HS2Editor;

internal sealed partial class HS2EditorModule
{
    private void DrawMainMenu()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Save Level", "Ctrl+S")) SaveCurrentLevel();
            if (ImGui.MenuItem("Launch Game From Level")) LaunchGameFromCurrentLevel();
            if (ImGui.MenuItem("Launch Asset Importer")) LaunchAssetImporter();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Create"))
        {
            if (ImGui.MenuItem("New Empty Level")) CreateLevel(_newLevelName);
            if (ImGui.MenuItem("Prefab From Selection")) SaveSelectedPrefab();
            if (ImGui.MenuItem("Prefab Variant From Selection")) SaveSelectedPrefab(isVariant: true);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            ImGui.MenuItem("Show Scene Meshes", "", ref _drawSceneGlbModels);
            if (ImGui.MenuItem("Reset Dock Layout"))
            {
                TryResetImGuiLayoutFile();
                _resetDockLayoutNextFrame = true;
                _defaultLayoutFrames = DefaultLayoutFrameCount;
                _project.Preferences["DockLayoutVersion"] = DockLayoutVersion;
                SaveProject();
            }
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    private void DrawDockspace()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("HS2EditorDockspace", flags);
        ImGui.PopStyleVar(3);

        uint dockspaceId = ImGui.GetID("HS2EditorDockspaceId");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);
        if (_resetDockLayoutNextFrame)
        {
            _defaultLayoutFrames = Math.Max(_defaultLayoutFrames, DefaultLayoutFrameCount);
            _resetDockLayoutNextFrame = false;
        }
        ImGui.End();
    }

    private void ApplyDefaultWindowPlacement(string windowName)
    {
        if (_defaultLayoutFrames <= 0)
            return;

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        Vector2 rootPos = viewport.WorkPos;
        Vector2 rootSize = viewport.WorkSize;
        float leftWidth = MathF.Max(300f, rootSize.X * 0.21f);
        float rightWidth = MathF.Max(380f, rootSize.X * 0.26f);
        float bottomHeight = MathF.Max(240f, rootSize.Y * 0.28f);
        float centerWidth = MathF.Max(280f, rootSize.X - leftWidth - rightWidth);
        float centerHeight = MathF.Max(240f, rootSize.Y - bottomHeight);

        Vector2 pos;
        Vector2 size;
        switch (windowName)
        {
            case "Hierarchy":
                pos = rootPos;
                size = new Vector2(leftWidth, rootSize.Y * 0.52f);
                break;
            case "Levels":
                pos = rootPos + new Vector2(0f, rootSize.Y * 0.52f);
                size = new Vector2(leftWidth, rootSize.Y * 0.20f);
                break;
            case "Project":
                pos = rootPos + new Vector2(0f, rootSize.Y * 0.72f);
                size = new Vector2(leftWidth, rootSize.Y * 0.28f);
                break;
            case "Scene":
                pos = rootPos + new Vector2(leftWidth, 0f);
                size = new Vector2(centerWidth, centerHeight);
                break;
            case "Content Browser":
                pos = rootPos + new Vector2(leftWidth, centerHeight);
                size = new Vector2(centerWidth, bottomHeight);
                break;
            case "Inspector":
                pos = rootPos + new Vector2(leftWidth + centerWidth, 0f);
                size = new Vector2(rightWidth, rootSize.Y * 0.60f);
                break;
            case "Toolbar":
                pos = rootPos + new Vector2(leftWidth + centerWidth, rootSize.Y * 0.60f);
                size = new Vector2(rightWidth, rootSize.Y * 0.18f);
                break;
            case "Prefabs":
                pos = rootPos + new Vector2(leftWidth + centerWidth, rootSize.Y * 0.78f);
                size = new Vector2(rightWidth, rootSize.Y * 0.18f);
                break;
            case "UI Manager":
                pos = rootPos + new Vector2(leftWidth + centerWidth, rootSize.Y * 0.88f);
                size = new Vector2(rightWidth, rootSize.Y * 0.08f);
                break;
            case "Status":
                pos = rootPos + new Vector2(leftWidth + centerWidth, rootSize.Y * 0.96f);
                size = new Vector2(rightWidth, rootSize.Y * 0.04f);
                break;
            default:
                return;
        }

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
    }

    private void DrawProjectPanel()
    {
        ApplyDefaultWindowPlacement("Project");
        ImGui.Begin("Project");
        MarkUiHover();
        ImGui.Text(_project.ProjectName);
        ImGui.TextDisabled(_projectRoot);
        ImGui.Separator();
        ImGui.Text($"Content: {MakeProjectRelative(_contentRoot)}");
        ImGui.Text($"Level: {MakeProjectRelative(_editor.LevelPath)}");
        ImGui.Text($"Startup: {_project.StartupLevel}");
        ImGui.Separator();
        if (ImGui.Button("Save Project")) { SaveProject(); _status = "Project settings saved."; }
        ImGui.SameLine();
        if (ImGui.Button("Launch Importer")) LaunchAssetImporter();
        ImGui.SameLine();
        if (ImGui.Button("Play Selected Level")) LaunchGameFromCurrentLevel();
        ImGui.Separator();
        ImGui.TextDisabled("Path-based assets for now. GUID/meta asset IDs come later.");
        ImGui.End();
    }

    private void DrawLevelsPanel()
    {
        ApplyDefaultWindowPlacement("Levels");
        ImGui.Begin("Levels");
        MarkUiHover();
        ImGui.SetNextItemWidth(180f);
        ImGui.InputText("New", ref _newLevelName, 96);
        ImGui.SameLine();
        if (ImGui.Button("Create")) CreateLevel(_newLevelName);

        ImGui.SetNextItemWidth(180f);
        ImGui.InputText("Duplicate As", ref _duplicateLevelName, 96);
        ImGui.SameLine();
        if (ImGui.Button("Duplicate Current")) DuplicateCurrentLevel();

        ImGui.SetNextItemWidth(180f);
        ImGui.InputText("Rename To", ref _renameLevelName, 96);
        ImGui.SameLine();
        if (ImGui.Button("Rename Current")) RenameCurrentLevel();

        ImGui.Separator();
        foreach (string path in Directory.EnumerateFiles(_levelsRoot, "*.json", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
        {
            bool selected = string.Equals(Path.GetFullPath(path), Path.GetFullPath(_editor.LevelPath), StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(Path.GetFileName(path), selected)) LoadLevel(path);
        }
        ImGui.End();
    }

    private void DrawContentBrowserPanel()
    {
        ApplyDefaultWindowPlacement("Content Browser");
        ImGui.Begin("Content Browser");
        MarkUiHover();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##contentFilter", "Filter assets...", ref _contentFilter, 128);
        ImGui.Separator();

        if (ImGui.BeginTabBar("ContentTabs"))
        {
            if (ImGui.BeginTabItem("Models")) { DrawAssetList(Path.Combine(_contentRoot, "Models"), "*.glb", true); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Animations")) { DrawAssetList(Path.Combine(_contentRoot, "Animations"), "*.glb", false); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Prefabs")) { DrawPrefabAssetList(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("All Content")) { DrawAssetList(_contentRoot, "*.*", false); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private void DrawAssetList(string root, string pattern, bool showAssignModel)
    {
        if (!Directory.Exists(root)) { ImGui.TextDisabled("Folder does not exist yet."); return; }
        string[] files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(FileMatchesContentFilter)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToArray();

        if (files.Length == 0) { ImGui.TextDisabled($"No matching assets in {MakeProjectRelative(root)}."); return; }

        Vector2 available = ImGui.GetContentRegionAvail();
        bool drawPreview = showAssignModel;
        bool enoughRoomForPreview = drawPreview && available.X >= 560f;
        float listWidth = enoughRoomForPreview ? MathF.Max(320f, available.X * 0.56f) : 0f;

        ImGui.BeginChild($"assetList{root}{pattern}", enoughRoomForPreview ? new Vector2(listWidth, 0f) : new Vector2(0f, 0f), ImGuiChildFlags.Borders);
        ImGui.TextDisabled($"{files.Length} asset(s) in {MakeProjectRelative(root)}");
        ImGui.Separator();

        if (ImGui.BeginTable($"assetTable{root}{pattern}", showAssignModel ? 2 : 1, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Asset", ImGuiTableColumnFlags.WidthStretch);
            if (showAssignModel)
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 64f);

            foreach (string file in files)
            {
                string projectRelative = MakeProjectRelative(file);
                string contentPath = ToContentAssetPath(file);
                bool selected = string.Equals(Path.GetFullPath(file), _selectedAssetAbsolutePath, StringComparison.OrdinalIgnoreCase);

                ImGui.PushID(file);
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                if (ImGui.Selectable(projectRelative, selected))
                    SelectAsset(file);

                if (showAssignModel)
                    DrawGlbAssetDragSource(contentPath, Path.GetFileName(file));

                if (showAssignModel)
                {
                    ImGui.TableSetColumnIndex(1);
                    bool canAssign = _editor.TryGetSelectedEntity(out _);
                    if (!canAssign) ImGui.BeginDisabled();
                    if (ImGui.SmallButton("Assign"))
                    {
                        SelectAsset(file);
                        if (_editor.AssignSelectedMeshPath(contentPath))
                            _status = $"Assigned {contentPath}.";
                        else
                            _status = "Select a scene object before assigning a model.";
                    }
                    if (!canAssign) ImGui.EndDisabled();
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();

        if (enoughRoomForPreview)
        {
            ImGui.SameLine();
            DrawSelectedAssetPreviewPane();
        }
    }

    private void DrawGlbAssetDragSource(string contentPath, string label)
    {
        if (!ImGui.BeginDragDropSource())
            return;

        byte[] bytes = Encoding.UTF8.GetBytes(contentPath + "\0");
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            _activeGlbDragPath = contentPath;
            ImGui.SetDragDropPayload(GlbAssetDragDropPayload, handle.AddrOfPinnedObject(), (uint)bytes.Length);
            ImGui.Text($"Model: {label}");
            ImGui.TextDisabled(contentPath);
            ImGui.EndDragDropSource();
        }
        finally
        {
            handle.Free();
        }
    }

    private static bool TryReadPayloadString(ImGuiPayloadPtr payload, out string value)
    {
        value = "";
        int size = payload.DataSize;
        nint data = payload.Data;
        if (data == IntPtr.Zero || size <= 0)
            return false;

        byte[] bytes = new byte[size];
        Marshal.Copy(data, bytes, 0, size);
        value = Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private void DrawPrefabPanel()
    {
        ApplyDefaultWindowPlacement("Prefabs");
        ImGui.Begin("Prefabs");
        MarkUiHover();

        if (IsEditingPrefab)
        {
            ImGui.Text($"Editing: {MakeProjectRelative(_editingPrefabPath)}");
            if (ImGui.Button("Save Prefab")) SaveEditedPrefab();
            ImGui.SameLine();
            if (ImGui.Button("Return To Level")) ReturnFromPrefabEdit();
            ImGui.Separator();
        }

        ImGui.SetNextItemWidth(170f);
        ImGui.InputText("Name", ref _prefabName, 96);
        ImGui.SameLine();
        bool hasSelection = _editor.TryGetSelectedEntity(out _);
        if (!hasSelection) ImGui.BeginDisabled();
        if (ImGui.Button("Create From Selection")) SaveSelectedPrefab();
        if (!hasSelection) ImGui.EndDisabled();

        ImGui.SetNextItemWidth(170f);
        ImGui.InputText("Variant", ref _prefabVariantName, 96);
        ImGui.SameLine();
        if (!hasSelection) ImGui.BeginDisabled();
        if (ImGui.Button("Create Variant")) SaveSelectedPrefab(isVariant: true);
        if (!hasSelection) ImGui.EndDisabled();

        ImGui.Separator();
        bool isPrefabInstance = _editor.TryGetSelectedPrefabInstance(out _, out string selectedPrefabAssetPath);
        if (isPrefabInstance)
        {
            ImGui.TextDisabled(selectedPrefabAssetPath);
            if (ImGui.Button("Apply")) ApplySelectedPrefabInstance();
            ImGui.SameLine();
            if (ImGui.Button("Revert")) RevertSelectedPrefabInstance();
            ImGui.SameLine();
            if (ImGui.Button("Unpack")) UnpackSelectedPrefabInstance();
        }
        else
        {
            ImGui.TextDisabled("Select a prefab instance for Apply / Revert / Unpack.");
        }

        ImGui.Separator();
        DrawPrefabListRows(compact: true);
        ImGui.End();
    }

    private void DrawPrefabAssetList()
    {
        DrawPrefabListRows(compact: false);
    }

    private void DrawPrefabListRows(bool compact)
    {
        if (!Directory.Exists(_prefabsRoot))
        {
            ImGui.TextDisabled("No Prefabs folder yet.");
            return;
        }

        string[] prefabs = Directory.EnumerateFiles(_prefabsRoot, "*.json", SearchOption.AllDirectories)
            .Where(FileMatchesContentFilter)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToArray();

        if (prefabs.Length == 0)
        {
            ImGui.TextDisabled("No matching prefabs.");
            return;
        }

        if (compact)
        {
            foreach (string path in prefabs.Take(8))
                DrawPrefabRow(path);
            if (prefabs.Length > 8)
                ImGui.TextDisabled($"{prefabs.Length - 8} more in Content Browser > Prefabs.");
            return;
        }

        ImGui.TextDisabled($"{prefabs.Length} prefab(s) in {MakeProjectRelative(_prefabsRoot)}");
        ImGui.Separator();
        if (ImGui.BeginTable("prefabAssetTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Prefab", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Place", ImGuiTableColumnFlags.WidthFixed, 58f);
            ImGui.TableSetupColumn("Edit", ImGuiTableColumnFlags.WidthFixed, 48f);
            ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthFixed, 72f);

            foreach (string path in prefabs)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                bool selected = string.Equals(Path.GetFullPath(path), _selectedPrefabPath, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(MakeProjectRelative(path), selected))
                    _selectedPrefabPath = Path.GetFullPath(path);

                ImGui.TableSetColumnIndex(1);
                if (ImGui.SmallButton($"Place##cb{path}")) PlacePrefab(path);

                ImGui.TableSetColumnIndex(2);
                if (ImGui.SmallButton($"Edit##cb{path}")) EditPrefab(path);

                ImGui.TableSetColumnIndex(3);
                DrawPrefabKind(path);
            }
            ImGui.EndTable();
        }
    }

    private void DrawPrefabRow(string path)
    {
        bool selected = string.Equals(Path.GetFullPath(path), _selectedPrefabPath, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Selectable(Path.GetFileNameWithoutExtension(path), selected))
            _selectedPrefabPath = Path.GetFullPath(path);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Place##{path}")) PlacePrefab(path);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Edit##{path}")) EditPrefab(path);
    }

    private void DrawPrefabKind(string path)
    {
        try
        {
            PrefabFile prefab = PrefabIO.Load(path);
            ImGui.TextDisabled(prefab.IsVariant ? "Variant" : "Prefab");
        }
        catch
        {
            ImGui.TextDisabled("Legacy");
        }
    }

    private void DrawViewportPanel()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        ApplyDefaultWindowPlacement("Scene");
        ImGui.Begin("Scene", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        _sceneViewportFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        _sceneViewportMin = ImGui.GetCursorScreenPos();
        Vector2 available = ImGui.GetContentRegionAvail();
        _sceneViewportSize = new Vector2(MathF.Max(1f, available.X), MathF.Max(1f, available.Y));

        ImGui.InvisibleButton("##sceneViewportInput", _sceneViewportSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.SetWindowFocus();

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 textPos = _sceneViewportMin + new Vector2(8f, 8f);
        drawList.AddText(textPos, 0xD0FFFFFF, "Scene  RMB + WASD/QE to fly  LMB selects/drags");
        drawList.AddText(textPos + new Vector2(0f, 18f), 0x90FFFFFF, $"Camera: {_cameraPosition.X:0.0}, {_cameraPosition.Y:0.0}, {_cameraPosition.Z:0.0}");
        if (!_drawSceneGlbModels)
            drawList.AddText(textPos + new Vector2(0f, 36f), 0xFF66AAFF, "Scene meshes hidden: View > Show Scene Meshes");

        ImGui.End();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private void DrawStatusPanel()
    {
        ApplyDefaultWindowPlacement("Status");
        ImGui.Begin("Status");
        MarkUiHover();
        ImGui.TextWrapped(_status);
        ImGui.End();
    }

    private void MarkUiHover()
    {
        _mouseOverUi |= ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        _keyboardOverUi |= ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
    }
}


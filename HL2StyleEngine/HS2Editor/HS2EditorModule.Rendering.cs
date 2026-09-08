using Editor.Editor;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using System.Numerics;
using Veldrid;

namespace HS2Editor;

internal sealed partial class HS2EditorModule
{
    private sealed class EditorSceneModelEntry : IDisposable
    {
        public LoadedModel? LoadedModel;
        public RenderModel? RenderModel;
        public Task<LoadedModel>? LoadTask;
        public Vector3 Min;
        public Vector3 Max;
        public bool Failed;
        public string Error = "";

        public void Dispose()
        {
            RenderModel?.Dispose();
            RenderModel = null;
        }
    }

    private readonly Dictionary<string, EditorSceneModelEntry> _sceneModelCache = new(StringComparer.OrdinalIgnoreCase);

    private Vector3 CameraForward
    {
        get
        {
            float cp = MathF.Cos(_cameraPitch);
            float sp = MathF.Sin(_cameraPitch);
            float cy = MathF.Cos(_cameraYaw);
            float sy = MathF.Sin(_cameraYaw);
            return Vector3.Normalize(new Vector3(sy * cp, sp, cy * cp));
        }
    }

    private Vector3 CameraRight
    {
        get
        {
            Vector3 right = Vector3.Cross(Vector3.UnitY, CameraForward);
            return right.LengthSquared() < 0.0001f ? Vector3.UnitX : -Vector3.Normalize(right);
        }
    }

    private void UpdateCameraLook()
    {
        const float sensitivity = 0.0025f;
        Vector2 delta = _input.MouseDelta;
        _cameraYaw -= delta.X * sensitivity;
        _cameraPitch -= delta.Y * sensitivity;
        _cameraPitch = Math.Clamp(_cameraPitch, -1.53f, 1.53f);
    }

    private void UpdateCameraMove(float dt)
    {
        Vector3 move = Vector3.Zero;
        Vector3 forwardFlat = CameraForward;
        forwardFlat.Y = 0f;
        if (forwardFlat.LengthSquared() > 0.001f)
            forwardFlat = Vector3.Normalize(forwardFlat);

        if (_input.IsDown(Key.W)) move += forwardFlat;
        if (_input.IsDown(Key.S)) move -= forwardFlat;
        if (_input.IsDown(Key.D)) move += CameraRight;
        if (_input.IsDown(Key.A)) move -= CameraRight;
        if (_input.IsDown(Key.E)) move += Vector3.UnitY;
        if (_input.IsDown(Key.Q)) move -= Vector3.UnitY;

        if (move.LengthSquared() <= 0.001f)
            return;

        float speed = _input.IsDown(Key.ShiftLeft) || _input.IsDown(Key.ShiftRight)
            ? _cameraSpeed * 2.5f
            : _cameraSpeed;
        _cameraPosition += Vector3.Normalize(move) * speed * dt;
    }

    private void UpdateEditorPicking(bool sceneMouse, bool wantsMouse)
    {
        bool ctrl = _input.IsDown(Key.ControlLeft) || _input.IsDown(Key.ControlRight);
        bool vertexSnap = _input.IsDown(Key.V);

        if (!string.IsNullOrWhiteSpace(_activeGlbDragPath))
        {
            _editor.UpdateVertexSnapPreview(default, active: false);
            return;
        }

        if (sceneMouse && !wantsMouse)
            _editor.UpdateVertexSnapPreview(GetMouseRay(), vertexSnap);
        else
            _editor.UpdateVertexSnapPreview(default, active: false);

        if (_input.LeftMousePressedThisFrame && sceneMouse && !wantsMouse)
        {
            _sceneMouseDragActive = !vertexSnap;
            _editor.OnMousePressed(GetMouseRay(), ctrl, vertexSnap);
        }
        else if (_input.LeftMouseDown && _sceneMouseDragActive)
        {
            _editor.OnMouseHeld(GetMouseRay(), leftDown: true, ctrl, vertexSnap);
        }
        else if (!_input.LeftMouseDown && _sceneMouseDragActive)
        {
            _editor.OnMouseReleased();
            _sceneMouseDragActive = false;
        }
    }

    private void TryPlaceDraggedGlbOnMouseRelease(bool sceneMouse)
    {
        if (string.IsNullOrWhiteSpace(_activeGlbDragPath))
            return;

        if (!_input.LeftMouseReleasedThisFrame)
            return;

        if (sceneMouse && _editor.PlaceModelInScene(_activeGlbDragPath, GetMouseRay()))
            _status = $"Placed {_activeGlbDragPath}.";

        _activeGlbDragPath = "";
        _sceneMouseDragActive = false;
    }

    private bool IsMouseInsideSceneViewport(Vector2 mouse)
        => mouse.X >= _sceneViewportMin.X && mouse.Y >= _sceneViewportMin.Y &&
           mouse.X <= _sceneViewportMin.X + _sceneViewportSize.X &&
           mouse.Y <= _sceneViewportMin.Y + _sceneViewportSize.Y;

    private EditorPicking.Ray GetMouseRay()
    {
        Vector2 local = _input.MousePosition - _sceneViewportMin;
        local.X = Math.Clamp(local.X, 0f, MathF.Max(1f, _sceneViewportSize.X) - 1f);
        local.Y = Math.Clamp(local.Y, 0f, MathF.Max(1f, _sceneViewportSize.Y) - 1f);

        return EditorPicking.ScreenPointToRay(
            local,
            Math.Max(1f, _sceneViewportSize.X),
            Math.Max(1f, _sceneViewportSize.Y),
            _view,
            _proj);
    }

    public void RenderWorld(Renderer renderer)
    {
        int windowWidth = Math.Max(1, _context.Window.Window.Width);
        int windowHeight = Math.Max(1, _context.Window.Window.Height);
        bool hasValidSceneViewport =
            float.IsFinite(_sceneViewportMin.X) &&
            float.IsFinite(_sceneViewportMin.Y) &&
            _sceneViewportSize.X >= 96f &&
            _sceneViewportSize.Y >= 96f;

        int viewportX = hasValidSceneViewport
            ? (int)Math.Clamp(MathF.Floor(_sceneViewportMin.X), 0f, windowWidth - 1f)
            : 0;
        int viewportY = hasValidSceneViewport
            ? (int)Math.Clamp(MathF.Floor(_sceneViewportMin.Y), 0f, windowHeight - 1f)
            : 0;
        int viewportWidth = hasValidSceneViewport
            ? (int)Math.Clamp(MathF.Ceiling(_sceneViewportSize.X), 1f, windowWidth - viewportX)
            : windowWidth;
        int viewportHeight = hasValidSceneViewport
            ? (int)Math.Clamp(MathF.Ceiling(_sceneViewportSize.Y), 1f, windowHeight - viewportY)
            : windowHeight;
        float aspect = viewportWidth / (float)viewportHeight;

        _view = Matrix4x4.CreateLookAt(_cameraPosition, _cameraPosition + CameraForward, Vector3.UnitY);
        _proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, aspect, 0.05f, 500f);
        Matrix4x4 viewProj = _view * _proj;

        renderer.CommandList.SetViewport(0, new Viewport(viewportX, viewportY, viewportWidth, viewportHeight, 0f, 1f));
        renderer.CommandList.SetScissorRect(0, (uint)viewportX, (uint)viewportY, (uint)viewportWidth, (uint)viewportHeight);

        _world.BeginFrame();
        _world.UpdateCamera(viewProj, _cameraPosition);
        LevelLighting.Apply(_world, _editor, _cameraPosition);

        DrawGrid(renderer);

        for (int i = 0; i < _editor.DrawBoxes.Count; i++)
        {
            EditorDrawBox draw = _editor.DrawBoxes[i];
            bool selected = i == _editor.SelectedEntityIndex;
            bool selectedHierarchy = selected || IsChildOfSelectedEntity(i);
            bool drewModel = TryDrawEntityModel(renderer, i, draw, selected, selectedHierarchy);
            if (drewModel)
            {
                if (_editor.ShowColliders)
                    DrawColliderOverlay(renderer, draw, selectedHierarchy);

                DrawSelectionOutline(renderer, draw, selected, selectedHierarchy);
                continue;
            }

            bool invisibleHelper = draw.Color.W <= 0.01f;
            if (invisibleHelper && !selectedHierarchy)
            {
                if (_editor.ShowColliders)
                    DrawColliderOverlay(renderer, draw, selected: false);
                continue;
            }

            if (!invisibleHelper)
            {
                if (draw.IsSphere)
                    DrawSphere(renderer, draw.Position, MathF.Max(0.05f, draw.Size.X * 0.5f), draw.Color);
                else
                    DrawBox(renderer, draw.Position, draw.Size, draw.Rotation, draw.Color);
            }

            DrawSelectionOutline(renderer, draw, selected, selectedHierarchy);
        }
        if (_editor.HasGizmo(out EditorDrawBox xLine, out EditorDrawBox xHandle,
                             out EditorDrawBox yLine, out EditorDrawBox yHandle,
                             out EditorDrawBox zLine, out EditorDrawBox zHandle))
        {
            DrawBox(renderer, xLine.Position, xLine.Size, xLine.Rotation, xLine.Color);
            DrawBox(renderer, xHandle.Position, xHandle.Size, xHandle.Rotation, xHandle.Color);
            DrawBox(renderer, yLine.Position, yLine.Size, yLine.Rotation, yLine.Color);
            DrawBox(renderer, yHandle.Position, yHandle.Size, yHandle.Rotation, yHandle.Color);
            DrawBox(renderer, zLine.Position, zLine.Size, zLine.Rotation, zLine.Color);
            DrawBox(renderer, zHandle.Position, zHandle.Size, zHandle.Rotation, zHandle.Color);
        }

        for (int i = 0; i < _editor.VertexSnapMarkers.Count; i++)
        {
            EditorDrawBox marker = _editor.VertexSnapMarkers[i];
            DrawBox(renderer, marker.Position, marker.Size, marker.Rotation, marker.Color);
        }
        renderer.CommandList.SetViewport(0, new Viewport(0f, 0f, windowWidth, windowHeight, 0f, 1f));
        renderer.CommandList.SetScissorRect(0, 0, 0, (uint)windowWidth, (uint)windowHeight);
    }

    private bool TryDrawEntityModel(Renderer renderer, int entityIndex, EditorDrawBox draw, bool selected, bool selectedHierarchy)
    {
        if (!_drawSceneGlbModels)
            return false;

        if (entityIndex < 0 || entityIndex >= _editor.LevelFile.Entities.Count)
            return false;

        LevelEntityDef entity = _editor.LevelFile.Entities[entityIndex];
        if (!IsGlbAssetPath(entity.MeshPath))
            return false;

        if (!TryGetEditorSceneModel(entity.MeshPath, out EditorSceneModelEntry entry) || entry.RenderModel == null || entry.LoadedModel == null)
            return false;

        try
        {
            Matrix4x4 transform = CreateBoundsFitTransform(entry.Min, entry.Max, draw.Position, draw.Size, draw.Rotation);
            _world.DrawModel(renderer.CommandList, entry.RenderModel, transform, Vector4.One);
            return true;
        }
        catch (Exception ex)
        {
            entry.Failed = true;
            entry.Error = ex.Message;
            return false;
        }
    }

    private bool IsChildOfSelectedEntity(int entityIndex)
    {
        int selectedIndex = _editor.SelectedEntityIndex;
        if (selectedIndex < 0 || selectedIndex >= _editor.LevelFile.Entities.Count)
            return false;
        if (entityIndex < 0 || entityIndex >= _editor.LevelFile.Entities.Count)
            return false;

        string selectedId = _editor.LevelFile.Entities[selectedIndex].Id;
        string? parentId = _editor.LevelFile.Entities[entityIndex].ParentId;
        const int maxDepth = 128;
        int depth = 0;

        while (!string.IsNullOrWhiteSpace(parentId) && depth++ < maxDepth)
        {
            if (string.Equals(parentId, selectedId, StringComparison.OrdinalIgnoreCase))
                return true;

            int parentIndex = -1;
            for (int i = 0; i < _editor.LevelFile.Entities.Count; i++)
            {
                if (string.Equals(_editor.LevelFile.Entities[i].Id, parentId, StringComparison.OrdinalIgnoreCase))
                {
                    parentIndex = i;
                    break;
                }
            }

            if (parentIndex < 0)
                return false;

            parentId = _editor.LevelFile.Entities[parentIndex].ParentId;
        }

        return false;
    }
    private bool TryGetEditorSceneModel(string modelPath, out EditorSceneModelEntry entry)
    {
        string absolutePath = ResolveContentAssetPath(modelPath);
        if (_sceneModelCache.TryGetValue(absolutePath, out entry!))
        {
            if (entry.RenderModel != null)
                return true;

            if (entry.Failed)
                return false;

            if (entry.LoadTask is { IsCompleted: true } task)
            {
                if (task.IsCompletedSuccessfully)
                {
                    try
                    {
                        LoadedModel loaded = task.Result;
                        ComputeModelBounds(loaded, out Vector3 min, out Vector3 max);
                        entry.LoadedModel = loaded;
                        entry.RenderModel = _world.CreateRenderModel(loaded, loadTextures: true);
                        entry.Min = min;
                        entry.Max = max;
                        entry.LoadTask = null;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        entry.Failed = true;
                        entry.Error = ex.Message;
                        entry.LoadTask = null;
                    }
                }
                else
                {
                    entry.Failed = true;
                    entry.Error = task.Exception?.GetBaseException().Message ?? "model load failed";
                    entry.LoadTask = null;
                }
            }

            return false;
        }

        entry = new EditorSceneModelEntry();
        if (!File.Exists(absolutePath))
        {
            entry.Failed = true;
            entry.Error = "file not found";
            _sceneModelCache[absolutePath] = entry;
            return false;
        }

        entry.LoadTask = Task.Run(() => GlbModelLoader.Load(absolutePath));
        _sceneModelCache[absolutePath] = entry;
        return false;
    }

    private string ResolveContentAssetPath(string assetPath)
    {
        if (Path.IsPathRooted(assetPath))
            return Path.GetFullPath(assetPath);

        string normalized = assetPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string contentPrefix = "Content" + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(contentPrefix, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(Path.Combine(_contentRoot, normalized[contentPrefix.Length..]));

        return ToAbsolutePath(assetPath);
    }

    private static bool IsGlbAssetPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

    private static Matrix4x4 CreateBoundsFitTransform(Vector3 min, Vector3 max, Vector3 position, Vector3 size, Quaternion rotation)
    {
        Vector3 center = (min + max) * 0.5f;
        Vector3 boundsSize = Vector3.Max(max - min, new Vector3(0.0001f));
        Vector3 scale = new(
            MathF.Abs(size.X) / boundsSize.X,
            MathF.Abs(size.Y) / boundsSize.Y,
            MathF.Abs(size.Z) / boundsSize.Z);

        return Matrix4x4.CreateTranslation(-center) *
               Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateFromQuaternion(rotation) *
               Matrix4x4.CreateTranslation(position);
    }

    private void DisposeEditorSceneModelCache()
    {
        foreach (EditorSceneModelEntry entry in _sceneModelCache.Values)
            entry.Dispose();

        _sceneModelCache.Clear();
    }
    private void DrawGrid(Renderer renderer)
    {
        const int halfLines = 20;
        Vector4 grid = new(0.20f, 0.22f, 0.24f, 1f);
        Vector4 xAxis = new(0.52f, 0.12f, 0.10f, 1f);
        Vector4 zAxis = new(0.12f, 0.24f, 0.55f, 1f);

        for (int i = -halfLines; i <= halfLines; i++)
        {
            DrawBox(renderer, new Vector3(0f, -0.01f, i), new Vector3(halfLines * 2f, 0.015f, 0.015f), Quaternion.Identity, i == 0 ? zAxis : grid);
            DrawBox(renderer, new Vector3(i, -0.008f, 0f), new Vector3(0.015f, 0.015f, halfLines * 2f), Quaternion.Identity, i == 0 ? xAxis : grid);
        }
    }

    private void DrawColliderOverlay(Renderer renderer, EditorDrawBox draw, bool selected)
    {
        Vector4 color = selected
            ? new Vector4(1f, 1f, 0.12f, 0.16f)
            : new Vector4(0.38f, 0.74f, 1f, 0.08f);

        if (draw.IsSphere)
            DrawSphere(renderer, draw.Position, MathF.Max(0.05f, draw.Size.X * 0.5f), color);
        else
            DrawBox(renderer, draw.Position, draw.Size, draw.Rotation, color);
    }
    private void DrawSelectionOutline(Renderer renderer, EditorDrawBox draw, bool selected, bool selectedHierarchy)
    {
        if (!selectedHierarchy)
            return;

        Vector4 color = selected
            ? new Vector4(1f, 0.92f, 0.16f, 1f)
            : new Vector4(0.2f, 0.78f, 1f, 0.9f);
        float thickness = selected ? 0.035f : 0.025f;
        float cornerSize = selected ? 0.12f : 0.09f;
        Quaternion rotation = draw.IsSphere ? Quaternion.Identity : draw.Rotation;

        DrawWireObb(renderer, draw.Position, draw.Size, rotation, color, thickness);
        DrawObbCorners(renderer, draw.Position, draw.Size, rotation, color, cornerSize);
    }

    private void DrawObbCorners(Renderer renderer, Vector3 center, Vector3 size, Quaternion rotation, Vector4 color, float cornerSize)
    {
        Vector3 he = new(MathF.Abs(size.X) * 0.5f, MathF.Abs(size.Y) * 0.5f, MathF.Abs(size.Z) * 0.5f);
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(-he.X, -he.Y, -he.Z);
        corners[1] = new Vector3( he.X, -he.Y, -he.Z);
        corners[2] = new Vector3( he.X, -he.Y,  he.Z);
        corners[3] = new Vector3(-he.X, -he.Y,  he.Z);
        corners[4] = new Vector3(-he.X,  he.Y, -he.Z);
        corners[5] = new Vector3( he.X,  he.Y, -he.Z);
        corners[6] = new Vector3( he.X,  he.Y,  he.Z);
        corners[7] = new Vector3(-he.X,  he.Y,  he.Z);

        Vector3 cube = new(cornerSize);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 world = Vector3.Transform(corners[i], rotation) + center;
            DrawBox(renderer, world, cube, Quaternion.Identity, color);
        }
    }

    private void DrawWireObb(Renderer renderer, Vector3 center, Vector3 size, Quaternion rotation, Vector4 color, float thickness)
    {
        Vector3 he = new(MathF.Abs(size.X) * 0.5f, MathF.Abs(size.Y) * 0.5f, MathF.Abs(size.Z) * 0.5f);
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(-he.X, -he.Y, -he.Z);
        corners[1] = new Vector3( he.X, -he.Y, -he.Z);
        corners[2] = new Vector3( he.X, -he.Y,  he.Z);
        corners[3] = new Vector3(-he.X, -he.Y,  he.Z);
        corners[4] = new Vector3(-he.X,  he.Y, -he.Z);
        corners[5] = new Vector3( he.X,  he.Y, -he.Z);
        corners[6] = new Vector3( he.X,  he.Y,  he.Z);
        corners[7] = new Vector3(-he.X,  he.Y,  he.Z);

        for (int i = 0; i < corners.Length; i++)
            corners[i] = Vector3.Transform(corners[i], rotation) + center;

        Span<(int A, int B)> edges = stackalloc (int, int)[12]
        {
            (0, 1), (1, 2), (2, 3), (3, 0),
            (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        };

        for (int i = 0; i < edges.Length; i++)
            DrawEdgeBox(renderer, corners[edges[i].A], corners[edges[i].B], thickness, color);
    }

    private void DrawEdgeBox(Renderer renderer, Vector3 a, Vector3 b, float thickness, Vector4 color)
    {
        Vector3 dir = b - a;
        float length = dir.Length();
        if (length < 0.0001f)
            return;

        dir /= length;
        Quaternion rotation = FromToRotation(Vector3.UnitZ, dir);
        DrawBox(renderer, (a + b) * 0.5f, new Vector3(thickness, thickness, length), rotation, color);
    }

    private static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        float dot = Vector3.Dot(from, to);

        if (dot > 0.9999f)
            return Quaternion.Identity;

        if (dot < -0.9999f)
        {
            Vector3 axis = Vector3.Cross(from, Vector3.UnitX);
            if (axis.LengthSquared() < 0.0001f)
                axis = Vector3.Cross(from, Vector3.UnitY);

            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }

        Vector3 cross = Vector3.Normalize(Vector3.Cross(from, to));
        float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        return Quaternion.CreateFromAxisAngle(cross, angle);
    }
    private void DrawBox(Renderer renderer, Vector3 position, Vector3 size, Quaternion rotation, Vector4 color)
    {
        Matrix4x4 model = Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        _world.DrawBox(renderer.CommandList, model, color);
    }

    private void DrawSphere(Renderer renderer, Vector3 position, float radius, Vector4 color)
    {
        Matrix4x4 model = Matrix4x4.CreateScale(new Vector3(radius * 2f)) * Matrix4x4.CreateTranslation(position);
        _world.DrawSphere(renderer.CommandList, model, color);
    }
}




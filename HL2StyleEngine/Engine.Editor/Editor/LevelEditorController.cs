using Editor.Editor;
using Engine.Editor.Level;
using Engine.Physics.Collision;
using ImGuiNET;
using System.Numerics;
using System.Text.Json;

namespace Engine.Editor.Editor;

public sealed class LevelEditorController
{
    public LevelFile LevelFile { get; private set; } = null!;
    public string LevelPath { get; private set; } = "";

    public bool Dirty { get; private set; }
    public int SelectedEntityIndex { get; private set; } = -1;

    public readonly List<EditorDrawBox> DrawBoxes = new();
    public readonly List<Aabb> SolidColliders = new();
    public readonly List<(string eventName, Aabb aabb, bool wasInside)> Triggers = new();

    public string LastTriggerEvent { get; private set; } = "";

    public bool GizmoEnabled = true;
    public bool SnapEnabled = false;
    public float SnapStep = 0.25f;

    public float GizmoAxisLen = 1.5f;
    public float GizmoAxisThickness = 0.05f;
    public float GizmoHandleSize = 0.25f;

    public float MarkerSmall = 0.25f;
    public float MarkerMedium = 0.35f;

    public bool ShowColliders = true;
    public bool ShowColliderCorners = true;
    public bool ShowPhysicsAabbs = true;
    public float ColliderLineThickness = 0.03f;
    public float CornerSize = 0.08f;

    private enum GizmoAxis { None, X, Y, Z }

    private bool _dragging;
    private GizmoAxis _dragAxis = GizmoAxis.None;

    private Vector3 _dragOffset;
    private float _dragPlaneY;

    private Vector3 _axisOriginAtGrab;
    private float _axisGrabT;
    private Vector3 _entityPosAtGrab;

    private bool _layoutDockedOnce = false;

    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();

    private bool _editInProgress;
    private string _editStartSnapshot = "";

    public void LoadOrCreate(string path, Func<LevelFile> createDefault)
    {
        LevelPath = path;
        LevelFile = LevelIO.LoadOrCreate(path, createDefault);

        Dirty = false;
        SelectedEntityIndex = Math.Clamp(SelectedEntityIndex, -1, LevelFile.Entities.Count - 1);

        _dragging = false;
        _dragAxis = GizmoAxis.None;

        _undoStack.Clear();
        _redoStack.Clear();
        _editInProgress = false;
        _editStartSnapshot = "";

        RebuildRuntimeFromLevel();
    }

    public void Reload()
    {
        LevelFile = LevelIO.Load(LevelPath);
        Dirty = false;

        SelectedEntityIndex = Math.Clamp(SelectedEntityIndex, -1, LevelFile.Entities.Count - 1);

        _dragging = false;
        _dragAxis = GizmoAxis.None;

        _undoStack.Clear();
        _redoStack.Clear();
        _editInProgress = false;
        _editStartSnapshot = "";

        RebuildRuntimeFromLevel();
    }

    public void Save()
    {
        LevelIO.Save(LevelPath, LevelFile);
        Dirty = false;
    }

    public bool TryGetPlayerSpawn(out Vector3 feetPos, out float yawDeg)
    {
        for (int i = 0; i < LevelFile.Entities.Count; i++)
        {
            var e = LevelFile.Entities[i];
            if (e.Type == EntityTypes.PlayerSpawn)
            {
                feetPos = (Vector3)e.Position;
                yawDeg = e.YawDeg;
                return true;
            }
        }

        feetPos = default;
        yawDeg = 0f;
        return false;
    }

    public void TickTriggers(Vector3 playerPoint)
    {
        for (int i = 0; i < Triggers.Count; i++)
        {
            var (eventName, aabb, wasInside) = Triggers[i];

            bool inside =
                playerPoint.X >= aabb.Min.X && playerPoint.X <= aabb.Max.X &&
                playerPoint.Y >= aabb.Min.Y && playerPoint.Y <= aabb.Max.Y &&
                playerPoint.Z >= aabb.Min.Z && playerPoint.Z <= aabb.Max.Z;

            if (!wasInside && inside)
                LastTriggerEvent = eventName;

            Triggers[i] = (eventName, aabb, inside);
        }
    }

    public bool DuplicateSelected()
    {
        if (SelectedEntityIndex < 0 || SelectedEntityIndex >= LevelFile.Entities.Count)
            return false;

        PushUndoSnapshot();

        var src = LevelFile.Entities[SelectedEntityIndex];
        var copy = CloneEntity(src);

        copy.Position = (Vector3)copy.Position + new Vector3(0.5f, 0f, 0.5f);

        LevelFile.Entities.Add(copy);
        SelectedEntityIndex = LevelFile.Entities.Count - 1;

        Dirty = true;
        RebuildRuntimeFromLevel();
        return true;
    }

    public bool DeleteSelected()
    {
        if (SelectedEntityIndex < 0 || SelectedEntityIndex >= LevelFile.Entities.Count)
            return false;

        PushUndoSnapshot();

        LevelFile.Entities.RemoveAt(SelectedEntityIndex);
        SelectedEntityIndex = Math.Clamp(SelectedEntityIndex, -1, LevelFile.Entities.Count - 1);

        Dirty = true;
        RebuildRuntimeFromLevel();
        return true;
    }

    private static LevelEntityDef CloneEntity(LevelEntityDef src)
    {
        return new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = src.Type,
            Name = (src.Name ?? "Entity") + "_copy",

            Position = src.Position,
            RotationEulerDeg = src.RotationEulerDeg,
            Scale = src.Scale,

            Size = src.Size,
            Color = src.Color,

            YawDeg = src.YawDeg,

            LightColor = src.LightColor,
            Intensity = src.Intensity,
            Range = src.Range,

            MeshPath = src.MeshPath,
            MaterialPath = src.MaterialPath,

            TriggerSize = src.TriggerSize,
            TriggerEvent = src.TriggerEvent,

            Shape = src.Shape,
            Mass = src.Mass,
            Friction = src.Friction,
            Restitution = src.Restitution,
            IsKinematic = src.IsKinematic,

            Radius = src.Radius,
            Height = src.Height
        };
    }

    public void DrawToolbarPanel(ref bool mouseOverUi, ref bool keyboardOverUi)
    {
        ImGui.Begin("Toolbar");
        mouseOverUi |= ImGui.IsWindowHovered();
        keyboardOverUi |= ImGui.IsWindowFocused();

        if (ImGui.Button("Undo")) Undo();
        ImGui.SameLine();
        if (ImGui.Button("Redo")) Redo();
        ImGui.Separator();
        if (ImGui.Button("Save")) Save();
        ImGui.SameLine();
        if (ImGui.Button("Reload")) Reload();

        ImGui.SameLine();
        ImGui.TextDisabled(Dirty ? "Dirty: YES" : "Dirty: NO");

        ImGui.Separator();

        ImGui.Checkbox("Gizmo", ref GizmoEnabled);
        ImGui.SameLine();
        ImGui.Checkbox("Snap", ref SnapEnabled);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.DragFloat("Step", ref SnapStep, 0.01f, 0.01f, 10f);

        ImGui.Separator();
        ImGui.Text("Debug Draw");
        ImGui.Checkbox("Show Colliders (OBB)", ref ShowColliders);
        ImGui.SameLine();
        ImGui.Checkbox("Corners", ref ShowColliderCorners);
        ImGui.SameLine();
        ImGui.Checkbox("Physics AABBs", ref ShowPhysicsAabbs);

        ImGui.Separator();

        if (ImGui.Button("Add Box")) AddBox();
        ImGui.SameLine();
        if (ImGui.Button("Add Light")) AddLight();
        ImGui.SameLine();
        if (ImGui.Button("Add Spawn")) AddSpawn();
        ImGui.SameLine();
        if (ImGui.Button("Add Trigger")) AddTrigger();
        ImGui.SameLine();
        if (ImGui.Button("Add Prop")) AddProp();
        ImGui.SameLine();
        if (ImGui.Button("Add RigidBody")) AddRigidBody();

        ImGui.Separator();

        bool hasSelection = SelectedEntityIndex >= 0 && SelectedEntityIndex < LevelFile.Entities.Count;

        if (!hasSelection) ImGui.BeginDisabled();
        if (ImGui.Button("Duplicate (Ctrl+D)"))
            DuplicateSelected();
        ImGui.SameLine();
        if (ImGui.Button("Delete"))
            DeleteSelected();
        if (!hasSelection) ImGui.EndDisabled();

        ImGui.End();
    }

    public void DrawHierarchyPanel(ref bool mouseOverUi, ref bool keyboardOverUi)
    {
        ImGui.Begin("Hierarchy");
        mouseOverUi |= ImGui.IsWindowHovered();
        keyboardOverUi |= ImGui.IsWindowFocused();

        ImGui.Text($"Level: {LevelPath}");
        ImGui.Separator();

        ImGui.BeginChild("entity_list", new Vector2(0, 0), ImGuiChildFlags.Borders);

        for (int i = 0; i < LevelFile.Entities.Count; i++)
        {
            var e = LevelFile.Entities[i];
            bool selected = i == SelectedEntityIndex;
            string label = $"{i:00} [{e.Type}] {e.Name}##{e.Id}";

            if (ImGui.Selectable(label, selected))
                SelectedEntityIndex = i;
        }

        ImGui.EndChild();
        ImGui.End();
    }

    public void DrawInspectorPanel(ref bool mouseOverUi, ref bool keyboardOverUi)
    {
        ImGui.Begin("Inspector");
        mouseOverUi |= ImGui.IsWindowHovered();
        keyboardOverUi |= ImGui.IsWindowFocused();

        bool hasSelection = SelectedEntityIndex >= 0 && SelectedEntityIndex < LevelFile.Entities.Count;
        if (!hasSelection)
        {
            ImGui.Text("Select an entity.");
            ImGui.Separator();
            DrawHelp();
            ImGui.End();
            return;
        }

        var ent = LevelFile.Entities[SelectedEntityIndex];

        ImGui.Text($"Selected: {SelectedEntityIndex}");
        ImGui.Text($"Type: {ent.Type}");
        ImGui.Text($"Id: {ent.Id}");

        ImGui.Separator();

        string name = ent.Name ?? "";
        if (ImGui.InputText("Name", ref name, 128))
        {
            ent.Name = name;
            Dirty = true;
        }
        if (ImGui.IsItemActivated()) BeginEdit();
        if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

        ImGui.Separator();
        ImGui.Text("Transform");

        Vector3 pos = ent.Position;
        if (ImGui.DragFloat3("Position", ref pos, 0.05f))
        {
            ent.Position = ApplySnapping(pos, GizmoAxis.None, ctrlDown: false);
            Dirty = true;
            RebuildRuntimeFromLevel();
        }
        if (ImGui.IsItemActivated()) BeginEdit();
        if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

        bool showRotScale =
            ent.Type == EntityTypes.Box ||
            ent.Type == EntityTypes.Prop ||
            ent.Type == EntityTypes.RigidBody;

        if (showRotScale)
        {
            Vector3 rot = ent.RotationEulerDeg;
            if (ImGui.DragFloat3("Rotation (deg)", ref rot, 1f))
            {
                ent.RotationEulerDeg = rot;
                Dirty = true;
                RebuildRuntimeFromLevel();
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            Vector3 scl = ent.Scale;
            if (ImGui.DragFloat3("Scale", ref scl, 0.01f))
            {
                scl.X = MathF.Max(0.01f, scl.X);
                scl.Y = MathF.Max(0.01f, scl.Y);
                scl.Z = MathF.Max(0.01f, scl.Z);

                ent.Scale = scl;
                Dirty = true;
                RebuildRuntimeFromLevel();
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();
        }

        ImGui.Separator();
        DrawTypeSpecificInspector(ent);

        ImGui.Separator();
        DrawHelp();

        ImGui.End();
    }

    private static void DrawHelp()
    {
        ImGui.Text("Controls:");
        ImGui.BulletText("LMB on entity marker: XZ drag");
        ImGui.BulletText("LMB on axis handle: axis drag (X/Y/Z)");
        ImGui.BulletText("RMB: look  |  WASD/QE: move  |  Shift: fast");
        ImGui.BulletText("Snap: checkbox, hold Ctrl to disable");
        ImGui.BulletText("Duplicate: Ctrl+D");
        ImGui.BulletText("Undo: Ctrl+Z  |  Redo: Ctrl+Y / Ctrl+Shift+Z");
    }

    private void DrawTypeSpecificInspector(LevelEntityDef ent)
    {
        if (ent.Type == EntityTypes.Box)
        {
            Vector3 size = ent.Size;
            if (ImGui.DragFloat3("Size", ref size, 0.05f))
            {
                size.X = MathF.Max(0.01f, size.X);
                size.Y = MathF.Max(0.01f, size.Y);
                size.Z = MathF.Max(0.01f, size.Z);
                ent.Size = size;

                Dirty = true;
                RebuildRuntimeFromLevel();
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            Vector4 col = ent.Color;
            if (ImGui.ColorEdit4("Color", ref col))
            {
                ent.Color = col;
                Dirty = true;
                RebuildRuntimeFromLevel();
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();
        }
        else if (ent.Type == EntityTypes.PlayerSpawn)
        {
            float yaw = ent.YawDeg;
            if (ImGui.DragFloat("YawDeg", ref yaw, 1f))
            {
                ent.YawDeg = yaw;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();
        }
        else if (ent.Type == EntityTypes.PointLight)
        {
            Vector4 lc = ent.LightColor;
            if (ImGui.ColorEdit4("LightColor", ref lc))
            {
                ent.LightColor = lc;
                Dirty = true;
                RebuildRuntimeFromLevel();
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            float intensity = ent.Intensity;
            if (ImGui.DragFloat("Intensity", ref intensity, 0.1f, 0f, 100f))
            {
                ent.Intensity = intensity;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            float range = ent.Range;
            if (ImGui.DragFloat("Range", ref range, 0.1f, 0.1f, 1000f))
            {
                ent.Range = range;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();
        }
        else if (ent.Type == EntityTypes.Prop)
        {
            string mesh = ent.MeshPath ?? "";
            if (ImGui.InputText("MeshPath", ref mesh, 256))
            {
                ent.MeshPath = mesh;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            string mat = ent.MaterialPath ?? "";
            if (ImGui.InputText("MaterialPath", ref mat, 256))
            {
                ent.MaterialPath = mat;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();
        }
        else if (ent.Type == EntityTypes.TriggerVolume)
        {
            Vector3 tsize = ent.TriggerSize;
            if (ImGui.DragFloat3("TriggerSize", ref tsize, 0.05f))
            {
                tsize.X = MathF.Max(0.01f, tsize.X);
                tsize.Y = MathF.Max(0.01f, tsize.Y);
                tsize.Z = MathF.Max(0.01f, tsize.Z);
                ent.TriggerSize = tsize;

                Dirty = true;
                RebuildRuntimeFromLevel();
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            string evt = ent.TriggerEvent ?? "";
            if (ImGui.InputText("TriggerEvent", ref evt, 128))
            {
                ent.TriggerEvent = evt;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();
        }
        else if (ent.Type == EntityTypes.RigidBody)
        {
            string shape = ent.Shape ?? "Box";
            if (ImGui.InputText("Shape", ref shape, 32))
            {
                ent.Shape = shape;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            float mass = ent.Mass;
            if (ImGui.DragFloat("Mass", ref mass, 0.1f, 0f, 100000f))
            {
                ent.Mass = mass;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            float fr = ent.Friction;
            if (ImGui.DragFloat("Friction", ref fr, 0.01f, 0f, 10f))
            {
                ent.Friction = fr;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            float rest = ent.Restitution;
            if (ImGui.DragFloat("Restitution", ref rest, 0.01f, 0f, 1f))
            {
                ent.Restitution = rest;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            bool kin = ent.IsKinematic;
            if (ImGui.Checkbox("IsKinematic", ref kin))
            {
                ent.IsKinematic = kin;
                Dirty = true;
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();

            Vector3 size = ent.Size;
            if (ImGui.DragFloat3("Size", ref size, 0.05f))
            {
                size.X = MathF.Max(0.01f, size.X);
                size.Y = MathF.Max(0.01f, size.Y);
                size.Z = MathF.Max(0.01f, size.Z);
                ent.Size = size;

                Dirty = true;
                RebuildRuntimeFromLevel();
            }
            if (ImGui.IsItemActivated()) BeginEdit();
            if (ImGui.IsItemDeactivatedAfterEdit()) EndEditIfAny();
        }
    }

    public void OnMousePressed(EditorPicking.Ray ray, bool ctrlDown)
    {
        BeginEdit();
        if (GizmoEnabled && SelectedEntityIndex >= 0 && SelectedEntityIndex < LevelFile.Entities.Count)
        {
            Vector3 p = LevelFile.Entities[SelectedEntityIndex].Position;

            if (RayHitsAxisHandle(ray, p, GizmoAxis.Y)) { BeginAxisDrag(ray, p, GizmoAxis.Y); return; }
            if (RayHitsAxisHandle(ray, p, GizmoAxis.X)) { BeginAxisDrag(ray, p, GizmoAxis.X); return; }
            if (RayHitsAxisHandle(ray, p, GizmoAxis.Z)) { BeginAxisDrag(ray, p, GizmoAxis.Z); return; }
        }

        if (TryPickEntity(ray, out int hitIndex, out Vector3 hitPoint))
        {
            SelectedEntityIndex = hitIndex;

            _dragPlaneY = hitPoint.Y;
            Vector3 pos = LevelFile.Entities[SelectedEntityIndex].Position;
            _dragOffset = hitPoint - pos;

            _dragging = true;
            _dragAxis = GizmoAxis.None;

            BeginEdit(); 
        }
        else
        {
            SelectedEntityIndex = -1;
            _dragging = false;
            _dragAxis = GizmoAxis.None;
        }
    }

    public void OnMouseHeld(EditorPicking.Ray ray, bool leftDown, bool ctrlDown)
    {
        if (!_dragging || !leftDown)
            return;

        if (SelectedEntityIndex < 0 || SelectedEntityIndex >= LevelFile.Entities.Count)
            return;

        if (_dragAxis != GizmoAxis.None)
            ContinueAxisDrag(ray, ctrlDown);
        else
            ContinueXZDrag(ray, ctrlDown);
    }

    public void OnMouseReleased()
    {
        if (_dragging)
            EndEditIfAny();

        _dragging = false;
        _dragAxis = GizmoAxis.None;
        EndEditIfAny();
    }

    public bool HasGizmo(out EditorDrawBox xLine, out EditorDrawBox xHandle,
                         out EditorDrawBox yLine, out EditorDrawBox yHandle,
                         out EditorDrawBox zLine, out EditorDrawBox zHandle)
    {
        xLine = xHandle = yLine = yHandle = zLine = zHandle = default;

        if (!GizmoEnabled || SelectedEntityIndex < 0 || SelectedEntityIndex >= LevelFile.Entities.Count)
            return false;

        Vector3 p = LevelFile.Entities[SelectedEntityIndex].Position;

        BuildAxis(p, Vector3.UnitX, out xLine, out xHandle, active: _dragAxis == GizmoAxis.X);
        BuildAxis(p, Vector3.UnitY, out yLine, out yHandle, active: _dragAxis == GizmoAxis.Y);
        BuildAxis(p, Vector3.UnitZ, out zLine, out zHandle, active: _dragAxis == GizmoAxis.Z);

        return true;
    }

    private void BuildAxis(Vector3 origin, Vector3 dir, out EditorDrawBox line, out EditorDrawBox handle, bool active)
    {
        Vector4 col =
            dir == Vector3.UnitX ? new Vector4(1f, 0.2f, 0.2f, 1f) :
            dir == Vector3.UnitY ? new Vector4(0.2f, 1f, 0.2f, 1f) :
                                   new Vector4(0.2f, 0.4f, 1f, 1f);

        Vector3 lineCenter = origin + dir * (GizmoAxisLen * 0.5f);

        Vector3 size = dir == Vector3.UnitY ? new Vector3(GizmoAxisThickness, GizmoAxisLen, GizmoAxisThickness) :
            dir == Vector3.UnitX ? new Vector3(GizmoAxisLen, GizmoAxisThickness, GizmoAxisThickness) :
                                   new Vector3(GizmoAxisThickness, GizmoAxisThickness, GizmoAxisLen);

        line = EditorDrawBox.AxisAligned(lineCenter, size, col);

        Vector3 handleCenter = origin + dir * GizmoAxisLen;
        Vector3 handleSize = new(GizmoHandleSize, GizmoHandleSize, GizmoHandleSize);

        Vector4 handleCol = active ? new Vector4(1f, 1f, 0.1f, 1f) : col;
        handle = EditorDrawBox.AxisAligned(handleCenter, handleSize, handleCol);
    }

    public void RebuildRuntimeFromLevel()
    {
        DrawBoxes.Clear();
        SolidColliders.Clear();
        Triggers.Clear();

        Vector3 markerSmall = new(MarkerSmall, MarkerSmall, MarkerSmall);
        Vector3 markerMed = new(MarkerMedium, MarkerMedium, MarkerMedium);

        foreach (var e in LevelFile.Entities)
        {
            if (e.Type == EntityTypes.Box)
            {
                Vector3 pos = e.Position;
                Vector3 size = Mul((Vector3)e.Size, (Vector3)e.Scale);
                Quaternion rot = EulerDegToQuat((Vector3)e.RotationEulerDeg);

                DrawBoxes.Add(new EditorDrawBox(pos, size, (Vector4)e.Color, rot));

                Vector3 halfAabb = OrientedBoxAabbHalfExtents(size * 0.5f, rot);
                SolidColliders.Add(new Aabb(pos - halfAabb, pos + halfAabb));
            }
            else if (e.Type == EntityTypes.TriggerVolume)
            {
                Vector3 pos = e.Position;
                DrawBoxes.Add(EditorDrawBox.AxisAligned(pos, markerMed, new Vector4(0.1f, 0.9f, 0.1f, 1f)));

                Vector3 half = ((Vector3)e.TriggerSize) * 0.5f;
                var aabb = new Aabb(pos - half, pos + half);
                Triggers.Add((e.TriggerEvent, aabb, false));
            }
            else if (e.Type == EntityTypes.PointLight)
            {
                DrawBoxes.Add(EditorDrawBox.AxisAligned((Vector3)e.Position, markerSmall, (Vector4)e.LightColor));
            }
            else if (e.Type == EntityTypes.PlayerSpawn)
            {
                DrawBoxes.Add(EditorDrawBox.AxisAligned((Vector3)e.Position, markerMed, new Vector4(0.2f, 0.8f, 1f, 1f)));
            }
            else if (e.Type == EntityTypes.Prop)
            {
                Vector3 pos = e.Position;
                Vector3 size = Mul(markerMed, (Vector3)e.Scale);
                Quaternion rot = EulerDegToQuat((Vector3)e.RotationEulerDeg);

                DrawBoxes.Add(new EditorDrawBox(pos, size, new Vector4(0.8f, 0.6f, 0.2f, 1f), rot));
            }
            else if (e.Type == EntityTypes.RigidBody)
            {
                Vector3 pos = e.Position;
                Vector3 size = Mul((Vector3)e.Size, (Vector3)e.Scale);
                Quaternion rot = EulerDegToQuat((Vector3)e.RotationEulerDeg);

                DrawBoxes.Add(new EditorDrawBox(pos, size, new Vector4(0.9f, 0.2f, 0.6f, 1f), rot));

                Vector3 halfAabb = OrientedBoxAabbHalfExtents(size * 0.5f, rot);
                SolidColliders.Add(new Aabb(pos - halfAabb, pos + halfAabb));
            }
            else
            {
                DrawBoxes.Add(EditorDrawBox.AxisAligned((Vector3)e.Position, markerSmall, new Vector4(1f, 0f, 1f, 1f)));
            }
        }
    }

    private bool TryPickEntity(EditorPicking.Ray ray, out int hitIndex, out Vector3 hitPoint)
    {
        hitIndex = -1;
        hitPoint = default;

        float bestT = float.PositiveInfinity;
        int bestIndex = -1;

        for (int i = 0; i < DrawBoxes.Count; i++)
        {
            var inst = DrawBoxes[i];

            Vector3 half = inst.Size * 0.5f;

            Vector3 halfAabb = OrientedBoxAabbHalfExtents(half, inst.Rotation);
            Vector3 mn = inst.Position - halfAabb;
            Vector3 mx = inst.Position + halfAabb;

            if (EditorPicking.RayIntersectsAabb(ray, mn, mx, out float t) && t < bestT)
            {
                bestT = t;
                bestIndex = i;
            }
        }

        if (bestIndex == -1)
            return false;

        hitIndex = bestIndex;
        hitPoint = ray.GetPoint(bestT);
        return true;
    }

    private bool RayHitsAxisHandle(EditorPicking.Ray ray, Vector3 entityPos, GizmoAxis axis)
    {
        Vector3 dir = axis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.UnitY
        };

        Vector3 handleCenter = entityPos + dir * GizmoAxisLen;
        Vector3 half = new Vector3(GizmoHandleSize * 0.5f);

        return EditorPicking.RayIntersectsAabb(ray, handleCenter - half, handleCenter + half, out _);
    }

    private void BeginAxisDrag(EditorPicking.Ray ray, Vector3 entityPos, GizmoAxis axis)
    {
        _dragging = true;
        _dragAxis = axis;

        _axisOriginAtGrab = entityPos;
        _entityPosAtGrab = entityPos;

        Vector3 axisDir = axis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.UnitY
        };

        if (!EditorPicking.ClosestTOnLineToRay(_axisOriginAtGrab, axisDir, ray, out float tLine))
            tLine = 0f;

        _axisGrabT = tLine;

        BeginEdit();
    }

    private void ContinueAxisDrag(EditorPicking.Ray ray, bool ctrlDown)
    {
        Vector3 axisDir = _dragAxis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.UnitY
        };

        if (!EditorPicking.ClosestTOnLineToRay(_axisOriginAtGrab, axisDir, ray, out float tLine))
            return;

        float deltaT = tLine - _axisGrabT;
        Vector3 newPos = _entityPosAtGrab + axisDir * deltaT;

        newPos = ApplySnapping(newPos, _dragAxis, ctrlDown);

        LevelFile.Entities[SelectedEntityIndex].Position = newPos;
        Dirty = true;
        RebuildRuntimeFromLevel();
    }

    private void ContinueXZDrag(EditorPicking.Ray ray, bool ctrlDown)
    {
        if (!EditorPicking.RayIntersectsPlane(ray, Vector3.UnitY, _dragPlaneY, out float t))
            return;

        Vector3 hit = ray.GetPoint(t);
        Vector3 newPos = hit - _dragOffset;

        newPos = ApplySnapping(newPos, GizmoAxis.None, ctrlDown);

        LevelFile.Entities[SelectedEntityIndex].Position = newPos;
        Dirty = true;
        RebuildRuntimeFromLevel();
    }

    private Vector3 ApplySnapping(Vector3 pos, GizmoAxis axis, bool ctrlDown)
    {
        if (!SnapEnabled || ctrlDown || SnapStep <= 0.0001f)
            return pos;

        float Snap(float v) => MathF.Round(v / SnapStep) * SnapStep;

        if (axis == GizmoAxis.None)
        {
            pos.X = Snap(pos.X);
            pos.Y = Snap(pos.Y);
            pos.Z = Snap(pos.Z);
            return pos;
        }

        if (axis == GizmoAxis.X) pos.X = Snap(pos.X);
        if (axis == GizmoAxis.Y) pos.Y = Snap(pos.Y);
        if (axis == GizmoAxis.Z) pos.Z = Snap(pos.Z);

        return pos;
    }

    private static Quaternion EulerDegToQuat(Vector3 eulerDeg)
    {
        float yaw = MathF.PI / 180f * eulerDeg.Y;
        float pitch = MathF.PI / 180f * eulerDeg.X;
        float roll = MathF.PI / 180f * eulerDeg.Z;

        return Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
    }

    private static Vector3 OrientedBoxAabbHalfExtents(Vector3 halfExtents, Quaternion rotation)
    {
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(rotation);

        float r00 = MathF.Abs(m.M11), r01 = MathF.Abs(m.M12), r02 = MathF.Abs(m.M13);
        float r10 = MathF.Abs(m.M21), r11 = MathF.Abs(m.M22), r12 = MathF.Abs(m.M23);
        float r20 = MathF.Abs(m.M31), r21 = MathF.Abs(m.M32), r22 = MathF.Abs(m.M33);

        return new Vector3(
            r00 * halfExtents.X + r01 * halfExtents.Y + r02 * halfExtents.Z,
            r10 * halfExtents.X + r11 * halfExtents.Y + r12 * halfExtents.Z,
            r20 * halfExtents.X + r21 * halfExtents.Y + r22 * halfExtents.Z
        );
    }

    private static Vector3 Mul(Vector3 a, Vector3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    private void AddBox()
    {
        PushUndoSnapshot();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Type = EntityTypes.Box,
            Name = $"Box_{LevelFile.Entities.Count}",
            Position = new Vector3(0, 0.5f, 0),
            RotationEulerDeg = new Vector3(0, 0, 0),
            Scale = new Vector3(1, 1, 1),
            Size = new Vector3(1, 1, 1),
            Color = new Vector4(0.6f, 0.6f, 0.6f, 1f)
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();
    }

    private void AddLight()
    {
        PushUndoSnapshot();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Type = EntityTypes.PointLight,
            Name = $"Light_{LevelFile.Entities.Count}",
            Position = new Vector3(0, 3f, 0),
            LightColor = new Vector4(1, 1, 1, 1),
            Intensity = 3f,
            Range = 8f
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();
    }

    private void AddSpawn()
    {
        PushUndoSnapshot();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Type = EntityTypes.PlayerSpawn,
            Name = $"Spawn_{LevelFile.Entities.Count}",
            Position = new Vector3(0, 0, -5f),
            YawDeg = 0f
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();
    }

    private void AddTrigger()
    {
        PushUndoSnapshot();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Type = EntityTypes.TriggerVolume,
            Name = $"Trigger_{LevelFile.Entities.Count}",
            Position = new Vector3(0, 1f, 0),
            TriggerSize = new Vector3(2, 2, 2),
            TriggerEvent = "OnEnter_Trigger"
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();
    }

    private void AddProp()
    {
        PushUndoSnapshot();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Type = EntityTypes.Prop,
            Name = $"Prop_{LevelFile.Entities.Count}",
            Position = new Vector3(2f, 0.5f, 0),
            RotationEulerDeg = new Vector3(0, 0, 0),
            Scale = new Vector3(1, 1, 1),
            MeshPath = "Content/Meshes/prop.mesh",
            MaterialPath = ""
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();
    }

    private void AddRigidBody()
    {
        PushUndoSnapshot();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Type = EntityTypes.RigidBody,
            Name = $"RB_{LevelFile.Entities.Count}",
            Position = new Vector3(-2f, 0.5f, 0),
            RotationEulerDeg = new Vector3(0, 0, 0),
            Scale = new Vector3(1, 1, 1),
            Shape = "Box",
            Size = new Vector3(1, 1, 1),
            Mass = 10f,
            Friction = 0.8f
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();
    }
    private string Snapshot()
    {
        return JsonSerializer.Serialize(LevelFile);
    }

    private void RestoreSnapshot(string json)
    {
        LevelFile = JsonSerializer.Deserialize<LevelFile>(json)!;
        Dirty = true;
        SelectedEntityIndex = Math.Clamp(SelectedEntityIndex, -1, LevelFile.Entities.Count - 1);
        RebuildRuntimeFromLevel();
    }

    private void PushUndoSnapshot()
    {
        _undoStack.Push(Snapshot());
        _redoStack.Clear();
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;
        _redoStack.Push(Snapshot());
        RestoreSnapshot(_undoStack.Pop());
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0) return false;
        _undoStack.Push(Snapshot());
        RestoreSnapshot(_redoStack.Pop());
        return true;
    }

    private void BeginEdit()
    {
        if (_editInProgress) return;
        _editInProgress = true;
        _editStartSnapshot = Snapshot();
    }

    private void EndEditIfAny()
    {
        if (!_editInProgress) return;
        _editInProgress = false;

        string now = Snapshot();
        if (_editStartSnapshot != now)
        {
            _undoStack.Push(_editStartSnapshot);
            _redoStack.Clear();
        }

        _editStartSnapshot = "";
    }
}

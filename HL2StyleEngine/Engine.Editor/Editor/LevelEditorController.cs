using Editor.Editor;
using Engine.Editor.Level;
using Engine.Physics.Collision;
using ImGuiNET;
using System.Numerics;
using System.Text;
using System.Runtime.InteropServices;

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

    private enum GizmoAxis { None, X, Y, Z }

    private bool _dragging;
    private GizmoAxis _dragAxis = GizmoAxis.None;

    private Vector3 _dragOffset;
    private float _dragPlaneY;

    private Vector3 _axisOriginAtGrab;
    private float _axisGrabT;
    private Vector3 _entityPosAtGrab;

    private bool _layoutDockedOnce = false;

    public bool ShowColliders = true;
    public bool ShowColliderCorners = true;
    public bool ShowPhysicsAabbs = true;
    public float ColliderLineThickness = 0.03f;
    public float CornerSize = 0.08f;

    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private bool _editInProgress;
    private string _editStartSnapshot = "";

    private string _hierarchyFilter = "";
    private bool _hierarchyWindowFocused;

    private enum TransformSpace { Local, World }
    private TransformSpace _inspectorSpace = TransformSpace.Local;

    public bool FrameSelectionRequested { get; private set; }
    private static bool IsZero(Vector3 v) => MathF.Abs(v.X) < 0.0001f && MathF.Abs(v.Y) < 0.0001f && MathF.Abs(v.Z) < 0.0001f;

    public void ConsumeFrameRequest() => FrameSelectionRequested = false;

    public void LoadOrCreate(string path, Func<LevelFile> createDefault)
    {
        LevelPath = path;
        LevelFile = LevelIO.LoadOrCreate(path, createDefault);
        FixupLoadedEntities();
        RebuildRuntimeFromLevel();
        Dirty = false;
        SelectedEntityIndex = Math.Clamp(SelectedEntityIndex, -1, LevelFile.Entities.Count - 1);
        _dragging = false;
        _dragAxis = GizmoAxis.None;

        RebuildRuntimeFromLevel();
    }

    public void Reload()
    {
        LevelFile = LevelIO.Load(LevelPath);
        FixupLoadedEntities();
        RebuildRuntimeFromLevel();
        Dirty = false;

        SelectedEntityIndex = Math.Clamp(SelectedEntityIndex, -1, LevelFile.Entities.Count - 1);
        _dragging = false;
        _dragAxis = GizmoAxis.None;

        RebuildRuntimeFromLevel();
    }

    public void Save()
    {
        LevelIO.Save(LevelPath, LevelFile);
        Dirty = false;
    }

    private int FindIndexById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return -1;
        for (int i = 0; i < LevelFile.Entities.Count; i++)
            if (LevelFile.Entities[i].Id == id) return i;
        return -1;
    }

    private Matrix4x4 GetLocalMatrix(LevelEntityDef e)
    {
        Quaternion r = EulerDegToQuat(e.LocalRotationEulerDeg);
        return
            Matrix4x4.CreateScale(e.LocalScale) *
            Matrix4x4.CreateFromQuaternion(r) *
            Matrix4x4.CreateTranslation(e.LocalPosition);
    }

    private Matrix4x4 GetWorldMatrix(int entityIndex)
    {
        if (entityIndex < 0 || entityIndex >= LevelFile.Entities.Count)
            return Matrix4x4.Identity;

        var e = LevelFile.Entities[entityIndex];
        Matrix4x4 local = GetLocalMatrix(e);

        int parentIndex = FindIndexById(e.ParentId);
        if (parentIndex < 0)
            return local;

        const int maxDepth = 128;
        int depth = 0;

        Matrix4x4 parentChain = Matrix4x4.Identity;
        int cur = parentIndex;

        while (cur >= 0 && depth++ < maxDepth)
        {
            var p = LevelFile.Entities[cur];

            parentChain = parentChain * GetLocalMatrix(p);

            cur = FindIndexById(p.ParentId);
        }

        return local * parentChain;
    }


    private bool IsDescendant(string childId, string potentialAncestorId)
    {
        int idx = FindIndexById(childId);
        const int maxDepth = 256;
        int depth = 0;

        while (idx >= 0 && depth++ < maxDepth)
        {
            var e = LevelFile.Entities[idx];
            if (string.IsNullOrWhiteSpace(e.ParentId)) return false;
            if (e.ParentId == potentialAncestorId) return true;
            idx = FindIndexById(e.ParentId);
        }
        return false;
    }

    private void SetParentKeepWorld(int childIndex, int newParentIndex)
    {
        if (childIndex < 0 || childIndex >= LevelFile.Entities.Count) return;
        if (newParentIndex < 0 || newParentIndex >= LevelFile.Entities.Count) return;
        if (childIndex == newParentIndex) return;

        var child = LevelFile.Entities[childIndex];
        var newParent = LevelFile.Entities[newParentIndex];

        if (IsDescendant(newParent.Id, child.Id))
            return;

        BeginEdit();

        Matrix4x4 childWorld = GetWorldMatrix(childIndex);
        Matrix4x4 newParentWorld = GetWorldMatrix(newParentIndex);

        if (!Matrix4x4.Invert(newParentWorld, out var invParentWorld))
            invParentWorld = Matrix4x4.Identity;

        Matrix4x4 newLocal = childWorld * invParentWorld;

        if (Matrix4x4.Decompose(newLocal, out Vector3 s, out Quaternion r, out Vector3 t))
        {
            child.ParentId = newParent.Id;
            child.LocalPosition = t;
            child.LocalScale = s;
            child.LocalRotationEulerDeg = QuatToEulerDeg(r);

            Dirty = true;
            RebuildRuntimeFromLevel();
        }

        EndEditIfAny();
    }

    private void ClearParentKeepWorld(int childIndex)
    {
        if (childIndex < 0 || childIndex >= LevelFile.Entities.Count) return;

        var child = LevelFile.Entities[childIndex];
        if (string.IsNullOrWhiteSpace(child.ParentId))
            return;

        BeginEdit();

        Matrix4x4 childWorld = GetWorldMatrix(childIndex);

        if (Matrix4x4.Decompose(childWorld, out Vector3 s, out Quaternion r, out Vector3 t))
        {
            child.ParentId = null;
            child.LocalPosition = t;
            child.LocalScale = s;
            child.LocalRotationEulerDeg = QuatToEulerDeg(r);

            Dirty = true;
            RebuildRuntimeFromLevel();
        }

        EndEditIfAny();
    }

    public bool TryGetPlayerSpawn(out Vector3 feetPos, out float yawDeg)
    {
        for (int i = 0; i < LevelFile.Entities.Count; i++)
        {
            var e = LevelFile.Entities[i];
            if (e.Type == EntityTypes.PlayerSpawn)
            {
                feetPos = e.LocalPosition; 
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

        BeginEdit();

        var src = LevelFile.Entities[SelectedEntityIndex];
        var copy = CloneEntity(src);

        copy.LocalPosition = copy.LocalPosition + new Vector3(0.5f, 0f, 0.5f);

        LevelFile.Entities.Add(copy);
        SelectedEntityIndex = LevelFile.Entities.Count - 1;

        Dirty = true;
        RebuildRuntimeFromLevel();

        EndEditIfAny();
        return true;
    }

    private static LevelEntityDef CloneEntity(LevelEntityDef src)
    {
        return new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = src.Type,
            Name = (src.Name ?? "Entity") + "_copy",

            ParentId = src.ParentId,

            LocalPosition = src.LocalPosition,
            LocalRotationEulerDeg = src.LocalRotationEulerDeg,
            LocalScale = src.LocalScale,

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

        if (!_layoutDockedOnce)
        {
            ImGui.SameLine();
            if (ImGui.Button("Lock Layout"))
                _layoutDockedOnce = true;
        }
        else
        {
            ImGui.SameLine();
            if (ImGui.Button("Reset Layout"))
                _layoutDockedOnce = false;
        }

        if (ImGui.Button("Save")) Save();
        ImGui.SameLine();
        if (ImGui.Button("Reload")) Reload();

        ImGui.Separator();
        if (ImGui.Button("Undo")) Undo();
        ImGui.SameLine();
        if (ImGui.Button("Redo")) Redo();
        
        ImGui.Separator();

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
        {
            BeginEdit();

            LevelFile.Entities.RemoveAt(SelectedEntityIndex);
            SelectedEntityIndex = Math.Clamp(SelectedEntityIndex, -1, LevelFile.Entities.Count - 1);

            Dirty = true;
            RebuildRuntimeFromLevel();

            EndEditIfAny();
        }
        if (!hasSelection) ImGui.EndDisabled();

        ImGui.End();
    }

    public void DrawHierarchyPanel(ref bool mouseOverUi, ref bool keyboardOverUi)
    {
        ImGui.Begin("Hierarchy");
        mouseOverUi |= ImGui.IsWindowHovered();
        keyboardOverUi |= ImGui.IsWindowFocused();
        _hierarchyWindowFocused = ImGui.IsWindowFocused();

        // Filter UI
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##hierFilter", "Search... (name contains)  |  t:box  t:prop  etc", ref _hierarchyFilter, 256);
        ImGui.Separator();

        if (_hierarchyWindowFocused && ImGui.IsKeyPressed(ImGuiKey.F, false))
        {
            if (SelectedEntityIndex >= 0)
                FrameSelectionRequested = true;
        }

        var children = BuildChildrenMap();

        List<int> roots = new();
        for (int i = 0; i < LevelFile.Entities.Count; i++)
        {
            var e = LevelFile.Entities[i];
            if (FindIndexById(e.ParentId) < 0)
                roots.Add(i);
        }

        ImGui.BeginChild("entity_tree", new Vector2(0, 0), ImGuiChildFlags.Borders);

        for (int r = 0; r < roots.Count; r++)
            DrawHierarchyNodeRecursive(roots[r], children, parentVisibleBecauseChildMatches: false);

        ImGui.EndChild();
        ImGui.End();
    }

    private Dictionary<int, List<int>> BuildChildrenMap()
    {
        var map = new Dictionary<int, List<int>>();
        for (int i = 0; i < LevelFile.Entities.Count; i++)
            map[i] = new List<int>();

        for (int i = 0; i < LevelFile.Entities.Count; i++)
        {
            int p = FindIndexById(LevelFile.Entities[i].ParentId);
            if (p >= 0)
                map[p].Add(i);
        }

        return map;
    }

    private void DrawHierarchyNodeRecursive(
        int index,
        Dictionary<int, List<int>> children,
        bool parentVisibleBecauseChildMatches)
    {
        if (index < 0 || index >= LevelFile.Entities.Count) return;

        var e = LevelFile.Entities[index];
        var kids = children.TryGetValue(index, out var list) ? list : null;

        bool selfMatches = MatchesFilter(e);
        bool anyChildMatches = false;

        if (kids != null && kids.Count > 0)
        {
            for (int i = 0; i < kids.Count; i++)
            {
                if (SubtreeMatches(kids[i], children))
                {
                    anyChildMatches = true;
                    break;
                }
            }
        }

        bool shouldShow = selfMatches || anyChildMatches || parentVisibleBecauseChildMatches || string.IsNullOrWhiteSpace(_hierarchyFilter);
        if (!shouldShow)
            return;

        bool selected = index == SelectedEntityIndex;

        ImGui.PushID(e.Id);

        ImGuiTreeNodeFlags flags =
            ImGuiTreeNodeFlags.OpenOnArrow |
            ImGuiTreeNodeFlags.SpanFullWidth;

        if (selected) flags |= ImGuiTreeNodeFlags.Selected;
        if (kids == null || kids.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        string label = $"{e.Name ?? "Entity"}  [{e.Type}]##node";

        bool open = ImGui.TreeNodeEx(label, flags);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            SelectedEntityIndex = index;

        if (ImGui.BeginDragDropSource())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(e.Id + "\0");

            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                ImGui.SetDragDropPayload("ENTITY_ID", handle.AddrOfPinnedObject(), (uint)bytes.Length);
            }
            finally
            {
                handle.Free();
            }

            ImGui.Text($"Move: {e.Name ?? e.Id}");
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload =
                ImGui.AcceptDragDropPayload("ENTITY_ID", ImGuiDragDropFlags.None);

            unsafe
            {
                if (payload.NativePtr != null && payload.Delivery)
                {
                    int size = payload.DataSize;
                    nint data = payload.Data;

                    if (data != IntPtr.Zero && size > 0)
                    {
                        byte[] bytes = new byte[size];
                        Marshal.Copy(data, bytes, 0, size);

                        string draggedId = Encoding.UTF8
                            .GetString(bytes)
                            .TrimEnd('\0')
                            .Trim();

                        int childIndex = FindIndexById(draggedId);
                        if (childIndex >= 0 && childIndex != index)
                            SetParentKeepWorld(childIndex, index);
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }



        if (open && !(flags.HasFlag(ImGuiTreeNodeFlags.Leaf) || flags.HasFlag(ImGuiTreeNodeFlags.NoTreePushOnOpen)))
        {
            for (int i = 0; i < kids!.Count; i++)
                DrawHierarchyNodeRecursive(kids[i], children, parentVisibleBecauseChildMatches: selfMatches);

            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private bool SubtreeMatches(int index, Dictionary<int, List<int>> children)
    {
        if (index < 0 || index >= LevelFile.Entities.Count) return false;
        var e = LevelFile.Entities[index];
        if (MatchesFilter(e)) return true;

        if (!children.TryGetValue(index, out var kids)) return false;
        for (int i = 0; i < kids.Count; i++)
            if (SubtreeMatches(kids[i], children)) return true;

        return false;
    }

    private bool MatchesFilter(LevelEntityDef e)
    {
        if (string.IsNullOrWhiteSpace(_hierarchyFilter))
            return true;

        string f = _hierarchyFilter.Trim();

        string typeToken = "";
        string nameToken = f;

        var parts = f.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nameParts = new List<string>();

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("t:", StringComparison.OrdinalIgnoreCase))
                typeToken = parts[i].Substring(2);
            else
                nameParts.Add(parts[i]);
        }

        nameToken = string.Join(' ', nameParts);

        if (!string.IsNullOrWhiteSpace(typeToken))
        {
            if (string.IsNullOrWhiteSpace(e.Type)) return false;
            if (!e.Type.Contains(typeToken, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(nameToken))
        {
            string n = e.Name ?? "";
            if (!n.Contains(nameToken, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
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

        string parentLabel = "(none)";
        int parentIndex = FindIndexById(ent.ParentId);
        if (parentIndex >= 0)
            parentLabel = LevelFile.Entities[parentIndex].Name ?? LevelFile.Entities[parentIndex].Id;

        ImGui.Text($"Parent: {parentLabel}");
        if (parentIndex >= 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear Parent"))
                ClearParentKeepWorld(SelectedEntityIndex);
        }

        ImGui.Separator();

        string name = ent.Name ?? "";
        if (ImGui.InputText("Name", ref name, 128))
        {
            BeginEdit();
            ent.Name = name;
            Dirty = true;
            EndEditIfAny();
        }

        ImGui.Separator();
        ImGui.Text("Transform");

        string spaceLabel = _inspectorSpace == TransformSpace.Local ? "Space: Local" : "Space: World";
        if (ImGui.Button(spaceLabel))
            _inspectorSpace = _inspectorSpace == TransformSpace.Local ? TransformSpace.World : TransformSpace.Local;

        Vector3 pos, rotDeg, scl;

        if (_inspectorSpace == TransformSpace.Local)
        {
            pos = ent.LocalPosition;
            rotDeg = ent.LocalRotationEulerDeg;
            scl = ent.LocalScale;
        }
        else
        {
            Matrix4x4 w = GetWorldMatrix(SelectedEntityIndex);
            if (!Matrix4x4.Decompose(w, out Vector3 ws, out Quaternion wr, out Vector3 wt))
            {
                ws = Vector3.One;
                wr = Quaternion.Identity;
                wt = Vector3.Zero;
            }
            pos = wt;
            scl = ws;
            rotDeg = QuatToEulerDeg(wr);
        }

        bool posChanged = ImGui.DragFloat3("Position", ref pos, 0.05f);
        bool rotChanged = false;
        bool sclChanged = false;

        bool showRotScale =
            ent.Type == EntityTypes.Box ||
            ent.Type == EntityTypes.Prop ||
            ent.Type == EntityTypes.RigidBody;

        if (showRotScale)
        {
            rotChanged = ImGui.DragFloat3("Rotation (deg)", ref rotDeg, 1f);
            sclChanged = ImGui.DragFloat3("Scale", ref scl, 0.01f);
        }

        if (posChanged || rotChanged || sclChanged)
        {
            BeginEdit();

            scl.X = MathF.Max(0.01f, scl.X);
            scl.Y = MathF.Max(0.01f, scl.Y);
            scl.Z = MathF.Max(0.01f, scl.Z);

            if (_inspectorSpace == TransformSpace.Local)
            {
                ent.LocalPosition = ApplySnapping(pos, GizmoAxis.None, ctrlDown: false);
                ent.LocalRotationEulerDeg = rotDeg;
                ent.LocalScale = scl;
            }
            else
            {
                Matrix4x4 desiredWorld =
                    Matrix4x4.CreateScale(scl) *
                    Matrix4x4.CreateFromQuaternion(EulerDegToQuat(rotDeg)) *
                    Matrix4x4.CreateTranslation(ApplySnapping(pos, GizmoAxis.None, ctrlDown: false));

                Matrix4x4 parentWorld = Matrix4x4.Identity;
                if (parentIndex >= 0)
                    parentWorld = GetWorldMatrix(parentIndex);

                if (!Matrix4x4.Invert(parentWorld, out var invParent))
                    invParent = Matrix4x4.Identity;

                Matrix4x4 desiredLocal = desiredWorld * invParent;

                if (Matrix4x4.Decompose(desiredLocal, out Vector3 ls, out Quaternion lr, out Vector3 lt))
                {
                    ent.LocalPosition = lt;
                    ent.LocalScale = ls;
                    ent.LocalRotationEulerDeg = QuatToEulerDeg(lr);
                }
            }

            Dirty = true;
            RebuildRuntimeFromLevel();

            EndEditIfAny();
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
        ImGui.BulletText("Hierarchy: search name contains, t:type, drag-drop to parent, F to frame");
    }

    private void DrawTypeSpecificInspector(LevelEntityDef ent)
    {
        if (ent.Type == EntityTypes.Box)
        {
            Vector3 size = ent.Size;
            if (ImGui.DragFloat3("Size", ref size, 0.05f))
            {
                BeginEdit();

                size.X = MathF.Max(0.01f, size.X);
                size.Y = MathF.Max(0.01f, size.Y);
                size.Z = MathF.Max(0.01f, size.Z);
                ent.Size = size;

                Dirty = true;
                RebuildRuntimeFromLevel();

                EndEditIfAny();
            }

            Vector4 col = ent.Color;
            if (ImGui.ColorEdit4("Color", ref col))
            {
                BeginEdit();
                ent.Color = col;
                Dirty = true;
                RebuildRuntimeFromLevel();
                EndEditIfAny();
            }
        }
        else if (ent.Type == EntityTypes.PlayerSpawn)
        {
            float yaw = ent.YawDeg;
            if (ImGui.DragFloat("YawDeg", ref yaw, 1f))
            {
                BeginEdit();
                ent.YawDeg = yaw;
                Dirty = true;
                EndEditIfAny();
            }
        }
        else if (ent.Type == EntityTypes.PointLight)
        {
            Vector4 lc = ent.LightColor;
            if (ImGui.ColorEdit4("LightColor", ref lc))
            {
                BeginEdit();
                ent.LightColor = lc;
                Dirty = true;
                RebuildRuntimeFromLevel();
                EndEditIfAny();
            }

            float intensity = ent.Intensity;
            if (ImGui.DragFloat("Intensity", ref intensity, 0.1f, 0f, 100f))
            {
                BeginEdit();
                ent.Intensity = intensity;
                Dirty = true;
                EndEditIfAny();
            }

            float range = ent.Range;
            if (ImGui.DragFloat("Range", ref range, 0.1f, 0.1f, 1000f))
            {
                BeginEdit();
                ent.Range = range;
                Dirty = true;
                EndEditIfAny();
            }
        }
        else if (ent.Type == EntityTypes.Prop)
        {
            string mesh = ent.MeshPath ?? "";
            if (ImGui.InputText("MeshPath", ref mesh, 256))
            {
                BeginEdit();
                ent.MeshPath = mesh;
                Dirty = true;
                EndEditIfAny();
            }

            string mat = ent.MaterialPath ?? "";
            if (ImGui.InputText("MaterialPath", ref mat, 256))
            {
                BeginEdit();
                ent.MaterialPath = mat;
                Dirty = true;
                EndEditIfAny();
            }
        }
        else if (ent.Type == EntityTypes.TriggerVolume)
        {
            Vector3 tsize = ent.TriggerSize;
            if (ImGui.DragFloat3("TriggerSize", ref tsize, 0.05f))
            {
                BeginEdit();

                tsize.X = MathF.Max(0.01f, tsize.X);
                tsize.Y = MathF.Max(0.01f, tsize.Y);
                tsize.Z = MathF.Max(0.01f, tsize.Z);
                ent.TriggerSize = tsize;

                Dirty = true;
                RebuildRuntimeFromLevel();

                EndEditIfAny();
            }

            string evt = ent.TriggerEvent ?? "";
            if (ImGui.InputText("TriggerEvent", ref evt, 128))
            {
                BeginEdit();
                ent.TriggerEvent = evt;
                Dirty = true;
                EndEditIfAny();
            }
        }
        else if (ent.Type == EntityTypes.RigidBody)
        {
            string shape = ent.Shape ?? "Box";
            if (ImGui.InputText("Shape", ref shape, 32))
            {
                BeginEdit();
                ent.Shape = shape;
                Dirty = true;
                EndEditIfAny();
            }

            float mass = ent.Mass;
            if (ImGui.DragFloat("Mass", ref mass, 0.1f, 0f, 100000f))
            {
                BeginEdit();
                ent.Mass = mass;
                Dirty = true;
                EndEditIfAny();
            }

            float fr = ent.Friction;
            if (ImGui.DragFloat("Friction", ref fr, 0.01f, 0f, 10f))
            {
                BeginEdit();
                ent.Friction = fr;
                Dirty = true;
                EndEditIfAny();
            }

            float rest = ent.Restitution;
            if (ImGui.DragFloat("Restitution", ref rest, 0.01f, 0f, 1f))
            {
                BeginEdit();
                ent.Restitution = rest;
                Dirty = true;
                EndEditIfAny();
            }

            bool kin = ent.IsKinematic;
            if (ImGui.Checkbox("IsKinematic", ref kin))
            {
                BeginEdit();
                ent.IsKinematic = kin;
                Dirty = true;
                EndEditIfAny();
            }

            Vector3 size = ent.Size;
            if (ImGui.DragFloat3("Size", ref size, 0.05f))
            {
                BeginEdit();

                size.X = MathF.Max(0.01f, size.X);
                size.Y = MathF.Max(0.01f, size.Y);
                size.Z = MathF.Max(0.01f, size.Z);
                ent.Size = size;

                Dirty = true;
                RebuildRuntimeFromLevel();

                EndEditIfAny();
            }
        }
    }

    public void OnMousePressed(EditorPicking.Ray ray, bool ctrlDown)
    {
        if (GizmoEnabled && SelectedEntityIndex >= 0 && SelectedEntityIndex < LevelFile.Entities.Count)
        {
            Vector3 p = GetEntityWorldPosition(SelectedEntityIndex);

            if (RayHitsAxisHandle(ray, p, GizmoAxis.Y)) { BeginAxisDrag(ray, p, GizmoAxis.Y); return; }
            if (RayHitsAxisHandle(ray, p, GizmoAxis.X)) { BeginAxisDrag(ray, p, GizmoAxis.X); return; }
            if (RayHitsAxisHandle(ray, p, GizmoAxis.Z)) { BeginAxisDrag(ray, p, GizmoAxis.Z); return; }
        }

        if (TryPickEntity(ray, out int hitIndex, out Vector3 hitPoint))
        {
            SelectedEntityIndex = hitIndex;

            _dragPlaneY = hitPoint.Y;
            Vector3 pos = GetEntityWorldPosition(SelectedEntityIndex);
            _dragOffset = hitPoint - pos;

            BeginEdit();

            _dragging = true;
            _dragAxis = GizmoAxis.None;
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
        _dragging = false;
        _dragAxis = GizmoAxis.None;
        EndEditIfAny();
    }

    private Vector3 GetEntityWorldPosition(int idx)
    {
        Matrix4x4 w = GetWorldMatrix(idx);
        return new Vector3(w.M41, w.M42, w.M43);
    }

    public bool HasGizmo(out EditorDrawBox xLine, out EditorDrawBox xHandle,
                         out EditorDrawBox yLine, out EditorDrawBox yHandle,
                         out EditorDrawBox zLine, out EditorDrawBox zHandle)
    {
        xLine = xHandle = yLine = yHandle = zLine = zHandle = default;

        if (!GizmoEnabled || SelectedEntityIndex < 0 || SelectedEntityIndex >= LevelFile.Entities.Count)
            return false;

        Vector3 p = GetEntityWorldPosition(SelectedEntityIndex);

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

    private void BeginAxisDrag(EditorPicking.Ray ray, Vector3 entityWorldPos, GizmoAxis axis)
    {
        BeginEdit();
        _dragging = true;
        _dragAxis = axis;

        _axisOriginAtGrab = entityWorldPos;
        _entityPosAtGrab = entityWorldPos;

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
        Vector3 newWorldPos = _entityPosAtGrab + axisDir * deltaT;

        newWorldPos = ApplySnapping(newWorldPos, _dragAxis, ctrlDown);

        SetEntityWorldPosition(SelectedEntityIndex, newWorldPos);
    }

    private void ContinueXZDrag(EditorPicking.Ray ray, bool ctrlDown)
    {
        if (!EditorPicking.RayIntersectsPlane(ray, Vector3.UnitY, _dragPlaneY, out float t))
            return;

        Vector3 hit = ray.GetPoint(t);
        Vector3 newWorldPos = hit - _dragOffset;

        newWorldPos = ApplySnapping(newWorldPos, GizmoAxis.None, ctrlDown);

        SetEntityWorldPosition(SelectedEntityIndex, newWorldPos);
    }

    private void SetEntityWorldPosition(int idx, Vector3 desiredWorldPos)
    {
        if (idx < 0 || idx >= LevelFile.Entities.Count) return;

        var ent = LevelFile.Entities[idx];

        int parentIndex = FindIndexById(ent.ParentId);
        if (parentIndex < 0)
        {
            ent.LocalPosition = desiredWorldPos;
        }
        else
        {
            Matrix4x4 parentWorld = GetWorldMatrix(parentIndex);
            if (!Matrix4x4.Invert(parentWorld, out var invParent))
                invParent = Matrix4x4.Identity;

            Matrix4x4 currentWorld = GetWorldMatrix(idx);
            Matrix4x4.Decompose(currentWorld, out Vector3 ws, out Quaternion wr, out _);

            Matrix4x4 desiredWorld =
                Matrix4x4.CreateScale(ws) *
                Matrix4x4.CreateFromQuaternion(wr) *
                Matrix4x4.CreateTranslation(desiredWorldPos);

            Matrix4x4 desiredLocal = desiredWorld * invParent;

            if (Matrix4x4.Decompose(desiredLocal, out Vector3 ls, out Quaternion lr, out Vector3 lt))
            {
                ent.LocalPosition = lt;
                ent.LocalScale = ls;
                ent.LocalRotationEulerDeg = QuatToEulerDeg(lr);
            }
        }

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

    public void RebuildRuntimeFromLevel()
    {
        DrawBoxes.Clear();
        SolidColliders.Clear();
        Triggers.Clear();

        Vector3 markerSmall = new(MarkerSmall, MarkerSmall, MarkerSmall);
        Vector3 markerMed = new(MarkerMedium, MarkerMedium, MarkerMedium);

        for (int i = 0; i < LevelFile.Entities.Count; i++)
        {
            var e = LevelFile.Entities[i];
            Matrix4x4 world = GetWorldMatrix(i);

            if (!Matrix4x4.Decompose(world, out Vector3 ws, out Quaternion wr, out Vector3 wt))
            {
                ws = Vector3.One;
                wr = Quaternion.Identity;
                wt = Vector3.Zero;
            }

            if (e.Type == EntityTypes.Box)
            {
                Vector3 size = Mul(e.Size, ws);
                DrawBoxes.Add(new EditorDrawBox(wt, size, e.Color, wr));

                Vector3 halfAabb = OrientedBoxAabbHalfExtents(size * 0.5f, wr);
                SolidColliders.Add(new Aabb(wt - halfAabb, wt + halfAabb));
            }
            else if (e.Type == EntityTypes.TriggerVolume)
            {
                DrawBoxes.Add(EditorDrawBox.AxisAligned(wt, markerMed, new Vector4(0.1f, 0.9f, 0.1f, 1f)));

                Vector3 half = ((Vector3)e.TriggerSize * 0.5f);
                var aabb = new Aabb(wt - half, wt + half);
                Triggers.Add((e.TriggerEvent ?? "", aabb, false));
            }
            else if (e.Type == EntityTypes.PointLight)
            {
                DrawBoxes.Add(EditorDrawBox.AxisAligned(wt, markerSmall, e.LightColor));
            }
            else if (e.Type == EntityTypes.PlayerSpawn)
            {
                DrawBoxes.Add(EditorDrawBox.AxisAligned(wt, markerMed, new Vector4(0.2f, 0.8f, 1f, 1f)));
            }
            else if (e.Type == EntityTypes.Prop)
            {
                Vector3 size = Mul(markerMed, ws);
                DrawBoxes.Add(new EditorDrawBox(wt, size, new Vector4(0.8f, 0.6f, 0.2f, 1f), wr));
            }
            else if (e.Type == EntityTypes.RigidBody)
            {
                Vector3 size = Mul(e.Size, ws);
                DrawBoxes.Add(new EditorDrawBox(wt, size, new Vector4(0.9f, 0.2f, 0.6f, 1f), wr));
            }
            else
            {
                DrawBoxes.Add(EditorDrawBox.AxisAligned(wt, markerSmall, new Vector4(1f, 0f, 1f, 1f)));
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

    public bool TryGetEntityWorldTRS(int idx, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        pos = default;
        rot = Quaternion.Identity;
        scale = Vector3.One;

        if (idx < 0 || idx >= LevelFile.Entities.Count)
            return false;

        Matrix4x4 w = GetWorldMatrix(idx);
        if (!Matrix4x4.Decompose(w, out scale, out rot, out pos))
            return false;

        return true;
    }


    public bool TryGetSelectedWorldPosition(out Vector3 pos)
    {
        pos = default;
        if (SelectedEntityIndex < 0 || SelectedEntityIndex >= LevelFile.Entities.Count)
            return false;

        pos = GetEntityWorldPosition(SelectedEntityIndex);
        return true;
    }

    private string Snapshot() => System.Text.Json.JsonSerializer.Serialize(LevelFile);

    private void RestoreSnapshot(string json)
    {
        LevelFile = System.Text.Json.JsonSerializer.Deserialize<LevelFile>(json)!;
        Dirty = true;
        SelectedEntityIndex = Math.Clamp(SelectedEntityIndex, -1, LevelFile.Entities.Count - 1);
        RebuildRuntimeFromLevel();
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

        if (_editStartSnapshot != Snapshot())
        {
            _undoStack.Push(_editStartSnapshot);
            _redoStack.Clear();
        }

        _editStartSnapshot = "";
    }

    private static Quaternion EulerDegToQuat(Vector3 eulerDeg)
    {
        float yaw = MathF.PI / 180f * eulerDeg.Y;
        float pitch = MathF.PI / 180f * eulerDeg.X;
        float roll = MathF.PI / 180f * eulerDeg.Z;

        return Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
    }

    private static Vector3 QuatToEulerDeg(Quaternion q)
    {
        var m = Matrix4x4.CreateFromQuaternion(q);

        float sy = -m.M23;
        float cy = MathF.Sqrt(MathF.Max(0f, 1f - sy * sy));

        float pitch, yaw, roll;

        if (cy > 0.0001f)
        {
            pitch = MathF.Asin(sy);
            yaw = MathF.Atan2(m.M13, m.M33);
            roll = MathF.Atan2(m.M21, m.M22);
        }
        else
        {
            pitch = MathF.Asin(sy);
            yaw = MathF.Atan2(-m.M31, m.M11);
            roll = 0f;
        }

        const float rad2deg = 180f / MathF.PI;
        return new Vector3(pitch * rad2deg, yaw * rad2deg, roll * rad2deg);
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
        BeginEdit();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.Box,
            Name = $"Box_{LevelFile.Entities.Count}",
            LocalPosition = new Vector3(0, 0.5f, 0),
            LocalRotationEulerDeg = Vector3.Zero,
            LocalScale = Vector3.One,
            Size = new Vector3(1, 1, 1),
            Color = new Vector4(0.6f, 0.6f, 0.6f, 1f)
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();

        EndEditIfAny();
    }

    private void AddLight()
    {
        BeginEdit();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.PointLight,
            Name = $"Light_{LevelFile.Entities.Count}",
            LocalPosition = new Vector3(0, 3f, 0),
            LightColor = new Vector4(1, 1, 1, 1),
            Intensity = 3f,
            Range = 8f
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();

        EndEditIfAny();
    }

    private void AddSpawn()
    {
        BeginEdit();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.PlayerSpawn,
            Name = $"Spawn_{LevelFile.Entities.Count}",
            LocalPosition = new Vector3(0, 0, -5f),
            YawDeg = 0f
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();

        EndEditIfAny();
    }

    private void AddTrigger()
    {
        BeginEdit();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.TriggerVolume,
            Name = $"Trigger_{LevelFile.Entities.Count}",
            LocalPosition = new Vector3(0, 1f, 0),
            TriggerSize = new Vector3(2, 2, 2),
            TriggerEvent = "OnEnter_Trigger"
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();

        EndEditIfAny();
    }

    private void AddProp()
    {
        BeginEdit();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.Prop,
            Name = $"Prop_{LevelFile.Entities.Count}",
            LocalPosition = new Vector3(2f, 0.5f, 0),
            LocalRotationEulerDeg = Vector3.Zero,
            LocalScale = Vector3.One,
            MeshPath = "Content/Meshes/prop.mesh",
            MaterialPath = ""
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();

        EndEditIfAny();
    }

    private void AddRigidBody()
    {
        BeginEdit();

        LevelFile.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.RigidBody,
            Name = $"RB_{LevelFile.Entities.Count}",
            LocalPosition = new Vector3(-2f, 0.5f, 0),
            LocalRotationEulerDeg = Vector3.Zero,
            LocalScale = Vector3.One,
            Shape = "Box",
            Size = new Vector3(1, 1, 1),
            Mass = 10f,
            Friction = 0.8f
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();

        EndEditIfAny();
    }

    public void RequestFrameSelection()
    {
        if (SelectedEntityIndex >= 0 && SelectedEntityIndex < LevelFile.Entities.Count)
            FrameSelectionRequested = true;
    }


    private void FixupLoadedEntities()
    {
        for (int i = 0; i < LevelFile.Entities.Count; i++)
        {
            var e = LevelFile.Entities[i];

            Vector3 s = e.LocalScale; 
            if (IsZero(s))
                e.LocalScale = new Vector3(1, 1, 1);

            if (e.Type == EntityTypes.Box || e.Type == EntityTypes.RigidBody)
            {
                Vector3 sz = e.Size;
                if (IsZero(sz))
                    e.Size = new Vector3(1, 1, 1);
            }

            if (e.Type == EntityTypes.TriggerVolume)
            {
                Vector3 ts = e.TriggerSize;
                if (IsZero(ts))
                    e.TriggerSize = new Vector3(1, 1, 1);
            }

            LevelFile.Entities[i] = e; 
        }
    }
}

using Editor.Editor;
using Engine.Editor.Level;
using Engine.Physics.Collision;
using Engine.Physics.Dynamics;
using Engine.Runtime.Entities;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Engine.Editor.Editor;

public sealed class LevelEditorController
{
    private const string GlbAssetDragDropPayload = "HS2_GLB_ASSET_PATH";

    public LevelFile LevelFile { get; private set; } = null!;
    public string LevelPath { get; private set; } = "";

    public bool Dirty { get; private set; }
    public int SelectedEntityIndex { get; private set; } = -1;
    public Action? SaveActionOverride { get; set; }

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

    public bool ShowColliders = false;
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
    private int _addScriptSelectedIndex = 0;
    private int _damageModelPickerIndex = 0;
    private int _debrisModelPickerIndex = 0;
    private int _interactionKindPickerIndex = 0;
    private int _interactionItemPickerIndex = 0;
    private int _interactionTargetPickerIndex = 0;
    private int _interactionRequiredStatePickerIndex = 0;
    private int _interactionRewardItemPickerIndex = 0;

    private enum TransformSpace { Local, World }
    private TransformSpace _inspectorSpace = TransformSpace.Local;

    public bool FrameSelectionRequested { get; private set; }
    private static bool IsZero(Vector3 v) => MathF.Abs(v.X) < 0.0001f && MathF.Abs(v.Y) < 0.0001f && MathF.Abs(v.Z) < 0.0001f;
    private static bool IsSphereShape(string? shape) => string.Equals(shape, "Sphere", StringComparison.OrdinalIgnoreCase);
    private static bool IsCapsuleShape(string? shape) => string.Equals(shape, "Capsule", StringComparison.OrdinalIgnoreCase);
    private static bool IsMeshShape(string? shape) => string.Equals(shape, "Mesh", StringComparison.OrdinalIgnoreCase);
    private static float GetScaledSphereRadius(LevelEntityDef entity, Vector3 scale)
    {
        float scaleMax = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
        return MathF.Max(0.01f, entity.Radius) * MathF.Max(0.01f, scaleMax);
    }
    private static float GetScaledCapsuleRadius(LevelEntityDef entity, Vector3 scale)
    {
        float scaleXZ = MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Z));
        return MathF.Max(0.01f, entity.Radius) * MathF.Max(0.01f, scaleXZ);
    }
    private static float GetScaledCapsuleHeight(LevelEntityDef entity, Vector3 scale)
    {
        float radius = GetScaledCapsuleRadius(entity, scale);
        float height = MathF.Max(0.01f, entity.Height) * MathF.Max(0.01f, MathF.Abs(scale.Y));
        return MathF.Max(height, radius * 2f);
    }
    private ScriptRegistry _scripts = null!;
    public void SetScriptRegistry(ScriptRegistry reg) => _scripts = reg;
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


    public void LoadFromMemory(string path, LevelFile level)
    {
        LevelPath = path;
        LevelFile = level;
        FixupLoadedEntities();
        RebuildRuntimeFromLevel();
        Dirty = false;
        SelectedEntityIndex = LevelFile.Entities.Count > 0 ? 0 : -1;
        _dragging = false;
        _dragAxis = GizmoAxis.None;
        _undoStack.Clear();
        _redoStack.Clear();
    }
    public void Save()
    {
        LevelIO.Save(LevelPath, LevelFile);
        Dirty = false;
    }

    public void MarkClean()
        => Dirty = false;

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

    public bool TryGetSelectedEntity(out LevelEntityDef entity)
    {
        if (SelectedEntityIndex >= 0 && SelectedEntityIndex < LevelFile.Entities.Count)
        {
            entity = LevelFile.Entities[SelectedEntityIndex];
            return true;
        }

        entity = null!;
        return false;
    }

    public bool AssignSelectedMeshPath(string meshPath)
    {
        if (!TryGetSelectedEntity(out LevelEntityDef entity))
            return false;

        BeginEdit();
        entity.MeshPath = meshPath ?? "";
        if (entity.Type == EntityTypes.RigidBody && entity.MotionType == MotionType.Static && entity.MeshPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            entity.Shape = "Mesh";
            entity.Mass = 0f;
        }
        Dirty = true;
        RebuildRuntimeFromLevel();
        EndEditIfAny();
        return true;
    }

    public bool PlaceModelInScene(string meshPath, EditorPicking.Ray ray)
    {
        if (string.IsNullOrWhiteSpace(meshPath) || !meshPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            return false;

        Vector3 placement = GetScenePlacementPoint(ray) + new Vector3(0f, 0.5f, 0f);
        placement = ApplySnapping(placement, GizmoAxis.None, ctrlDown: false);

        string entityName = MakeUniqueEntityName(MakeEntityNameFromMeshPath(meshPath));

        BeginEdit();
        LevelFile.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.RigidBody,
            Name = entityName,
            LocalPosition = placement,
            LocalRotationEulerDeg = Vector3.Zero,
            LocalScale = Vector3.One,
            Shape = "Mesh",
            MotionType = MotionType.Static,
            Size = Vector3.One,
            Color = new Vector4(1f, 1f, 1f, 1f),
            MeshPath = meshPath,
            MaterialPath = "",
            Friction = 0.8f,
            Restitution = 0.05f,
            Mass = 0f
        });

        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();
        EndEditIfAny();
        return true;
    }

    private Vector3 GetScenePlacementPoint(EditorPicking.Ray ray)
    {
        if (TryPickEntity(ray, out _, out Vector3 hitPoint))
            return hitPoint;

        if (EditorPicking.RayIntersectsPlane(ray, Vector3.UnitY, 0f, out float t))
            return ray.GetPoint(t);

        return ray.Origin + ray.Dir * 5f;
    }

    private string MakeUniqueEntityName(string baseName)
    {
        string name = string.IsNullOrWhiteSpace(baseName) ? "Model" : baseName;
        if (!LevelFile.Entities.Any(entity => string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)))
            return name;

        for (int i = 1; i < 10000; i++)
        {
            string candidate = $"{name}_{i:00}";
            if (!LevelFile.Entities.Any(entity => string.Equals(entity.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{name}_{Guid.NewGuid():N}";
    }

    private static string MakeEntityNameFromMeshPath(string meshPath)
    {
        string file = Path.GetFileNameWithoutExtension(meshPath.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(file))
            file = "Model";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            file = file.Replace(invalid, '_');

        return file.Replace(' ', '_');
    }

    public bool AddEntityFromTemplate(LevelEntityDef template, string? nameSuffix = null)
    {
        LevelEntityDef copy = CloneEntity(template);
        if (!string.IsNullOrWhiteSpace(nameSuffix))
            copy.Name = $"{copy.Name ?? copy.Type}_{nameSuffix}";

        BeginEdit();
        copy.LocalPosition = copy.LocalPosition + new Vector3(0.5f, 0f, 0.5f);
        LevelFile.Entities.Add(copy);
        SelectedEntityIndex = LevelFile.Entities.Count - 1;
        Dirty = true;
        RebuildRuntimeFromLevel();
        EndEditIfAny();
        return true;
    }
    public bool TryBuildPrefabFromSelection(string prefabName, string basePrefabPath, bool isVariant, out PrefabFile prefab)
    {
        prefab = null!;
        if (SelectedEntityIndex < 0 || SelectedEntityIndex >= LevelFile.Entities.Count)
            return false;

        List<int> ordered = GetHierarchyIndicesDepthFirst(SelectedEntityIndex);
        if (ordered.Count == 0)
            return false;

        HashSet<int> included = ordered.ToHashSet();
        var entities = new List<LevelEntityDef>(ordered.Count);
        string rootId = LevelFile.Entities[SelectedEntityIndex].Id;

        foreach (int index in ordered)
        {
            LevelEntityDef source = LevelFile.Entities[index];
            LevelEntityDef copy = CloneEntityExact(source);
            ClearPrefabMetadata(copy);

            if (index == SelectedEntityIndex || FindIndexById(source.ParentId) is int parentIndex && !included.Contains(parentIndex))
            {
                copy.ParentId = null;
                if (index == SelectedEntityIndex)
                    copy.LocalPosition = Vector3.Zero;
            }

            entities.Add(copy);
        }

        prefab = new PrefabFile
        {
            Name = string.IsNullOrWhiteSpace(prefabName) ? LevelFile.Entities[SelectedEntityIndex].Name ?? "Prefab" : prefabName,
            RootEntityId = rootId,
            BasePrefabPath = basePrefabPath ?? "",
            IsVariant = isVariant,
            Entities = entities
        };
        return true;
    }

    public bool TryBuildPrefabFromSelectedInstanceForApply(string prefabName, out PrefabFile prefab, out string assetPath)
    {
        prefab = null!;
        assetPath = "";

        if (!TryGetSelectedPrefabInstance(out string instanceId, out assetPath))
            return false;

        List<int> instanceIndices = GetPrefabInstanceIndices(instanceId);
        if (instanceIndices.Count == 0)
            return false;

        int rootIndex = FindPrefabInstanceRootIndex(instanceIndices, "");
        if (rootIndex < 0)
            rootIndex = instanceIndices[0];

        List<int> ordered = GetHierarchyIndicesDepthFirst(rootIndex)
            .Where(instanceIndices.Contains)
            .ToList();

        foreach (int index in instanceIndices)
            if (!ordered.Contains(index))
                ordered.Add(index);

        var sceneIdToPrefabId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (int index in ordered)
        {
            LevelEntityDef entity = LevelFile.Entities[index];
            string prefabEntityId = !string.IsNullOrWhiteSpace(entity.PrefabSourceEntityId)
                ? entity.PrefabSourceEntityId
                : entity.Id;
            sceneIdToPrefabId[entity.Id] = prefabEntityId;
        }

        var entities = new List<LevelEntityDef>(ordered.Count);
        foreach (int index in ordered)
        {
            LevelEntityDef source = LevelFile.Entities[index];
            LevelEntityDef copy = CloneEntityExact(source);
            ClearPrefabMetadata(copy);

            copy.Id = sceneIdToPrefabId[source.Id];
            copy.ParentId = !string.IsNullOrWhiteSpace(source.ParentId) && sceneIdToPrefabId.TryGetValue(source.ParentId, out string? parentId)
                ? parentId
                : null;

            if (index == rootIndex)
            {
                copy.ParentId = null;
                copy.LocalPosition = Vector3.Zero;
            }

            entities.Add(copy);
        }

        prefab = new PrefabFile
        {
            Name = string.IsNullOrWhiteSpace(prefabName) ? LevelFile.Entities[rootIndex].Name ?? "Prefab" : prefabName,
            RootEntityId = sceneIdToPrefabId[LevelFile.Entities[rootIndex].Id],
            Entities = entities
        };
        return true;
    }

    public bool AddPrefabInstance(PrefabFile prefab, string assetPath, string? nameSuffix = null)
    {
        if (prefab.Entities == null || prefab.Entities.Count == 0)
            return false;

        List<LevelEntityDef> ordered = OrderPrefabEntities(prefab);
        if (ordered.Count == 0)
            return false;

        string rootSourceId = !string.IsNullOrWhiteSpace(prefab.RootEntityId) ? prefab.RootEntityId : ordered[0].Id;
        string instanceId = Guid.NewGuid().ToString("N");
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Vector3 placementOffset = new(0.5f, 0f, 0.5f);

        BeginEdit();

        int firstInsertedIndex = LevelFile.Entities.Count;
        int selectedRootIndex = firstInsertedIndex;
        foreach (LevelEntityDef source in ordered)
        {
            LevelEntityDef copy = CloneEntityExact(source);
            string newId = Guid.NewGuid().ToString("N");
            idMap[source.Id] = newId;

            copy.Id = newId;
            copy.PrefabAssetPath = assetPath ?? "";
            copy.PrefabInstanceId = instanceId;
            copy.PrefabSourceEntityId = source.Id;
            copy.PrefabUnpacked = false;
            copy.ParentId = !string.IsNullOrWhiteSpace(source.ParentId) && idMap.TryGetValue(source.ParentId, out string? mappedParent)
                ? mappedParent
                : null;

            if (string.Equals(source.Id, rootSourceId, StringComparison.OrdinalIgnoreCase))
            {
                copy.ParentId = null;
                copy.LocalPosition = copy.LocalPosition + placementOffset;
                selectedRootIndex = LevelFile.Entities.Count;
            }

            string baseName = copy.Name ?? copy.Type;
            copy.Name = string.IsNullOrWhiteSpace(nameSuffix)
                ? MakeUniqueEntityName(baseName)
                : MakeUniqueEntityName($"{baseName}_{nameSuffix}");

            MakePlacedPrefabInteractionStateUnique(copy, instanceId);

            LevelFile.Entities.Add(copy);
        }

        SelectedEntityIndex = Math.Clamp(selectedRootIndex, firstInsertedIndex, LevelFile.Entities.Count - 1);
        Dirty = true;
        RebuildRuntimeFromLevel();
        EndEditIfAny();
        return true;
    }

    private static void MakePlacedPrefabInteractionStateUnique(LevelEntityDef entity, string instanceId)
    {
        if (entity.Interaction == null || string.IsNullOrWhiteSpace(entity.Interaction.Kind))
            return;

        string suffix = string.IsNullOrWhiteSpace(instanceId)
            ? Guid.NewGuid().ToString("N")[..8]
            : instanceId[..Math.Min(8, instanceId.Length)];

        string baseStateId = !string.IsNullOrWhiteSpace(entity.Interaction.StateId)
            ? entity.Interaction.StateId
            : $"{entity.Interaction.Kind}_{entity.Name ?? entity.Id}";

        if (!baseStateId.EndsWith($"_{suffix}", StringComparison.OrdinalIgnoreCase))
            entity.Interaction.StateId = $"{baseStateId}_{suffix}";
    }
    public bool TryGetSelectedPrefabInstance(out string instanceId, out string assetPath)
    {
        instanceId = "";
        assetPath = "";

        if (!TryGetSelectedEntity(out LevelEntityDef entity))
            return false;

        if (entity.PrefabUnpacked || string.IsNullOrWhiteSpace(entity.PrefabInstanceId) || string.IsNullOrWhiteSpace(entity.PrefabAssetPath))
            return false;

        instanceId = entity.PrefabInstanceId;
        assetPath = entity.PrefabAssetPath;
        return true;
    }

    public bool UnpackSelectedPrefabInstance()
    {
        if (!TryGetSelectedPrefabInstance(out string instanceId, out _))
            return false;

        BeginEdit();
        foreach (LevelEntityDef entity in LevelFile.Entities)
        {
            if (!string.Equals(entity.PrefabInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                continue;

            ClearPrefabMetadata(entity);
            entity.PrefabUnpacked = true;
        }

        Dirty = true;
        RebuildRuntimeFromLevel();
        EndEditIfAny();
        return true;
    }

    public bool RevertSelectedPrefabInstance(PrefabFile prefab, string assetPath)
    {
        if (!TryGetSelectedPrefabInstance(out string instanceId, out _))
            return false;

        List<int> oldIndices = GetPrefabInstanceIndices(instanceId);
        if (oldIndices.Count == 0 || prefab.Entities.Count == 0)
            return false;

        int oldRootIndex = FindPrefabInstanceRootIndex(oldIndices, prefab.RootEntityId);
        if (oldRootIndex < 0)
            oldRootIndex = oldIndices[0];

        LevelEntityDef oldRoot = LevelFile.Entities[oldRootIndex];
        string? preservedParentId = oldRoot.ParentId;
        Vector3 preservedPosition = oldRoot.LocalPosition;
        Vector3 preservedRotation = oldRoot.LocalRotationEulerDeg;
        Vector3 preservedScale = oldRoot.LocalScale;
        string? preservedName = oldRoot.Name;
        int insertAt = oldIndices.Min();

        List<LevelEntityDef> ordered = OrderPrefabEntities(prefab);
        string rootSourceId = !string.IsNullOrWhiteSpace(prefab.RootEntityId) ? prefab.RootEntityId : ordered[0].Id;
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newEntities = new List<LevelEntityDef>(ordered.Count);
        int newRootOffset = 0;

        foreach (LevelEntityDef source in ordered)
        {
            LevelEntityDef copy = CloneEntityExact(source);
            string newId = Guid.NewGuid().ToString("N");
            idMap[source.Id] = newId;

            copy.Id = newId;
            copy.PrefabAssetPath = assetPath ?? "";
            copy.PrefabInstanceId = instanceId;
            copy.PrefabSourceEntityId = source.Id;
            copy.PrefabUnpacked = false;
            copy.ParentId = !string.IsNullOrWhiteSpace(source.ParentId) && idMap.TryGetValue(source.ParentId, out string? mappedParent)
                ? mappedParent
                : null;

            if (string.Equals(source.Id, rootSourceId, StringComparison.OrdinalIgnoreCase))
            {
                copy.ParentId = preservedParentId;
                copy.LocalPosition = preservedPosition;
                copy.LocalRotationEulerDeg = preservedRotation;
                copy.LocalScale = preservedScale;
                copy.Name = preservedName;
                newRootOffset = newEntities.Count;
            }

            newEntities.Add(copy);
        }

        BeginEdit();
        foreach (int index in oldIndices.OrderByDescending(static index => index))
            LevelFile.Entities.RemoveAt(index);

        insertAt = Math.Clamp(insertAt, 0, LevelFile.Entities.Count);
        LevelFile.Entities.InsertRange(insertAt, newEntities);
        SelectedEntityIndex = insertAt + newRootOffset;
        Dirty = true;
        RebuildRuntimeFromLevel();
        EndEditIfAny();
        return true;
    }

    private List<int> GetHierarchyIndicesDepthFirst(int rootIndex)
    {
        var children = BuildChildrenMap();
        var ordered = new List<int>();
        CollectHierarchyIndices(rootIndex, children, ordered);
        return ordered;
    }

    private void CollectHierarchyIndices(int index, Dictionary<int, List<int>> children, List<int> ordered)
    {
        if (index < 0 || index >= LevelFile.Entities.Count || ordered.Contains(index))
            return;

        ordered.Add(index);
        if (!children.TryGetValue(index, out List<int>? kids))
            return;

        foreach (int child in kids)
            CollectHierarchyIndices(child, children, ordered);
    }

    private List<int> GetPrefabInstanceIndices(string instanceId)
    {
        var indices = new List<int>();
        for (int i = 0; i < LevelFile.Entities.Count; i++)
        {
            if (string.Equals(LevelFile.Entities[i].PrefabInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                indices.Add(i);
        }
        return indices;
    }

    private int FindPrefabInstanceRootIndex(List<int> instanceIndices, string rootSourceId)
    {
        HashSet<int> set = instanceIndices.ToHashSet();
        foreach (int index in instanceIndices)
        {
            LevelEntityDef entity = LevelFile.Entities[index];
            if (!string.IsNullOrWhiteSpace(rootSourceId) &&
                string.Equals(entity.PrefabSourceEntityId, rootSourceId, StringComparison.OrdinalIgnoreCase))
                return index;

            int parentIndex = FindIndexById(entity.ParentId);
            if (parentIndex < 0 || !set.Contains(parentIndex))
                return index;
        }

        return -1;
    }

    private static List<LevelEntityDef> OrderPrefabEntities(PrefabFile prefab)
    {
        Dictionary<string, LevelEntityDef> byId = prefab.Entities
            .Where(static entity => !string.IsNullOrWhiteSpace(entity.Id))
            .ToDictionary(static entity => entity.Id, static entity => entity, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<LevelEntityDef>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(LevelEntityDef entity)
        {
            if (string.IsNullOrWhiteSpace(entity.Id) || !visited.Add(entity.Id))
                return;

            if (!string.IsNullOrWhiteSpace(entity.ParentId) && byId.TryGetValue(entity.ParentId, out LevelEntityDef? parent))
                Visit(parent);

            ordered.Add(entity);
        }

        if (!string.IsNullOrWhiteSpace(prefab.RootEntityId) && byId.TryGetValue(prefab.RootEntityId, out LevelEntityDef? root))
            Visit(root);

        foreach (LevelEntityDef entity in prefab.Entities)
            Visit(entity);

        return ordered;
    }

    private static LevelEntityDef CloneEntityExact(LevelEntityDef src)
    {
        string json = JsonSerializer.Serialize(src);
        return JsonSerializer.Deserialize<LevelEntityDef>(json) ?? new LevelEntityDef();
    }

    private static void ClearPrefabMetadata(LevelEntityDef entity)
    {
        entity.PrefabAssetPath = "";
        entity.PrefabInstanceId = "";
        entity.PrefabSourceEntityId = "";
        entity.PrefabUnpacked = false;
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
        string json = JsonSerializer.Serialize(src);
        LevelEntityDef copy = JsonSerializer.Deserialize<LevelEntityDef>(json) ?? new LevelEntityDef();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = (src.Name ?? "Entity") + "_copy";
        ClearPrefabMetadata(copy);
        return copy;
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

        if (ImGui.Button("Save"))
        {
            if (SaveActionOverride != null) SaveActionOverride();
            else Save();
        }
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

        // ----------------------------
        // Gameplay / interaction flags
        // ----------------------------
        ImGui.Separator();
        ImGui.Text("Gameplay");

        bool canPickUp = ent.CanPickUp;
        if (ImGui.Checkbox("Can Pick Up", ref canPickUp))
        {
            BeginEdit();
            ent.CanPickUp = canPickUp;
            Dirty = true;

            // keep runtime mirrors in sync if you rely on them during play
            RebuildRuntimeFromLevel();

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

        DrawInteractionInspector(ent);

        DrawDamageableInspector(ent);

        ImGui.Separator();
        DrawScriptsInspector(ent);

        ImGui.Separator();
        DrawHelp();

        ImGui.End();
    }


    private void DrawMeshPathField(LevelEntityDef ent)
    {
        string mesh = ent.MeshPath ?? "";
        if (ImGui.InputText("MeshPath", ref mesh, 256))
        {
            BeginEdit();
            ent.MeshPath = mesh;
            Dirty = true;
            RebuildRuntimeFromLevel();
            EndEditIfAny();
        }

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(GlbAssetDragDropPayload, ImGuiDragDropFlags.None);
            if (payload.Data != IntPtr.Zero && payload.Delivery && TryReadPayloadString(payload, out string modelPath) &&
                modelPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                BeginEdit();
                ent.MeshPath = modelPath;
                Dirty = true;
                RebuildRuntimeFromLevel();
                EndEditIfAny();
            }
            ImGui.EndDragDropTarget();
        }
    }

    private void DrawInteractionInspector(LevelEntityDef ent)
    {
        ImGui.Separator();
        if (!ImGui.CollapsingHeader("Interaction", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        bool hasInteraction = ent.Interaction != null && !string.IsNullOrWhiteSpace(ent.Interaction.Kind);
        if (!hasInteraction)
        {
            ImGui.TextWrapped("Add interaction data to this selected entity. The data is saved inside this entity in the level JSON.");
            if (ImGui.Button("Add Locked Door", new Vector2(-1f, 0f)))
                SetDefaultInteraction(ent, "LockedDoor");
            if (ImGui.Button("Add Locked Chest", new Vector2(-1f, 0f)))
                SetDefaultInteraction(ent, "LockedChest");
            if (ImGui.Button("Add Puzzle Slot", new Vector2(-1f, 0f)))
                SetDefaultInteraction(ent, "PuzzleSlot");
            if (ImGui.Button("Add Puzzle Lever", new Vector2(-1f, 0f)))
                SetDefaultInteraction(ent, "PuzzleLever");
            if (ImGui.Button("Add Puzzle Door", new Vector2(-1f, 0f)))
                SetDefaultInteraction(ent, "PuzzleDoor");
            return;
        }

        LevelInteractionDef interaction = ent.Interaction!;
        EnsureInteractionDefaults(ent, interaction);

        string[] kinds = ["LockedDoor", "LockedChest", "PuzzleSlot", "PuzzleLever", "PuzzleDoor", "None"];
        _interactionKindPickerIndex = Array.FindIndex(kinds, kind => string.Equals(kind, interaction.Kind, StringComparison.OrdinalIgnoreCase));
        if (_interactionKindPickerIndex < 0)
            _interactionKindPickerIndex = 0;

        if (DrawInteractionCombo("Interaction Kind", ref _interactionKindPickerIndex, kinds))
        {
            BeginEdit();
            if (string.Equals(kinds[_interactionKindPickerIndex], "None", StringComparison.OrdinalIgnoreCase))
                ent.Interaction = null;
            else
            {
                interaction.Kind = kinds[_interactionKindPickerIndex];
                EnsureInteractionDefaults(ent, interaction);
            }
            CommitInteractionEdit();
            return;
        }

        if (ImGui.Button("Remove Interaction", new Vector2(-1f, 0f)))
        {
            BeginEdit();
            ent.Interaction = null;
            CommitInteractionEdit();
            return;
        }

        if (Dirty)
        {
            if (ImGui.Button("Save Active Document", new Vector2(-1f, 0f)))
            {
                if (SaveActionOverride != null) SaveActionOverride();
                else Save();
            }
        }

        ImGui.Spacing();

        string stateId = interaction.StateId ?? "";
        if (DrawInteractionInputText("State Id", ref stateId, 128))
        {
            BeginEdit();
            interaction.StateId = stateId;
            CommitInteractionEdit();
        }
        if (string.IsNullOrWhiteSpace(interaction.StateId))
            ImGui.TextColored(new Vector4(1f, 0.72f, 0.18f, 1f), "State Id should be unique for save/load persistence.");

        DrawInteractionRequiredItemField(interaction);

        bool consumesItem = interaction.ConsumesItem;
        if (ImGui.Checkbox("Consumes Item", ref consumesItem))
        {
            BeginEdit();
            interaction.ConsumesItem = consumesItem;
            CommitInteractionEdit();
        }

        DrawInteractionTextField("Prompt", value => interaction.Prompt = value, interaction.Prompt ?? "", 160);
        DrawInteractionTextField("Locked Prompt", value => interaction.LockedPrompt = value, interaction.LockedPrompt ?? "", 160);
        DrawInteractionTextField("Success Message", value => interaction.SuccessMessage = value, interaction.SuccessMessage ?? "", 220);

        if (IsInteractionKind(interaction, "LockedDoor"))
            DrawInteractionDoorHinge(interaction);

        if (IsInteractionKind(interaction, "PuzzleLever"))
            DrawInteractionRequiredStates(interaction);

        if (InteractionUsesTargets(interaction))
            DrawInteractionTargets(interaction);
        else
            DrawInteractionTargetsSummary(interaction);

        if (InteractionUsesRewards(interaction))
            DrawInteractionRewards(interaction);
        else
            DrawInteractionRewardsSummary(interaction);
    }

    private void CommitInteractionEdit()
    {
        Dirty = true;
        EndEditIfAny();
    }

    private void SetDefaultInteraction(LevelEntityDef ent, string kind)
    {
        BeginEdit();
        string requiredItem = GetDefaultRequiredItemForInteraction(ent, kind);
        ent.Interaction = new LevelInteractionDef
        {
            Kind = kind,
            StateId = MakeDefaultInteractionStateId(ent, kind),
            RequiredItem = requiredItem,
            ConsumesItem = false,
            Prompt = "",
            LockedPrompt = kind == "LockedDoor" || kind == "LockedChest" ? "Locked." : "",
            SuccessMessage = MakeDefaultInteractionSuccessMessage(kind, requiredItem),
            Targets = new List<string>(),
            RequiredStates = new List<string>(),
            Rewards = new List<LevelInteractionRewardDef>()
        };
        CommitInteractionEdit();
    }

    private void EnsureInteractionDefaults(LevelEntityDef ent, LevelInteractionDef interaction)
    {
        if (string.IsNullOrWhiteSpace(interaction.StateId))
            interaction.StateId = MakeDefaultInteractionStateId(ent, interaction.Kind);
        interaction.Targets ??= new List<string>();
        interaction.RequiredStates ??= new List<string>();
        interaction.Rewards ??= new List<LevelInteractionRewardDef>();
        if (interaction.OpenAngleDeg == 0f)
            interaction.OpenAngleDeg = 90f;
    }

    private static string MakeDefaultInteractionStateId(LevelEntityDef ent, string kind)
    {
        string name = ent.Name ?? ent.Id;
        string safe = string.IsNullOrWhiteSpace(name) ? "Entity" : name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        safe = safe.Replace(' ', '_');
        return $"{kind}_{safe}";
    }

    private static string GetDefaultRequiredItemForInteraction(LevelEntityDef ent, string kind)
    {
        string name = ent.Name ?? "";
        if (name.Contains("RustedKey", StringComparison.OrdinalIgnoreCase) || name.Contains("Rusted", StringComparison.OrdinalIgnoreCase))
            return "RustedKey";
        if (name.Contains("ServiceKey", StringComparison.OrdinalIgnoreCase) || name.Contains("Service", StringComparison.OrdinalIgnoreCase))
            return "ServiceKey";
        if (name.Contains("ArchiveKey", StringComparison.OrdinalIgnoreCase) || name.Contains("Archive", StringComparison.OrdinalIgnoreCase))
            return "ArchiveKey";
        if (name.Contains("Crank", StringComparison.OrdinalIgnoreCase))
            return "CrankHandle";
        if (name.Contains("Fuse", StringComparison.OrdinalIgnoreCase))
            return "Fuse";

        return kind == "PuzzleDoor" ? "" : "RustedKey";
    }

    private static string MakeDefaultInteractionSuccessMessage(string kind, string requiredItem)
    {
        if (kind == "PuzzleDoor")
            return "";
        if (kind == "PuzzleSlot")
            return string.IsNullOrWhiteSpace(requiredItem) ? "The mechanism turns." : $"The {requiredItem} clicks into place.";
        if (kind == "PuzzleLever")
            return "The lever clunks into position.";
        if (kind == "LockedChest")
            return string.IsNullOrWhiteSpace(requiredItem) ? "The chest opens." : $"The {requiredItem} opens the chest.";
        return string.IsNullOrWhiteSpace(requiredItem) ? "The door unlocks." : $"The {requiredItem} turns in the lock.";
    }

    private static bool InteractionUsesTargets(LevelInteractionDef interaction)
        => IsInteractionKind(interaction, "PuzzleSlot") || IsInteractionKind(interaction, "PuzzleLever");

    private static bool IsInteractionKind(LevelInteractionDef interaction, string kind)
        => string.Equals(interaction.Kind, kind, StringComparison.OrdinalIgnoreCase);

    private static bool InteractionUsesRewards(LevelInteractionDef interaction)
        => string.Equals(interaction.Kind, "LockedChest", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(interaction.Kind, "PuzzleSlot", StringComparison.OrdinalIgnoreCase);

    private static bool DrawInteractionCombo(string label, ref int index, string[] values)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1f);
        return ImGui.Combo($"##interaction_{label}", ref index, values, values.Length);
    }

    private static bool DrawInteractionInputText(string label, ref string value, uint maxLength)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1f);
        return ImGui.InputText($"##interaction_{label}", ref value, maxLength);
    }

    private void DrawInteractionRequiredItemField(LevelInteractionDef interaction)
    {
        string[] items = GetKnownInteractionItemIds();
        string[] labels = items.Select(static item => string.IsNullOrWhiteSpace(item) ? "(none)" : item).ToArray();
        if (items.Length > 0)
        {
            _interactionItemPickerIndex = Array.FindIndex(items, item => string.Equals(item, interaction.RequiredItem, StringComparison.OrdinalIgnoreCase));
            if (_interactionItemPickerIndex < 0)
                _interactionItemPickerIndex = 0;

            if (DrawInteractionCombo("Required Item", ref _interactionItemPickerIndex, labels))
            {
                BeginEdit();
                interaction.RequiredItem = items[_interactionItemPickerIndex];
                CommitInteractionEdit();
            }
        }

        string requiredItem = interaction.RequiredItem ?? "";
        if (DrawInteractionInputText("Required Item Id", ref requiredItem, 128))
        {
            BeginEdit();
            interaction.RequiredItem = requiredItem;
            CommitInteractionEdit();
        }
    }

    private void DrawInteractionTextField(string label, Action<string> setter, string currentValue, uint maxLength)
    {
        string value = currentValue;
        if (DrawInteractionInputText(label, ref value, maxLength))
        {
            BeginEdit();
            setter(value);
            CommitInteractionEdit();
        }
    }

    private void DrawInteractionDoorHinge(LevelInteractionDef interaction)
    {
        ImGui.SeparatorText("Door Hinge");
        ImGui.TextWrapped("Optional local hinge pivot for swing doors. Leave at 0,0,0 to infer the hinge from the door size; set X or Z to the hinge edge for a mounted door.");

        Vector3 hinge = interaction.HingeLocalOffset;
        ImGui.TextUnformatted("Hinge Local Offset");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.DragFloat3("##interaction_hingeLocalOffset", ref hinge, 0.01f))
        {
            BeginEdit();
            interaction.HingeLocalOffset = hinge;
            CommitInteractionEdit();
        }

        float openAngle = interaction.OpenAngleDeg == 0f ? 90f : interaction.OpenAngleDeg;
        ImGui.TextUnformatted("Open Angle Deg");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.DragFloat("##interaction_openAngleDeg", ref openAngle, 1f, 1f, 179f))
        {
            BeginEdit();
            interaction.OpenAngleDeg = Math.Clamp(openAngle, 1f, 179f);
            CommitInteractionEdit();
        }
    }

    private void DrawInteractionRequiredStates(LevelInteractionDef interaction)
    {
        interaction.RequiredStates ??= new List<string>();
        ImGui.SeparatorText("Required States");
        ImGui.TextWrapped("Puzzle levers only activate when all required interaction state ids have been solved.");

        string[] states = LevelFile.Entities
            .Where(entity => entity.Interaction != null && !string.IsNullOrWhiteSpace(GetInteractionStateIdForEditor(entity)))
            .Select(GetInteractionStateIdForEditor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static state => state, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (states.Length > 0)
        {
            _interactionRequiredStatePickerIndex = Math.Clamp(_interactionRequiredStatePickerIndex, 0, states.Length - 1);
            DrawInteractionCombo("Required State", ref _interactionRequiredStatePickerIndex, states);
            if (ImGui.Button("Add Required State", new Vector2(-1f, 0f)))
            {
                string state = states[_interactionRequiredStatePickerIndex];
                if (!interaction.RequiredStates.Contains(state, StringComparer.OrdinalIgnoreCase))
                {
                    BeginEdit();
                    interaction.RequiredStates.Add(state);
                    CommitInteractionEdit();
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No interaction state ids are available.");
        }

        if (interaction.RequiredStates.Count == 0)
        {
            ImGui.TextDisabled("No required states assigned.");
            return;
        }

        if (ImGui.Button("Clear Required States", new Vector2(-1f, 0f)))
        {
            BeginEdit();
            interaction.RequiredStates.Clear();
            CommitInteractionEdit();
            return;
        }

        for (int i = 0; i < interaction.RequiredStates.Count; i++)
        {
            ImGui.PushID($"requiredState{i}");
            string state = interaction.RequiredStates[i] ?? "";
            if (DrawInteractionInputText($"Required State {i + 1}", ref state, 160))
            {
                BeginEdit();
                interaction.RequiredStates[i] = state;
                CommitInteractionEdit();
            }
            if (ImGui.Button("Remove Required State", new Vector2(-1f, 0f)))
            {
                BeginEdit();
                interaction.RequiredStates.RemoveAt(i);
                CommitInteractionEdit();
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }
    }

    private static string GetInteractionStateIdForEditor(LevelEntityDef entity)
        => !string.IsNullOrWhiteSpace(entity.Interaction?.StateId)
            ? entity.Interaction.StateId
            : entity.Name ?? entity.Id;

    private void DrawInteractionTargetsSummary(LevelInteractionDef interaction)
    {
        interaction.Targets ??= new List<string>();
        ImGui.SeparatorText("Targets");
        ImGui.TextWrapped("Targets are used by puzzle slots and puzzle levers. Locked doors and chests act on the selected object itself.");
        if (interaction.Targets.Count <= 0)
            return;

        ImGui.TextColored(new Vector4(1f, 0.72f, 0.18f, 1f), $"This interaction has {interaction.Targets.Count} unused target(s).");
        if (ImGui.Button("Clear Unused Targets", new Vector2(-1f, 0f)))
        {
            BeginEdit();
            interaction.Targets.Clear();
            CommitInteractionEdit();
        }
    }

    private void DrawInteractionTargets(LevelInteractionDef interaction)
    {
        interaction.Targets ??= new List<string>();
        ImGui.SeparatorText("Targets");
        ImGui.TextWrapped("Targets are other named entities affected by this interaction. Use these for puzzle slots and levers that open puzzle doors or move reveal pieces.");

        string[] names = LevelFile.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => entity.Name!)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length > 0)
        {
            _interactionTargetPickerIndex = Math.Clamp(_interactionTargetPickerIndex, 0, names.Length - 1);
            DrawInteractionCombo("Target Entity", ref _interactionTargetPickerIndex, names);
            if (ImGui.Button("Add Target", new Vector2(-1f, 0f)))
            {
                string target = names[_interactionTargetPickerIndex];
                if (!interaction.Targets.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    BeginEdit();
                    interaction.Targets.Add(target);
                    CommitInteractionEdit();
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No named entities are available.");
        }

        if (interaction.Targets.Count == 0)
        {
            ImGui.TextDisabled("No targets assigned.");
            return;
        }

        if (ImGui.Button("Clear All Targets", new Vector2(-1f, 0f)))
        {
            BeginEdit();
            interaction.Targets.Clear();
            CommitInteractionEdit();
            return;
        }

        for (int i = 0; i < interaction.Targets.Count; i++)
        {
            ImGui.PushID($"target{i}");
            string target = interaction.Targets[i] ?? "";
            if (DrawInteractionInputText($"Target {i + 1}", ref target, 128))
            {
                BeginEdit();
                interaction.Targets[i] = target;
                CommitInteractionEdit();
            }
            if (ImGui.Button("Remove Target", new Vector2(-1f, 0f)))
            {
                BeginEdit();
                interaction.Targets.RemoveAt(i);
                CommitInteractionEdit();
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }
    }

    private void DrawInteractionRewardsSummary(LevelInteractionDef interaction)
    {
        interaction.Rewards ??= new List<LevelInteractionRewardDef>();
        ImGui.SeparatorText("Rewards");
        ImGui.TextWrapped("Rewards are currently useful for chest or puzzle-slot style interactions. This interaction type does not consume reward data yet.");
        if (interaction.Rewards.Count <= 0)
            return;

        ImGui.TextColored(new Vector4(1f, 0.72f, 0.18f, 1f), $"This interaction has {interaction.Rewards.Count} unused reward item(s).");
        if (ImGui.Button("Clear Unused Rewards", new Vector2(-1f, 0f)))
        {
            BeginEdit();
            interaction.Rewards.Clear();
            CommitInteractionEdit();
        }
    }

    private void DrawInteractionRewards(LevelInteractionDef interaction)
    {
        interaction.Rewards ??= new List<LevelInteractionRewardDef>();
        ImGui.SeparatorText("Rewards");
        ImGui.TextWrapped("Optional item rewards for this interaction. Runtime support is still interaction-kind specific.");

        string[] items = GetKnownInteractionItemIds(includeEmpty: false);
        if (items.Length > 0)
        {
            _interactionRewardItemPickerIndex = Math.Clamp(_interactionRewardItemPickerIndex, 0, items.Length - 1);
            DrawInteractionCombo("Reward Item", ref _interactionRewardItemPickerIndex, items);
            if (ImGui.Button("Add Reward", new Vector2(-1f, 0f)))
            {
                BeginEdit();
                interaction.Rewards.Add(new LevelInteractionRewardDef { ItemId = items[_interactionRewardItemPickerIndex], Count = 1 });
                CommitInteractionEdit();
            }
        }

        if (interaction.Rewards.Count == 0)
        {
            ImGui.TextDisabled("No rewards assigned.");
            return;
        }

        if (ImGui.Button("Clear All Rewards", new Vector2(-1f, 0f)))
        {
            BeginEdit();
            interaction.Rewards.Clear();
            CommitInteractionEdit();
            return;
        }

        for (int i = 0; i < interaction.Rewards.Count; i++)
        {
            ImGui.PushID($"reward{i}");
            LevelInteractionRewardDef reward = interaction.Rewards[i];
            string itemId = reward.ItemId ?? "";
            int count = Math.Clamp(reward.Count, 1, 999);

            if (DrawInteractionInputText($"Reward {i + 1} Item Id", ref itemId, 128))
            {
                BeginEdit();
                reward.ItemId = itemId;
                CommitInteractionEdit();
            }

            ImGui.TextUnformatted("Reward Count");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputInt("##rewardCount", ref count))
            {
                BeginEdit();
                reward.Count = Math.Clamp(count, 1, 999);
                CommitInteractionEdit();
            }

            if (ImGui.Button("Remove Reward", new Vector2(-1f, 0f)))
            {
                BeginEdit();
                interaction.Rewards.RemoveAt(i);
                CommitInteractionEdit();
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }
    }

    private static string[] GetKnownInteractionItemIds(bool includeEmpty = true)
    {
        string[] ids =
        [
            "",
            "RustedKey",
            "ServiceKey",
            "ArchiveKey",
            "MasterKey",
            "CrankHandle",
            "Fuse",
            "InkRibbon",
            "HealthPack",
            "SuitBattery",
            "Scrap",
            "Gunpowder",
            "Bullets"
        ];

        return includeEmpty ? ids : ids.Where(static id => !string.IsNullOrWhiteSpace(id)).ToArray();
    }
    private void DrawDamageableInspector(LevelEntityDef ent)
    {
        if (!SupportsDamageableSettings(ent))
            return;

        ent.BreakReplacementModelPaths ??= new List<string>();
        ent.BreakDebrisModelPaths ??= new List<string>();

        ImGui.Separator();
        ImGui.Text("Damage");

        bool damageable = ent.Damageable;
        if (ImGui.Checkbox("Damageable", ref damageable))
        {
            BeginEdit();
            ent.Damageable = damageable;
            Dirty = true;
            RebuildRuntimeFromLevel();
            EndEditIfAny();
        }

        if (!ent.Damageable)
            return;

        float maxHealth = MathF.Max(1f, ent.MaxHealth);
        if (ImGui.DragFloat("Max Health", ref maxHealth, 1f, 1f, 100000f))
        {
            BeginEdit();
            ent.MaxHealth = MathF.Max(1f, maxHealth);
            Dirty = true;
            RebuildRuntimeFromLevel();
            EndEditIfAny();
        }

        bool keepsPhysics = ent.BreakReplacementKeepsPhysics;
        if (ImGui.Checkbox("Replacement Keeps Physics", ref keepsPhysics))
        {
            BeginEdit();
            ent.BreakReplacementKeepsPhysics = keepsPhysics;
            Dirty = true;
            RebuildRuntimeFromLevel();
            EndEditIfAny();
        }

        string[] modelPaths = GetAvailableGlbModelAssetPaths();
        if (modelPaths.Length == 0)
        {
            ImGui.TextDisabled("No .glb models found under Content/Models.");
        }
        else
        {
            _damageModelPickerIndex = Math.Clamp(_damageModelPickerIndex, 0, modelPaths.Length - 1);
            ImGui.SetNextItemWidth(260f);
            ImGui.Combo("Replacement Model", ref _damageModelPickerIndex, modelPaths, modelPaths.Length);
            ImGui.SameLine();
            if (ImGui.Button("Add Replacement"))
            {
                string selected = modelPaths[_damageModelPickerIndex];
                if (!ent.BreakReplacementModelPaths.Contains(selected, StringComparer.OrdinalIgnoreCase))
                {
                    BeginEdit();
                    ent.BreakReplacementModelPaths.Add(selected);
                    Dirty = true;
                    RebuildRuntimeFromLevel();
                    EndEditIfAny();
                }
            }
        }

        DrawModelPathList("Replacement", ent.BreakReplacementModelPaths);

        if (ImGui.TreeNode("Debris Models (future)"))
        {
            if (modelPaths.Length > 0)
            {
                _debrisModelPickerIndex = Math.Clamp(_debrisModelPickerIndex, 0, modelPaths.Length - 1);
                ImGui.SetNextItemWidth(260f);
                ImGui.Combo("Debris Model", ref _debrisModelPickerIndex, modelPaths, modelPaths.Length);
                ImGui.SameLine();
                if (ImGui.Button("Add Debris"))
                {
                    string selected = modelPaths[_debrisModelPickerIndex];
                    if (!ent.BreakDebrisModelPaths.Contains(selected, StringComparer.OrdinalIgnoreCase))
                    {
                        BeginEdit();
                        ent.BreakDebrisModelPaths.Add(selected);
                        Dirty = true;
                        EndEditIfAny();
                    }
                }
            }

            DrawModelPathList("Debris", ent.BreakDebrisModelPaths);
            ImGui.TreePop();
        }
    }

    private void DrawModelPathList(string label, List<string> paths)
    {
        DrawModelPathDropTarget(label, paths);

        if (paths.Count == 0)
        {
            ImGui.TextDisabled($"No {label.ToLowerInvariant()} models selected.");
            return;
        }

        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i] ?? "";
            ImGui.SetNextItemWidth(320f);
            if (ImGui.InputText($"{label} {i + 1}##{label}{i}", ref path, 256))
            {
                BeginEdit();
                paths[i] = path;
                Dirty = true;
                RebuildRuntimeFromLevel();
                EndEditIfAny();
            }

            ImGui.SameLine();
            if (ImGui.Button($"Remove##{label}{i}"))
            {
                BeginEdit();
                paths.RemoveAt(i);
                Dirty = true;
                RebuildRuntimeFromLevel();
                EndEditIfAny();
                break;
            }
        }
    }

    private void DrawModelPathDropTarget(string label, List<string> paths)
    {
        ImGui.PushID($"{label}DropTarget");
        ImGui.Button($"Drop GLB here to add {label.ToLowerInvariant()} model", new Vector2(-1f, 24f));
        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(GlbAssetDragDropPayload, ImGuiDragDropFlags.None);
            unsafe
            {
                if (payload.NativePtr != null && payload.Delivery && TryReadPayloadString(payload, out string modelPath))
                {
                    if (modelPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
                        !paths.Contains(modelPath, StringComparer.OrdinalIgnoreCase))
                    {
                        BeginEdit();
                        paths.Add(modelPath);
                        Dirty = true;
                        RebuildRuntimeFromLevel();
                        EndEditIfAny();
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }
        ImGui.PopID();
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

    private static bool SupportsDamageableSettings(LevelEntityDef ent)
        => ent.Type == EntityTypes.Box || ent.Type == EntityTypes.RigidBody || ent.Type == EntityTypes.Prop;

    private string[] GetAvailableGlbModelAssetPaths()
    {
        string contentRoot = ResolveEditorContentRoot();
        string modelRoot = Path.Combine(contentRoot, "Models");
        if (!Directory.Exists(modelRoot))
            return [];

        return Directory.EnumerateFiles(modelRoot, "*.glb", SearchOption.AllDirectories)
            .Select(path => "Content/" + Path.GetRelativePath(contentRoot, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ResolveEditorContentRoot()
    {
        string? start = !string.IsNullOrWhiteSpace(LevelPath)
            ? Path.GetDirectoryName(LevelPath)
            : AppContext.BaseDirectory;

        DirectoryInfo? dir = string.IsNullOrWhiteSpace(start) ? null : new DirectoryInfo(start);
        while (dir != null)
        {
            if (string.Equals(dir.Name, "Content", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;

            string candidate = Path.Combine(dir.FullName, "Content");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Content");
    }

    private void DrawScriptsInspector(LevelEntityDef ent)
    {
        ImGui.Separator();
        ImGui.Text("Scripts");

        if (_scripts == null)
        {
            ImGui.TextDisabled("No script registry set (Game should call SetScriptRegistry).");
            return;
        }

        var types = _scripts.Types.ToArray();
        if (types.Length == 0)
        {
            ImGui.TextDisabled("No scripts registered.");
        }
        else
        {
            if (_addScriptSelectedIndex < 0 || _addScriptSelectedIndex >= types.Length)
                _addScriptSelectedIndex = 0;

            ImGui.SetNextItemWidth(200);
            ImGui.Combo("##AddScriptCombo", ref _addScriptSelectedIndex, types, types.Length);

            ImGui.SameLine();
            if (ImGui.Button("Add Script"))
            {
                string type = types[_addScriptSelectedIndex];

                bool alreadyHas = ent.Scripts.Any(s => s.Type == type);
                if (!alreadyHas)
                {
                    BeginEdit();
                    ent.Scripts.Add(new ScriptDef { Type = type, Json = "{}" });
                    Dirty = true;
                    RebuildRuntimeFromLevel();
                    EndEditIfAny();
                }
            }
        }

        ImGui.Separator();

        if (ent.Scripts.Count == 0)
        {
            ImGui.TextDisabled("No scripts attached.");
            return;
        }

        for (int i = 0; i < ent.Scripts.Count; i++)
        {
            var s = ent.Scripts[i];

            if (!ImGui.TreeNode($"{s.Type}##script{i}"))
                continue;

            if (ImGui.Button($"Remove##rm{i}"))
            {
                BeginEdit();
                ent.Scripts.RemoveAt(i);
                Dirty = true;
                RebuildRuntimeFromLevel();
                EndEditIfAny();
                ImGui.TreePop();
                break;
            }

            if (_scripts.TryCreateParams(s.Type, out var paramsObj))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(s.Json))
                    {
                        var loaded = System.Text.Json.JsonSerializer.Deserialize(s.Json, paramsObj.GetType());
                        if (loaded != null) paramsObj = loaded;
                    }
                }
                catch { }

                bool changed = ImGuiAutoInspector.DrawObject(paramsObj);

                if (changed)
                {
                    BeginEdit();
                    s.Json = System.Text.Json.JsonSerializer.Serialize(paramsObj, paramsObj.GetType());
                    Dirty = true;
                    RebuildRuntimeFromLevel();
                    EndEditIfAny();
                }
            }
            else
            {
                ImGui.TextDisabled("No params registered for this script.");
            }

            ImGui.TreePop();
        }
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
            // Motion type dropdown
            var motion = ent.MotionType;
            int motionIndex = (int)motion;

            string[] motionLabels = { "Static", "Dynamic", "Kinematic" };

            if (ImGui.Combo("Motion Type", ref motionIndex, motionLabels, motionLabels.Length))
            {
                BeginEdit();
                ent.MotionType = (MotionType)motionIndex;
                Dirty = true;
                RebuildRuntimeFromLevel();
                EndEditIfAny();
            }

            // Only show pickup if dynamic
            if (ent.MotionType == MotionType.Dynamic)
            {
                bool canPick = ent.CanPickUp;
                if (ImGui.Checkbox("Can Pick Up", ref canPick))
                {
                    BeginEdit();
                    ent.CanPickUp = canPick;
                    Dirty = true;
                    EndEditIfAny();
                }
            }

            Vector3 size = ent.Size;
            if (ImGui.DragFloat3("Size", ref size, 0.05f))
            {
                bool canPickUp = ent.CanPickUp;
                BeginEdit();
                if (ImGui.Checkbox("Can Pick Up", ref canPickUp))
                {
                    ent.CanPickUp = canPickUp;
                    Dirty = true;
                    RebuildRuntimeFromLevel();
                }

                

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


            DrawMeshPathField(ent);
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

            DrawMeshPathField(ent);

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
            var motion = ent.MotionType;
            int motionIndex = (int)motion;

            string[] motionLabels = { "Static", "Dynamic", "Kinematic" };

            if (ImGui.Combo("Motion Type", ref motionIndex, motionLabels, motionLabels.Length))
            {
                BeginEdit();
                ent.MotionType = (MotionType)motionIndex;
                Dirty = true;
                RebuildRuntimeFromLevel();
                EndEditIfAny();
            }

            if (ent.MotionType == MotionType.Dynamic)
            {
                bool canPick = ent.CanPickUp;
                if (ImGui.Checkbox("Can Pick Up", ref canPick))
                {
                    BeginEdit();
                    ent.CanPickUp = canPick;
                    Dirty = true;
                    EndEditIfAny();
                }
            }

            int shapeIndex = IsMeshShape(ent.Shape) ? 3 : IsCapsuleShape(ent.Shape) ? 2 : IsSphereShape(ent.Shape) ? 1 : 0;
            if (ImGui.Combo("Shape", ref shapeIndex, "Box\0Sphere\0Capsule\0Mesh\0"))
            {
                BeginEdit();
                ent.Shape = shapeIndex switch
                {
                    1 => "Sphere",
                    2 => "Capsule",
                    3 => "Mesh",
                    _ => "Box"
                };
                if (shapeIndex == 3)
                {
                    ent.MotionType = MotionType.Static;
                    ent.Mass = 0f;
                }
                Dirty = true;
                RebuildRuntimeFromLevel();
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

            DrawMeshPathField(ent);

            if (IsSphereShape(ent.Shape))
            {
                float radius = ent.Radius;
                if (ImGui.DragFloat("Radius", ref radius, 0.05f, 0.01f, 1000f))
                {
                    BeginEdit();
                    ent.Radius = MathF.Max(0.01f, radius);
                    Dirty = true;
                    RebuildRuntimeFromLevel();
                    EndEditIfAny();
                }
            }
            else if (IsCapsuleShape(ent.Shape))
            {
                float radius = ent.Radius;
                if (ImGui.DragFloat("Radius", ref radius, 0.05f, 0.01f, 1000f))
                {
                    BeginEdit();
                    ent.Radius = MathF.Max(0.01f, radius);
                    ent.Height = MathF.Max(ent.Height, ent.Radius * 2f);
                    Dirty = true;
                    RebuildRuntimeFromLevel();
                    EndEditIfAny();
                }

                float height = ent.Height;
                if (ImGui.DragFloat("Height", ref height, 0.05f, 0.01f, 1000f))
                {
                    BeginEdit();
                    ent.Height = MathF.Max(MathF.Max(0.01f, height), ent.Radius * 2f);
                    Dirty = true;
                    RebuildRuntimeFromLevel();
                    EndEditIfAny();
                }
            }
            else
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
                Vector3 baseSize = string.IsNullOrWhiteSpace(e.MeshPath) ? markerMed : (Vector3)e.Size;
                if (IsZero(baseSize))
                    baseSize = string.IsNullOrWhiteSpace(e.MeshPath) ? markerMed : Vector3.One;

                Vector3 size = Mul(baseSize, ws);
                DrawBoxes.Add(new EditorDrawBox(wt, size, new Vector4(0.8f, 0.6f, 0.2f, 1f), wr));
            }
            else if (e.Type == EntityTypes.RigidBody)
            {
                Vector4 color = e.Color;
                if (IsSphereShape(e.Shape))
                {
                    float radius = GetScaledSphereRadius(e, ws);
                    DrawBoxes.Add(EditorDrawBox.Sphere(wt, radius * 2f, color));
                }
                else if (IsCapsuleShape(e.Shape))
                {
                    float radius = GetScaledCapsuleRadius(e, ws);
                    float height = GetScaledCapsuleHeight(e, ws);
                    DrawBoxes.Add(new EditorDrawBox(
                        wt,
                        new Vector3(radius * 2f, height, radius * 2f),
                        color,
                        wr,
                        isSphere: true));
                }
                else
                {
                    Vector3 size = Mul(e.Size, ws);
                    DrawBoxes.Add(new EditorDrawBox(wt, size, color, wr));
                }
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
            Radius = 0.5f,
            Height = 1.5f,
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
                if (e.Type == EntityTypes.RigidBody && string.IsNullOrWhiteSpace(e.Shape))
                    e.Shape = "Box";

                if (IsSphereShape(e.Shape))
                {
                    if (e.Radius <= 0f)
                        e.Radius = 0.5f;
                }
                else if (IsCapsuleShape(e.Shape))
                {
                    if (e.Radius <= 0f)
                        e.Radius = 0.5f;

                    if (e.Height <= 0f)
                        e.Height = 1.5f;

                    e.Height = MathF.Max(e.Height, e.Radius * 2f);
                }
                else
                {
                    Vector3 sz = e.Size;
                    if (IsZero(sz))
                        e.Size = new Vector3(1, 1, 1);
                }
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









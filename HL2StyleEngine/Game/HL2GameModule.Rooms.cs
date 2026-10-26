using System.Diagnostics;
using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;

namespace Game;

public sealed partial class HL2GameModule
{
    private RoomVisibilityGraph? _roomVisibility;
    private long _roomRevision = -1;
    private string _roomPath = "";
    private int _roomBakeCursor;
    public bool RoomVisibilityEnabled { get; set; } = true;
    public int RoomCulledEntities { get; private set; }
    public int RoomPrefetchedBindings { get; private set; }
    public double RoomVisibilityMilliseconds { get; private set; }
    public string RoomVisibilityStatus => !RoomVisibilityEnabled ? "Room visibility disabled (reference)" : _roomVisibility?.Status ?? "Room visibility disabled";
    public RoomVisibilityGraph? VisibilityGraph => _roomVisibility;

    private void UpdateRoomVisibility(Matrix4x4 viewProjection)
    {
        long start = Stopwatch.GetTimestamp();
        RoomCulledEntities = 0; RoomPrefetchedBindings = 0;
        if (_roomRevision != _editor.Revision || _roomPath != _editor.LevelPath || _roomVisibility == null)
        {
            _roomVisibility = new(_editor.LevelFile.RoomVisibility);
            _roomRevision = _editor.Revision; _roomPath = _editor.LevelPath;
        }
        _roomVisibility.Update(_camera.Position, viewProjection);
        _roomReflections.PrefetchProbeIds = !_editorEnabled && RoomVisibilityEnabled ? _roomVisibility.PrefetchProbeIds : null;
        RoomVisibilityMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private bool RoomCanSee(Vector3 min, Vector3 max) => _editorEnabled || !RoomVisibilityEnabled || _roomVisibility?.IsVisible(min, max) != false;

    private void PrefetchRoomBakeBindings(Renderer renderer)
    {
        if (_editorEnabled || !RoomVisibilityEnabled || _roomVisibility is not { Active: true } || !_bakedLighting.Ready) return;
        // One atlas remains resident. Prepare instance bindings through the existing shared upload budget.
        for (int n = 0; n < Math.Min(32, _runtimeEntities.Count); n++)
        {
            var entity = _runtimeEntities[_roomBakeCursor++ % _runtimeEntities.Count];
            if (_roomBakeCursor >= _runtimeEntities.Count) _roomBakeCursor = 0;
            if (!entity.Render.Enabled || string.IsNullOrWhiteSpace(entity.Render.ModelAssetPath)) continue;
            RenderBounds(entity, out var min, out var max);
            if (!_roomVisibility.IsPrefetched(min,max)) continue;
            var def = FindLevelEntityDef(entity);
            if (def == null || !def.StaticLighting || def.ExcludeFromDiffuseBake) continue;
            string path = ResolveModelAssetPath(entity.Render.ModelAssetPath);
            if (!_weaponModelCache.TryGetValue(path, out var entry) || entry.Model == null || entry.LoadedModel == null) continue;
            var transform = CreateBoundsFitTransform(entry.Bounds, entity.Transform.Position, entity.Render.Size, GetColliderRotation(entity));
            if (_bakedLighting.GetBinding(def,entry.LoadedModel,transform,_editor.LevelFile,_world,renderer) != null) RoomPrefetchedBindings++;
        }
    }
}

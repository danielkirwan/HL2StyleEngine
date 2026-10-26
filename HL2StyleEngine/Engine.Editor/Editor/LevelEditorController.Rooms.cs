using System.Numerics;
using Engine.Core.Serialization;
using Engine.Editor.Level;
using Engine.Render;
using ImGuiNET;

namespace Engine.Editor.Editor;

public sealed partial class LevelEditorController
{
    public bool ShowRoomBounds;
    public void SetRoomVisibility(RoomVisibilitySettings? value)
    {
        BeginEdit();
        LevelFile.RoomVisibility = value == null ? null : StructuredData.ReadJson<RoomVisibilitySettings>(StructuredData.WriteJson(value));
        Dirty = true; EndEditIfAny();
    }

    private void DrawRoomToolbar()
    {
        if (!ImGui.TreeNode("Rooms And Doorways")) return;
        if (LevelFile.RoomVisibility == null)
        {
            if (ImGui.Button("Create Room Graph")) SetRoomVisibility(new());
            ImGui.TreePop(); return;
        }
        var settings = StructuredData.ReadJson<RoomVisibilitySettings>(StructuredData.WriteJson(LevelFile.RoomVisibility));
        settings.Rooms ??= new(); settings.Portals ??= new();
        settings.Rooms.RemoveAll(r => r == null); settings.Portals.RemoveAll(p => p == null);
        bool enabled = settings.Enabled;
        bool changed = ImGui.Checkbox("Room Visibility", ref enabled); settings.Enabled = enabled;
        ImGui.Checkbox("Show Room Bounds", ref ShowRoomBounds);
        int depth = settings.PrefetchDepth;
        ImGui.TextUnformatted("Prefetch Connections"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.SliderInt("##roomPrefetch", ref depth, 0, 4); settings.PrefetchDepth = depth;
        if (settings.Rooms.Count < 64 && ImGui.Button("Add Room"))
        {
            settings.Rooms.Add(new() { Center = SelectedEntityIndex >= 0 && SelectedEntityIndex < DrawBoxes.Count ? DrawBoxes[SelectedEntityIndex].Position : new(0,2,0) });
            changed = true;
        }
        for (int i = 0; i < settings.Rooms.Count; i++)
        {
            var room = settings.Rooms[i]; ImGui.PushID("room-"+room.Id);
            if (ImGui.TreeNode(room.Name+"##room"))
            {
                string name = room.Name ?? "Room"; Vector3 center = room.Center, size = room.Size;
                ImGui.TextUnformatted("Name"); ImGui.SetNextItemWidth(-1); changed |= ImGui.InputText("##name", ref name, 128); room.Name = name;
                changed |= VectorField("Center", ref center); room.Center = center;
                changed |= VectorField("Size", ref size); room.Size = size;
                string probe = room.ReflectionProbeId ?? "";
                changed |= Choice("Reflection Probe", ref probe, LevelFile.Entities.Where(e => e.Type == EntityTypes.ReflectionProbe).Select(e => (e.Id, e.Name ?? e.Id)));
                room.ReflectionProbeId = probe;
                if (ImGui.Button("Remove Room"))
                {
                    settings.Portals.RemoveAll(p => p.RoomA == room.Id || p.RoomB == room.Id);
                    settings.Rooms.RemoveAt(i--); changed = true;
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        if (settings.Rooms.Count >= 2 && settings.Portals.Count < 128 && ImGui.Button("Add Doorway"))
        {
            settings.Portals.Add(new() { RoomA = settings.Rooms[0].Id, RoomB = settings.Rooms[1].Id,
                Center = ((Vector3)settings.Rooms[0].Center + (Vector3)settings.Rooms[1].Center) / 2 }); changed = true;
        }
        for (int i = 0; i < settings.Portals.Count; i++)
        {
            var portal = settings.Portals[i]; ImGui.PushID("portal-"+portal.Id);
            if (ImGui.TreeNode(portal.Name+"##portal"))
            {
                string name = portal.Name ?? "Doorway", a = portal.RoomA ?? "", b = portal.RoomB ?? ""; Vector3 center = portal.Center, size = portal.Size; int axis = portal.Axis;
                ImGui.TextUnformatted("Name"); ImGui.SetNextItemWidth(-1); changed |= ImGui.InputText("##name", ref name, 128); portal.Name = name;
                changed |= Choice("Room A", ref a, settings.Rooms.Select(r => (r.Id, r.Name))); portal.RoomA = a;
                changed |= Choice("Room B", ref b, settings.Rooms.Select(r => (r.Id, r.Name))); portal.RoomB = b;
                changed |= VectorField("Center", ref center); portal.Center = center;
                changed |= VectorField("Aperture Size", ref size); portal.Size = size;
                ImGui.TextUnformatted("Normal Axis"); ImGui.SetNextItemWidth(-1); changed |= ImGui.Combo("##axis", ref axis, "X\0Y\0Z\0"); portal.Axis = axis;
                if (ImGui.Button("Remove Doorway")) { settings.Portals.RemoveAt(i--); changed = true; }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        if (changed) SetRoomVisibility(settings);
        string error = new RoomVisibilityGraph(settings).Error;
        if (error.Length > 0) ImGui.TextWrapped(error);
        ImGui.TreePop();

        static bool VectorField(string label, ref Vector3 value)
        { ImGui.TextUnformatted(label); ImGui.SetNextItemWidth(-1); return ImGui.DragFloat3("##"+label, ref value, .05f); }
        static bool Choice(string label, ref string value, IEnumerable<(string Id, string Name)> candidates)
        {
            var choices = candidates.ToArray(); string current = value;
            bool edit = false;
            ImGui.TextUnformatted(label); ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##"+label, choices.FirstOrDefault(c => c.Id == current).Name ?? (current.Length == 0 ? "None" : "Missing")))
            {
                if (ImGui.Selectable("None", value.Length == 0)) { value = ""; edit = true; }
                foreach (var c in choices) if (ImGui.Selectable(c.Name+"##"+c.Id, c.Id == value)) { value = c.Id; edit = true; }
                ImGui.EndCombo();
            }
            return edit;
        }
    }

    private void DrawRoomBounds(BasicWorldRenderer world, Renderer renderer)
    {
        if (!ShowRoomBounds || LevelFile.RoomVisibility is not { } settings) return;
        foreach (var room in settings.Rooms ?? [])
            if (room != null) Draw(room.Center, room.Size, new(.15f,.8f,1,1));
        foreach (var portal in settings.Portals ?? [])
        {
            if (portal == null) continue;
            Vector3 size = portal.Size;
            if (portal.Axis == 0) size.X = .03f; else if (portal.Axis == 1) size.Y = .03f; else size.Z = .03f;
            Draw(portal.Center, size, new(.2f,1,.35f,1));
        }
        void Draw(Vector3 center, Vector3 size, Vector4 color)
        {
            if (!float.IsFinite(center.LengthSquared()+size.LengthSquared())) return;
            for (int axis=0; axis<3; axis++) for (int a=-1; a<=1; a+=2) for (int b=-1; b<=1; b+=2)
            {
                Vector3 line = new(.015f), offset;
                if (axis==0) { line.X=size.X; offset=new(0,a*size.Y/2,b*size.Z/2); }
                else if (axis==1) { line.Y=size.Y; offset=new(a*size.X/2,0,b*size.Z/2); }
                else { line.Z=size.Z; offset=new(a*size.X/2,b*size.Y/2,0); }
                world.DrawBoundsOverlayBox(renderer.CommandList, Matrix4x4.CreateScale(line)*Matrix4x4.CreateTranslation(center+offset), color);
            }
        }
    }
}

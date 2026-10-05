using System.Numerics;
using Engine.Render;
using ImGuiNET;

namespace Engine.UI;

internal static class InventoryPreviewRenderer
{
    public static int Draw(GameplayUiState state, ImGuiViewportPtr viewport, Func<string, UiImage>? icons)
    {
        var layout = new InventoryLayout((int)viewport.Size.X, (int)viewport.Size.Y,
            state.GridWidth, state.GridHeight, state.PrimaryGridHeight);
        int hovered = -1;
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.SetNextWindowBgAlpha(0.62f);
        ImGui.Begin("InventoryGridOverlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
        var draw = ImGui.GetWindowDrawList();
        uint grey = 0xff9c9c9c, white = 0xffeeece8, gold = 0xff70cce8;
        draw.AddText(viewport.Pos + layout.PrimaryOrigin - new Vector2(0, 28), white, "INVENTORY");
        if (layout.OverflowRows > 0)
            draw.AddText(viewport.Pos + layout.OverflowOrigin - new Vector2(0, 28), grey, "OVERFLOW");

        for (int slot = 0; slot < state.GridWidth * state.GridHeight; slot++)
        {
            Vector2 min = viewport.Pos + layout.SlotOrigin(slot), max = min + new Vector2(layout.Cell);
            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"inventoryCell{slot}", new Vector2(layout.Cell));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)) hovered = slot;
            draw.AddRectFilled(min, max, 0x65141414);
            draw.AddRect(min, max, 0x888b8b8b);
        }

        GameplayUiInventoryItem? selected = null;
        foreach (var item in state.InventoryItems)
        {
            Vector2 min = viewport.Pos + layout.SlotOrigin(item.SlotIndex);
            Vector2 size = new(item.SlotWidth * layout.Cell, item.SlotHeight * layout.Cell);
            Vector2 max = min + size;
            bool focused = item.SlotIndex == state.SelectedSlot || item.CoveredSlots.Contains(state.SelectedSlot);
            if (focused) selected = item;
            uint border = item.IsValidCombineTarget ? 0xff82cf8d : item.IsCombineSource ? gold : focused ? white : 0xff777777;
            draw.AddRectFilled(min + Vector2.One, max - Vector2.One, 0xcc1d1d1d);
            draw.AddRect(min + Vector2.One, max - Vector2.One, border, 0, ImDrawFlags.None, focused ? 2 : 1);
            UiImage image = string.IsNullOrWhiteSpace(item.IconPath) ? default : icons?.Invoke(item.IconPath) ?? default;
            if (image.Id != IntPtr.Zero)
            {
                Vector2 available = size - new Vector2(10);
                float imageWidth = item.Rotated ? image.Height : image.Width;
                float imageHeight = item.Rotated ? image.Width : image.Height;
                float scale = Math.Min(available.X / imageWidth, available.Y / imageHeight);
                Vector2 imageSize = new(imageWidth * scale, imageHeight * scale);
                Vector2 a = min + (size - imageSize) / 2, b = a + imageSize;
                if (item.Rotated)
                    draw.AddImageQuad(image.Id, a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y),
                        new Vector2(0, 1), Vector2.Zero, new Vector2(1, 0), Vector2.One);
                else draw.AddImage(image.Id, a, b);
            }
            else
            {
                draw.PushClipRect(min + new Vector2(4), max - new Vector2(4), true);
                draw.AddText(min + new Vector2(6, 8), grey, item.DisplayName);
                draw.PopClipRect();
            }
            if (item.MaxStack > 1)
            {
                string count = item.Count.ToString();
                Vector2 at = max - ImGui.CalcTextSize(count) - new Vector2(5);
                draw.AddText(at + Vector2.One, 0xff000000, count);
                draw.AddText(at, gold, count);
            }
        }
        if (state.MovingInventoryItem && state.MovingTargetSlot >= 0)
        {
            Vector2 min = viewport.Pos + layout.SlotOrigin(state.MovingTargetSlot);
            draw.AddRect(min, min + new Vector2(state.MovingItemSlotWidth, state.MovingItemSlotHeight) * layout.Cell,
                state.CanPlaceMovingItem ? 0xff82cf8d : 0xff6464ef, 0, ImDrawFlags.None, 2);
        }
        ImGui.SetCursorScreenPos(viewport.Pos + layout.DescriptionOrigin);
        ImGui.BeginChild("InventoryDetails", new Vector2(layout.DescriptionWidth, 115));
        if (selected != null)
        {
            ImGui.TextUnformatted(selected.DisplayName);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.67f, .67f, .67f, 1));
            ImGui.TextWrapped(selected.Description);
            ImGui.PopStyleColor();
        }
        if (state.CombiningInventoryItem && !string.IsNullOrWhiteSpace(state.CombinePreviewResultName))
            ImGui.TextColored(new Vector4(.55f, .82f, .57f, 1), state.CombinePreviewResultName);
        ImGui.EndChild();
        ImGui.End();
        ImGui.PopStyleVar(2);
        return hovered;
    }
}

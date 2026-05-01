using ImGuiNET;
using System.Numerics;

namespace Engine.UI;

internal sealed class GameplayUiImGuiPreviewRenderer
{
    public bool Draw(GameplayUiState state, out int selectedSlot)
    {
        selectedSlot = -1;

        if (!state.InventoryOpen &&
            !state.ItemCollectedOpen &&
            string.IsNullOrWhiteSpace(state.InteractionPrompt) &&
            string.IsNullOrWhiteSpace(state.GameMessage))
        {
            return false;
        }

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();

        DrawPromptPanel(state, viewport);
        DrawCollectedItemPanel(state, viewport);

        if (state.InventoryOpen)
            selectedSlot = DrawInventoryPanel(state, viewport);

        return true;
    }

    private static void DrawPromptPanel(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        if (string.IsNullOrWhiteSpace(state.InteractionPrompt) && string.IsNullOrWhiteSpace(state.GameMessage))
            return;

        Vector2 centerBottom = new(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y - 96f);
        ImGui.SetNextWindowPos(centerBottom, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowBgAlpha(0.40f);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.02f, 0.025f, 0.025f, 0.84f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.62f, 0.66f, 0.60f, 0.45f));
        ImGui.Begin("RmlUiPreviewPrompt", flags);
        if (!string.IsNullOrWhiteSpace(state.InteractionPrompt))
            ImGui.TextColored(new Vector4(0.94f, 0.93f, 0.86f, 1f), $"E / X: {state.InteractionPrompt}");
        if (!string.IsNullOrWhiteSpace(state.GameMessage))
            ImGui.TextColored(new Vector4(0.72f, 0.74f, 0.68f, 1f), state.GameMessage);
        ImGui.End();
        ImGui.PopStyleColor(2);
    }

    private static void DrawCollectedItemPanel(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        if (!state.ItemCollectedOpen || state.CollectedItem == null)
            return;

        GameplayUiCollectedItem item = state.CollectedItem;
        Vector2 center = new(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(460f, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.02f, 0.025f, 0.028f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.74f, 0.74f, 0.66f, 0.50f));
        ImGui.Begin("RmlUiPreviewCollectedItem", flags);
        ImGui.TextColored(new Vector4(0.56f, 0.58f, 0.53f, 1f), "ITEM COLLECTED");
        ImGui.Spacing();
        string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
        ImGui.TextColored(new Vector4(0.93f, 0.92f, 0.84f, 1f), $"{item.DisplayName}{countSuffix}");
        ImGui.TextColored(new Vector4(0.58f, 0.60f, 0.56f, 1f), $"{item.Type} | {item.SlotWidth}x{item.SlotHeight} slots | Stack {item.MaxStack}");
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(item.Description);
        }

        ImGui.Separator();
        ImGui.TextDisabled("E / X: Confirm");
        ImGui.End();
        ImGui.PopStyleColor(2);
    }

    private static int DrawInventoryPanel(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        int selectedSlot = -1;

        ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X + 56f, viewport.Pos.Y + 54f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(980f, 610f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings;

        Dictionary<int, GameplayUiInventoryItem> byOriginSlot = state.InventoryItems.ToDictionary(item => item.SlotIndex);
        Dictionary<int, GameplayUiInventoryItem> byCoveredSlot = BuildCoveredSlotLookup(state);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.018f, 0.022f, 0.024f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.66f, 0.68f, 0.61f, 0.36f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(34f, 30f));
        ImGui.Begin("RmlUiPreviewInventory", flags);

        ImGui.TextColored(new Vector4(0.56f, 0.58f, 0.53f, 1f), "ITEM BOX");
        ImGui.TextColored(new Vector4(0.91f, 0.90f, 0.82f, 1f), "Inventory");
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 165f);
        ImGui.TextDisabled($"Slots: {state.UsedSlotCount}/{Math.Max(1, state.GridWidth * state.GridHeight)}");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Vector2 gridStart = ImGui.GetCursorScreenPos();
        const float slotSize = 66f;
        const float gap = 8f;
        int capacity = Math.Max(1, state.GridWidth * state.GridHeight);

        for (int slot = 0; slot < capacity; slot++)
        {
            int col = slot % state.GridWidth;
            int row = slot / state.GridWidth;
            Vector2 slotPos = gridStart + new Vector2(col * (slotSize + gap), row * (slotSize + gap));
            ImGui.SetCursorScreenPos(slotPos);

            bool hasItemOrigin = byOriginSlot.TryGetValue(slot, out GameplayUiInventoryItem? item);
            bool hasCoveredItem = byCoveredSlot.TryGetValue(slot, out GameplayUiInventoryItem? coveredItem);
            bool selected = slot == state.SelectedSlot;
            bool movingSource = state.MovingInventoryItem && slot == state.MovingFromSlot;
            bool movingTarget = state.MovingInventoryItem && slot == state.MovingTargetSlot;
            bool occupiedByFootprint = hasCoveredItem && !hasItemOrigin;
            Vector4 fill = hasCoveredItem
                ? new Vector4(0.11f, 0.14f, 0.14f, 0.94f)
                : new Vector4(0.055f, 0.065f, 0.070f, 0.88f);
            if (occupiedByFootprint)
                fill = new Vector4(0.085f, 0.11f, 0.11f, 0.94f);
            if (selected)
                fill = new Vector4(0.29f, 0.34f, 0.33f, 0.98f);
            if (movingSource)
                fill = new Vector4(0.23f, 0.22f, 0.13f, 0.98f);
            if (movingTarget)
                fill = state.CanPlaceMovingItem
                    ? new Vector4(0.16f, 0.35f, 0.25f, 0.98f)
                    : new Vector4(0.42f, 0.16f, 0.15f, 0.98f);

            ImGui.PushStyleColor(ImGuiCol.Button, fill);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.28f, 0.27f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.36f, 0.40f, 0.37f, 1f));
            string label = hasItemOrigin
                ? $"{ShortLabel(item!.DisplayName)}{(item.Count > 1 ? $" x{item.Count}" : "")}\n{item.SlotWidth}x{item.SlotHeight}##previewSlot{slot}"
                : occupiedByFootprint
                    ? $"({ShortLabel(coveredItem!.DisplayName)})##previewSlot{slot}"
                : $"##previewSlot{slot}";

            bool clicked = ImGui.Button(label, new Vector2(slotSize, slotSize));
            if (clicked && (hasCoveredItem || state.MovingInventoryItem))
                selectedSlot = slot;

            if ((hasCoveredItem || state.MovingInventoryItem) && ImGui.IsItemHovered())
                selectedSlot = slot;

            ImGui.PopStyleColor(3);
        }

        DrawFootprintOverlays(state, gridStart, slotSize, gap);
        DrawDescriptionPanel(state, byCoveredSlot);

        ImGui.End();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);

        return selectedSlot;
    }

    private static Dictionary<int, GameplayUiInventoryItem> BuildCoveredSlotLookup(GameplayUiState state)
    {
        Dictionary<int, GameplayUiInventoryItem> lookup = new();
        foreach (GameplayUiInventoryItem item in state.InventoryItems)
        {
            if (item.CoveredSlots.Count == 0)
            {
                lookup[item.SlotIndex] = item;
                continue;
            }

            foreach (int slot in item.CoveredSlots)
                lookup[slot] = item;
        }

        return lookup;
    }

    private static void DrawFootprintOverlays(GameplayUiState state, Vector2 gridStart, float slotSize, float gap)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        foreach (GameplayUiInventoryItem item in state.InventoryItems)
        {
            if (item.SlotWidth <= 1 && item.SlotHeight <= 1)
                continue;

            int col = item.SlotIndex % state.GridWidth;
            int row = item.SlotIndex / state.GridWidth;
            Vector2 min = gridStart + new Vector2(col * (slotSize + gap), row * (slotSize + gap));
            Vector2 max = min + new Vector2(
                item.SlotWidth * slotSize + Math.Max(0, item.SlotWidth - 1) * gap,
                item.SlotHeight * slotSize + Math.Max(0, item.SlotHeight - 1) * gap);

            uint fill = ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.23f, 0.22f, 0.34f));
            uint border = ImGui.ColorConvertFloat4ToU32(new Vector4(0.70f, 0.72f, 0.65f, 0.78f));
            drawList.AddRectFilled(min, max, fill, 4f);
            drawList.AddRect(min, max, border, 4f, ImDrawFlags.None, 2f);
        }
    }

    private static void DrawDescriptionPanel(GameplayUiState state, Dictionary<int, GameplayUiInventoryItem> bySlot)
    {
        ImGui.SetCursorPos(new Vector2(690f, 148f));
        ImGui.BeginChild("RmlUiPreviewDescription", new Vector2(250f, 320f));

        if (state.SelectedSlot >= 0 && bySlot.TryGetValue(state.SelectedSlot, out GameplayUiInventoryItem? item))
        {
            string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
            ImGui.TextColored(new Vector4(0.90f, 0.89f, 0.82f, 1f), $"{item.DisplayName}{countSuffix}");
            ImGui.TextDisabled($"{item.Type} | {item.SlotWidth}x{item.SlotHeight} slots | Stack {item.MaxStack}");
            ImGui.Spacing();
            if (!string.IsNullOrWhiteSpace(item.Description))
                ImGui.TextWrapped(item.Description);
        }
        else
        {
            ImGui.TextColored(new Vector4(0.90f, 0.89f, 0.82f, 1f), "No item selected");
            ImGui.TextDisabled("Move across the case with WASD, D-pad, or left stick.");
        }

        ImGui.Spacing();
        if (state.SaveCount > 0)
            ImGui.TextDisabled($"Saves used: {state.SaveCount}");
        if (state.MovingInventoryItem)
        {
            ImGui.Spacing();
            string moveHint = state.CanSwapMovingItem
                ? "Release here to swap items."
                : state.CanPlaceMovingItem
                    ? "Move target is valid."
                    : "That item will not fit there.";
            ImGui.TextColored(
                state.CanPlaceMovingItem ? new Vector4(0.48f, 0.78f, 0.55f, 1f) : new Vector4(0.92f, 0.42f, 0.36f, 1f),
                moveHint);
            ImGui.TextDisabled("E / X: Place");
            ImGui.TextDisabled("I / Back: Cancel move");
        }
        else
        {
            ImGui.TextDisabled("E / X: Move item");
            ImGui.TextDisabled("I / Back: Close");
        }

        ImGui.EndChild();
    }

    private static string ShortLabel(string text)
        => text.Length <= 10 ? text : text[..10];
}

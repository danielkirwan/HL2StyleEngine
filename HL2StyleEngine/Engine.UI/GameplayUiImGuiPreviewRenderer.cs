using ImGuiNET;
using System.Numerics;

namespace Engine.UI;

internal sealed class GameplayUiImGuiPreviewRenderer
{
    public bool Draw(GameplayUiState state, out int selectedSlot)
    {
        selectedSlot = -1;

        bool hasVisibleUi =
            state.HudVisible ||
            state.AmmoHudVisible ||
            state.WeaponSelectorVisible ||
            state.LoadingOverlayVisible ||
            state.CrosshairVisible ||
            state.InventoryOpen ||
            state.StorageOpen ||
            state.ItemCollectedOpen ||
            state.SaveSlotPanelOpen ||
            state.PauseMenuOpen ||
            state.LoadSlotPanelOpen ||
            !string.IsNullOrWhiteSpace(state.InteractionPrompt) ||
            !string.IsNullOrWhiteSpace(state.GameMessage);

        if (!hasVisibleUi)
            return false;

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();

        DrawCrosshair(state, viewport);
        DrawGameplayHud(state, viewport);
        DrawWeaponSelector(state, viewport);
        DrawPromptPanel(state, viewport);
        DrawCollectedItemPanel(state, viewport);

        if (state.SaveSlotPanelOpen)
            DrawSaveSlotPanel(state, viewport);
        if (state.LoadSlotPanelOpen)
            DrawLoadSlotPanel(state, viewport);
        if (state.PauseMenuOpen)
            DrawPauseMenu(state, viewport);

        if (state.InventoryOpen)
            selectedSlot = state.UsingInventoryItem
                ? DrawUseItemPanel(state, viewport)
                : DrawInventoryPanel(state, viewport);

        DrawLoadingOverlay(state, viewport);
        return true;
    }


    private static void DrawCrosshair(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        if (!state.CrosshairVisible)
            return;

        ImDrawListPtr drawList = ImGui.GetForegroundDrawList();
        Vector2 origin = viewport.Pos + new Vector2(state.CrosshairLeft, state.CrosshairTop);
        uint color = ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.94f, 0.90f, 1f));

        drawList.AddRectFilled(origin + new Vector2(0f, 17f), origin + new Vector2(10f, 19f), color);
        drawList.AddRectFilled(origin + new Vector2(26f, 17f), origin + new Vector2(36f, 19f), color);
        drawList.AddRectFilled(origin + new Vector2(17f, 0f), origin + new Vector2(19f, 10f), color);
        drawList.AddRectFilled(origin + new Vector2(17f, 26f), origin + new Vector2(19f, 36f), color);
        drawList.AddRectFilled(origin + new Vector2(16f, 16f), origin + new Vector2(20f, 20f), color);
    }

    private static void DrawGameplayHud(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        if (!state.HudVisible)
            return;

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground;

        Vector4 panelFill = new(0.06f, 0.05f, 0.00f, 0.54f);
        Vector4 panelBorder = new(0.88f, 0.75f, 0.00f, 0.48f);
        Vector4 yellow = new(0.96f, 0.88f, 0.00f, 1f);
        Vector4 mutedYellow = new(0.78f, 0.72f, 0.00f, 0.86f);

        Vector2 statusPos = new(viewport.Pos.X + 38f, viewport.Pos.Y + viewport.Size.Y - 78f);
        ImGui.SetNextWindowPos(statusPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(300f, 58f), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("GameplayHudLeft", flags);

        ImDrawListPtr statusDrawList = ImGui.GetWindowDrawList();
        DrawHudValuePanel(statusDrawList, ImGui.GetWindowPos(), new Vector2(134f, 54f), "HEALTH", Math.Clamp(state.Health, 0, 999).ToString(), panelFill, panelBorder, mutedYellow, yellow);
        DrawHudValuePanel(statusDrawList, ImGui.GetWindowPos() + new Vector2(154f, 0f), new Vector2(134f, 54f), "SUIT", Math.Clamp(state.Suit, 0, 999).ToString(), panelFill, panelBorder, mutedYellow, yellow);
        ImGui.End();
        ImGui.PopStyleVar();

        if (!state.AmmoHudVisible)
            return;

        float ammoWidth = 248f;
        float ammoX = viewport.Pos.X + viewport.Size.X - ammoWidth - 42f;
        float ammoY = viewport.Pos.Y + viewport.Size.Y - 78f;
        if (ammoX < statusPos.X + 324f)
            ammoY -= 66f;

        ImGui.SetNextWindowPos(new Vector2(ammoX, ammoY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(ammoWidth, 58f), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("GameplayHudAmmo", flags);

        ImDrawListPtr ammoDrawList = ImGui.GetWindowDrawList();
        Vector2 ammoMin = ImGui.GetWindowPos();
        Vector2 ammoSize = new(ammoWidth - 8f, 54f);
        Vector2 ammoBorderMin = ammoMin + Vector2.One;
        Vector2 ammoBorderMax = ammoMin + ammoSize - Vector2.One;
        ammoDrawList.AddRectFilled(ammoBorderMin, ammoBorderMax, ImGui.ColorConvertFloat4ToU32(panelFill));
        ammoDrawList.AddRect(ammoBorderMin, ammoBorderMax, ImGui.ColorConvertFloat4ToU32(panelBorder));
        ammoDrawList.AddText(ammoMin + new Vector2(14f, 31f), ImGui.ColorConvertFloat4ToU32(mutedYellow), "AMMO");

        ImGui.SetCursorScreenPos(ammoMin + new Vector2(96f, 4f));
        ImGui.SetWindowFontScale(2.15f);
        ImGui.TextColored(yellow, Math.Clamp(state.CurrentMagazineAmmo, 0, 999).ToString());
        ImGui.SetWindowFontScale(1.55f);
        ImGui.SetCursorScreenPos(ammoMin + new Vector2(188f, 12f));
        ImGui.TextColored(yellow, Math.Clamp(state.ReserveAmmo, 0, 999).ToString());
        ImGui.SetWindowFontScale(1f);
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static void DrawHudValuePanel(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 size,
        string label,
        string value,
        Vector4 fill,
        Vector4 border,
        Vector4 labelColor,
        Vector4 valueColor)
    {
        Vector2 borderMin = min + Vector2.One;
        Vector2 borderMax = min + size - Vector2.One;
        drawList.AddRectFilled(borderMin, borderMax, ImGui.ColorConvertFloat4ToU32(fill));
        drawList.AddRect(borderMin, borderMax, ImGui.ColorConvertFloat4ToU32(border));
        drawList.AddText(min + new Vector2(11f, 31f), ImGui.ColorConvertFloat4ToU32(labelColor), label);

        ImGui.SetCursorScreenPos(min + new Vector2(65f, 4f));
        ImGui.SetWindowFontScale(2.05f);
        ImGui.TextColored(valueColor, value);
        ImGui.SetWindowFontScale(1f);
    }

    private static void DrawWeaponSelector(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        if (!state.WeaponSelectorVisible || state.WeaponCategories.Count == 0)
            return;

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground;

        Vector2 center = new(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        float panelWidth = Math.Clamp(viewport.Size.X * 0.45f, 172f, 224f);
        float itemWidth = panelWidth - 16f;
        float verticalDistance = Math.Clamp(viewport.Size.Y * 0.18f, 92f, 136f);
        float horizontalDistance = Math.Clamp(viewport.Size.X * 0.26f, 112f, 250f);

        foreach (GameplayUiWeaponCategory category in state.WeaponCategories)
        {
            int weaponCount = Math.Max(1, category.Weapons.Count);
            float windowHeight = 14f + weaponCount * 39f;
            Vector2 pos = category.Slot switch
            {
                1 => new Vector2(center.X - panelWidth * 0.5f, center.Y - verticalDistance - windowHeight * 0.5f),
                2 => new Vector2(center.X + horizontalDistance, center.Y - windowHeight * 0.5f),
                3 => new Vector2(center.X - panelWidth * 0.5f, center.Y + verticalDistance - windowHeight * 0.5f),
                4 => new Vector2(center.X - horizontalDistance - panelWidth, center.Y - windowHeight * 0.5f),
                _ => new Vector2(center.X - panelWidth * 0.5f, center.Y - windowHeight * 0.5f)
            };

            pos.X = Math.Clamp(pos.X, viewport.Pos.X + 10f, viewport.Pos.X + viewport.Size.X - panelWidth - 10f);
            pos.Y = Math.Clamp(pos.Y, viewport.Pos.Y + 10f, viewport.Pos.Y + viewport.Size.Y - windowHeight - 10f);

            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(panelWidth, windowHeight), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 7f));
            ImGui.Begin($"WeaponSelector{category.Slot}", flags);

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            foreach (GameplayUiWeaponItem weapon in category.Weapons)
            {
                Vector2 itemMin = ImGui.GetCursorScreenPos();
                Vector2 itemSize = new(itemWidth, 34f);
                Vector2 itemMax = itemMin + itemSize;
                bool empty = weapon.UsesAmmo && !weapon.HasAmmo;
                Vector4 fill = weapon.Selected
                    ? new Vector4(0.43f, 0.35f, 0.00f, 0.74f)
                    : new Vector4(0.06f, 0.05f, 0.00f, 0.54f);
                Vector4 itemBorder = weapon.Selected
                    ? new Vector4(1.00f, 0.86f, 0.00f, 0.90f)
                    : new Vector4(0.88f, 0.75f, 0.00f, 0.48f);
                Vector4 textColor = empty
                    ? new Vector4(0.94f, 0.18f, 0.12f, 1f)
                    : weapon.Selected
                        ? new Vector4(1.0f, 0.94f, 0.0f, 1f)
                        : new Vector4(0.78f, 0.72f, 0.0f, 0.86f);

                if (empty)
                {
                    fill = new Vector4(0.18f, 0.03f, 0.02f, 0.66f);
                    itemBorder = new Vector4(0.90f, 0.14f, 0.10f, 0.78f);
                }

                string label = weapon.DisplayName.ToUpperInvariant();
                Vector2 textSize = ImGui.CalcTextSize(label);
                float textX = itemMin.X + MathF.Max(8f, (itemSize.X - textSize.X) * 0.5f);
                float textY = itemMin.Y + MathF.Max(3f, (itemSize.Y - textSize.Y) * 0.5f);

                drawList.AddRectFilled(itemMin, itemMax, ImGui.ColorConvertFloat4ToU32(fill));
                drawList.AddRect(itemMin, itemMax, ImGui.ColorConvertFloat4ToU32(itemBorder));
                drawList.AddText(new Vector2(textX, textY), ImGui.ColorConvertFloat4ToU32(textColor), label);
                ImGui.Dummy(itemSize + new Vector2(0f, 5f));
            }

            ImGui.End();
            ImGui.PopStyleVar(3);
        }
    }

    private static void DrawLoadingOverlay(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        if (!state.LoadingOverlayVisible)
            return;

        ImGuiWindowFlags overlayFlags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoInputs;

        ImGui.SetNextWindowPos(viewport.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(viewport.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.58f);
        ImGui.Begin("LoadingOverlayDim", overlayFlags);
        ImGui.End();

        Vector2 center = new(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.42f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(380f, 74f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);
        ImGui.Begin("LoadingOverlayPanel", overlayFlags);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), "LOADING...");
        ImGui.SameLine(350f);
        ImGui.TextColored(new Vector4(0.86f, 0.86f, 0.86f, 1f), "x");
        ImGui.ProgressBar(Math.Clamp(state.LoadingProgress, 0f, 1f), new Vector2(338f, 18f), "");
        ImGui.End();
    }
    private static void DrawSaveSlotPanel(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        Vector2 center = new(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(560f, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.96f);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.018f, 0.022f, 0.026f, 0.97f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.66f, 0.68f, 0.61f, 0.48f));
        ImGui.Begin("RmlUiPreviewSaveSlots", flags);
        ImGui.TextColored(new Vector4(0.92f, 0.91f, 0.84f, 1f), "Save Game");
        ImGui.TextDisabled("Choose a typewriter slot");
        ImGui.Separator();

        IReadOnlyList<GameplayUiSaveSlot> slots = state.SaveSlots.Count > 0
            ? state.SaveSlots
            : new[] { new GameplayUiSaveSlot { SlotIndex = 0, Label = "Slot 1", IsEmpty = true } };

        for (int i = 0; i < slots.Count; i++)
        {
            GameplayUiSaveSlot slot = slots[i];
            bool selected = slot.SlotIndex == state.SelectedSaveSlotIndex;
            Vector4 fill = selected
                ? new Vector4(0.24f, 0.30f, 0.30f, 1f)
                : new Vector4(0.10f, 0.12f, 0.13f, 0.95f);

            ImGui.PushStyleColor(ImGuiCol.Button, fill);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.31f, 0.31f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.34f, 0.38f, 0.36f, 1f));
            ImGui.Button($"##saveSlot{i}", new Vector2(520f, 76f));

            Vector2 min = ImGui.GetItemRectMin();
            Vector2 max = ImGui.GetItemRectMax();
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            uint titleColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.94f, 0.93f, 0.86f, 1f));
            uint mutedColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.62f, 0.65f, 0.61f, 1f));
            uint accentColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.78f, 0.75f, 0.58f, 1f));
            drawList.AddText(min + new Vector2(16f, 10f), titleColor, slot.Label);
            if (slot.IsEmpty)
            {
                drawList.AddText(min + new Vector2(16f, 34f), mutedColor, "Empty slot");
            }
            else
            {
                drawList.AddText(min + new Vector2(16f, 34f), mutedColor, $"{slot.AreaName} | Saves {slot.SaveCount}");
                drawList.AddText(min + new Vector2(16f, 54f), mutedColor, $"Play time {slot.PlayTime} | {slot.SavePointName}");
                Vector2 timeSize = ImGui.CalcTextSize(slot.SavedAt);
                drawList.AddText(new Vector2(max.X - timeSize.X - 16f, min.Y + 10f), accentColor, slot.SavedAt);
                string cargo = $"Inventory {slot.InventoryCount} / Storage {slot.StorageCount}";
                Vector2 cargoSize = ImGui.CalcTextSize(cargo);
                drawList.AddText(new Vector2(max.X - cargoSize.X - 16f, min.Y + 54f), mutedColor, cargo);
            }

            ImGui.PopStyleColor(3);
        }

        ImGui.Separator();
        if (state.SaveOverwriteConfirmOpen)
            ImGui.TextColored(new Vector4(0.92f, 0.78f, 0.54f, 1f), "Overwrite this slot? Press E / X again to confirm, or I / Back to cancel.");
        else
            ImGui.TextDisabled("W/S or D-pad: Select | E / X: Save | I / Back: Cancel");
        ImGui.End();
        ImGui.PopStyleColor(2);
    }

    private static void DrawPauseMenu(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        Vector2 center = new(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(360f, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.96f);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.018f, 0.022f, 0.026f, 0.97f));
        ImGui.Begin("RmlUiPreviewPauseMenu", flags);
        ImGui.TextColored(new Vector4(0.92f, 0.91f, 0.84f, 1f), "Paused");
        ImGui.TextDisabled("Interaction test");
        ImGui.Separator();
        DrawPauseOption("Resume", state.SelectedPauseMenuIndex == 0);
        DrawPauseOption("Load Save", state.SelectedPauseMenuIndex == 1);
        ImGui.Separator();
        ImGui.TextDisabled("W/S or D-pad: Select | E / X: Confirm | Esc / I / Back: Resume");
        ImGui.End();
        ImGui.PopStyleColor();
    }

    private static void DrawPauseOption(string label, bool selected)
    {
        Vector4 fill = selected
            ? new Vector4(0.24f, 0.30f, 0.30f, 1f)
            : new Vector4(0.10f, 0.12f, 0.13f, 0.95f);
        ImGui.PushStyleColor(ImGuiCol.Button, fill);
        ImGui.Button(label, new Vector2(320f, 44f));
        ImGui.PopStyleColor();
    }

    private static void DrawLoadSlotPanel(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        Vector2 center = new(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(560f, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.96f);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.018f, 0.022f, 0.026f, 0.97f));
        ImGui.Begin("RmlUiPreviewLoadSlots", flags);
        ImGui.TextColored(new Vector4(0.92f, 0.91f, 0.84f, 1f), "Load Game");
        ImGui.TextDisabled("Choose saved progress");
        ImGui.Separator();

        IReadOnlyList<GameplayUiSaveSlot> slots = state.SaveSlots.Count > 0
            ? state.SaveSlots
            : new[] { new GameplayUiSaveSlot { SlotIndex = 0, Label = "Slot 1", IsEmpty = true } };

        for (int i = 0; i < slots.Count; i++)
        {
            GameplayUiSaveSlot slot = slots[i];
            bool selected = slot.SlotIndex == state.SelectedLoadSlotIndex;
            Vector4 fill = selected
                ? new Vector4(0.24f, 0.30f, 0.30f, 1f)
                : new Vector4(0.10f, 0.12f, 0.13f, 0.95f);

            ImGui.PushStyleColor(ImGuiCol.Button, fill);
            ImGui.Button($"##loadSlot{i}", new Vector2(520f, 76f));

            Vector2 min = ImGui.GetItemRectMin();
            Vector2 max = ImGui.GetItemRectMax();
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            uint titleColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.94f, 0.93f, 0.86f, 1f));
            uint mutedColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.62f, 0.65f, 0.61f, 1f));
            uint accentColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.78f, 0.75f, 0.58f, 1f));
            drawList.AddText(min + new Vector2(16f, 10f), titleColor, slot.Label);
            if (slot.IsEmpty)
            {
                drawList.AddText(min + new Vector2(16f, 34f), mutedColor, "Empty slot");
            }
            else
            {
                drawList.AddText(min + new Vector2(16f, 34f), mutedColor, $"{slot.AreaName} | Saves {slot.SaveCount}");
                drawList.AddText(min + new Vector2(16f, 54f), mutedColor, $"Play time {slot.PlayTime} | {slot.SavePointName}");
                Vector2 timeSize = ImGui.CalcTextSize(slot.SavedAt);
                drawList.AddText(new Vector2(max.X - timeSize.X - 16f, min.Y + 10f), accentColor, slot.SavedAt);
                string cargo = $"Inventory {slot.InventoryCount} / Storage {slot.StorageCount}";
                Vector2 cargoSize = ImGui.CalcTextSize(cargo);
                drawList.AddText(new Vector2(max.X - cargoSize.X - 16f, min.Y + 54f), mutedColor, cargo);
            }

            ImGui.PopStyleColor();
        }

        ImGui.Separator();
        ImGui.TextDisabled("W/S or D-pad: Select | E / X: Load | Esc / I / Back: Back");
        ImGui.End();
        ImGui.PopStyleColor();
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
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28f, 26f));
        ImGui.Begin("RmlUiPreviewCollectedItem", flags);

        ImGui.BeginChild("RmlUiPreviewCollectedShowcase", new Vector2(155f, 205f), ImGuiChildFlags.Borders);
        ImGui.SetCursorPosY(72f);
        ImGui.SetCursorPosX(42f);
        ImGui.TextColored(GetItemToneColor(item.Type), GetItemSymbol(item.Id, item.Type));
        ImGui.SetCursorPosY(150f);
        ImGui.Separator();
        ImGui.EndChild();
        ImGui.SameLine();

        ImGui.BeginGroup();
        ImGui.TextColored(new Vector4(0.56f, 0.58f, 0.53f, 1f), item.Title.ToUpperInvariant());
        string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
        ImGui.TextColored(new Vector4(0.93f, 0.92f, 0.84f, 1f), $"{item.DisplayName}{countSuffix}");
        ImGui.TextColored(new Vector4(0.58f, 0.60f, 0.56f, 1f), $"{item.Type} | {item.SlotWidth}x{item.SlotHeight} slots | Stack {item.MaxStack}");
        ImGui.TextDisabled($"Case footprint: {item.SlotWidth} x {item.SlotHeight}");
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(item.Description);
        }

        ImGui.Separator();
        ImGui.TextDisabled("E / X: Confirm");
        ImGui.EndGroup();
        ImGui.End();
        ImGui.PopStyleVar();
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
            bool combineSource = hasCoveredItem && coveredItem!.IsCombineSource;
            bool combineTarget = hasCoveredItem && coveredItem!.IsValidCombineTarget;
            bool combineInvalid = hasCoveredItem && coveredItem!.IsInvalidCombineTarget;
            bool useTarget = hasCoveredItem && coveredItem!.IsValidUseTarget;
            bool useInvalid = hasCoveredItem && coveredItem!.IsInvalidUseTarget;
            Vector4 fill = hasCoveredItem
                ? new Vector4(0.11f, 0.14f, 0.14f, 0.94f)
                : new Vector4(0.055f, 0.065f, 0.070f, 0.88f);
            if (occupiedByFootprint)
                fill = new Vector4(0.085f, 0.11f, 0.11f, 0.94f);
            if (useInvalid)
                fill = new Vector4(0.060f, 0.064f, 0.070f, 0.76f);
            if (useTarget)
                fill = new Vector4(0.13f, 0.28f, 0.46f, 0.98f);
            if (combineInvalid)
                fill = new Vector4(0.060f, 0.064f, 0.064f, 0.76f);
            if (combineTarget)
                fill = new Vector4(0.12f, 0.34f, 0.25f, 0.98f);
            if (combineSource)
                fill = new Vector4(0.28f, 0.23f, 0.10f, 0.98f);
            if (selected)
                fill = new Vector4(0.29f, 0.34f, 0.33f, 0.98f);
            if (selected && useTarget)
                fill = new Vector4(0.16f, 0.36f, 0.62f, 1f);
            if (selected && combineTarget)
                fill = new Vector4(0.16f, 0.45f, 0.32f, 1f);
            if (selected && combineSource)
                fill = new Vector4(0.36f, 0.30f, 0.13f, 1f);
            if (movingSource)
                fill = new Vector4(0.23f, 0.22f, 0.13f, 0.98f);
            if (movingTarget)
                fill = state.CanPlaceMovingItem
                    ? new Vector4(0.16f, 0.35f, 0.25f, 0.98f)
                    : new Vector4(0.42f, 0.16f, 0.15f, 0.98f);

            ImGui.PushStyleColor(ImGuiCol.Button, fill);
            ImGui.PushStyleColor(
                ImGuiCol.ButtonHovered,
                combineTarget ? new Vector4(0.20f, 0.52f, 0.38f, 1f)
                    : useTarget ? new Vector4(0.22f, 0.42f, 0.70f, 1f)
                    : new Vector4(0.24f, 0.28f, 0.27f, 0.98f));
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

    private static int DrawUseItemPanel(GameplayUiState state, ImGuiViewportPtr viewport)
    {
        int selectedSlot = -1;
        List<GameplayUiInventoryItem> candidates = state.InventoryItems
            .Where(item => item.IsUseCandidate)
            .ToList();

        Vector2 center = new(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(620f, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.96f);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.018f, 0.022f, 0.026f, 0.97f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.66f, 0.68f, 0.61f, 0.48f));
        ImGui.Begin("RmlUiPreviewUseItem", flags);
        ImGui.TextColored(new Vector4(0.92f, 0.91f, 0.84f, 1f), "Use Item");
        if (!string.IsNullOrWhiteSpace(state.UseTargetPrompt))
            ImGui.TextWrapped(state.UseTargetPrompt);
        ImGui.Separator();

        if (candidates.Count == 0)
        {
            ImGui.TextDisabled("No usable items are being carried.");
        }
        else
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                GameplayUiInventoryItem item = candidates[i];
                bool selected = item.SlotIndex == state.SelectedSlot;
                Vector4 fill = item.IsValidUseTarget
                    ? new Vector4(0.13f, 0.28f, 0.46f, 0.98f)
                    : new Vector4(0.12f, 0.13f, 0.14f, 0.92f);
                if (selected)
                    fill = item.IsValidUseTarget
                        ? new Vector4(0.16f, 0.36f, 0.62f, 1f)
                        : new Vector4(0.28f, 0.29f, 0.30f, 1f);

                ImGui.PushStyleColor(ImGuiCol.Button, fill);
                ImGui.PushStyleColor(
                    ImGuiCol.ButtonHovered,
                    item.IsValidUseTarget ? new Vector4(0.22f, 0.42f, 0.70f, 1f) : new Vector4(0.24f, 0.26f, 0.27f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.36f, 0.40f, 0.42f, 1f));

                if (ImGui.Button($"##useCandidate{i}", new Vector2(570f, 66f)))
                    selectedSlot = item.SlotIndex;

                Vector2 min = ImGui.GetItemRectMin();
                Vector2 max = ImGui.GetItemRectMax();
                ImDrawListPtr drawList = ImGui.GetWindowDrawList();
                uint iconBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.72f, 0.72f, 0.64f, 0.35f));
                uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.94f, 0.93f, 0.86f, 1f));
                uint mutedColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.62f, 0.65f, 0.61f, 1f));
                uint validColor = ImGui.ColorConvertFloat4ToU32(item.IsValidUseTarget
                    ? new Vector4(0.48f, 0.72f, 1f, 1f)
                    : new Vector4(0.68f, 0.70f, 0.66f, 1f));
                Vector2 iconMin = min + new Vector2(12f, 10f);
                Vector2 iconMax = iconMin + new Vector2(46f, 46f);
                drawList.AddRect(iconMin, iconMax, iconBorder, 3f, ImDrawFlags.None, 1.5f);
                drawList.AddText(iconMin + new Vector2(10f, 16f), ImGui.ColorConvertFloat4ToU32(GetItemToneColor(item.Type)), GetItemSymbol(item.Id, item.Type));

                string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
                string validity = item.IsValidUseTarget ? "Ready" : "Doesn't fit";
                drawList.AddText(min + new Vector2(72f, 13f), textColor, $"{item.DisplayName}{countSuffix}");
                drawList.AddText(min + new Vector2(72f, 36f), mutedColor, $"{item.Type} | {item.SlotWidth}x{item.SlotHeight}");
                Vector2 validitySize = ImGui.CalcTextSize(validity);
                drawList.AddText(new Vector2(max.X - validitySize.X - 18f, min.Y + 24f), validColor, validity);

                if (ImGui.IsItemHovered())
                    selectedSlot = item.SlotIndex;

                ImGui.PopStyleColor(3);
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("W/S or D-pad: Select | E / X: Use | I / Back: Cancel");
        ImGui.End();
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

        GameplayUiInventoryItem? selectedItem = state.SelectedSlot >= 0 && bySlot.TryGetValue(state.SelectedSlot, out GameplayUiInventoryItem? item)
            ? item
            : null;

        if (selectedItem != null)
        {
            string countSuffix = selectedItem.Count > 1 ? $" x{selectedItem.Count}" : "";
            ImGui.TextColored(new Vector4(0.90f, 0.89f, 0.82f, 1f), $"{selectedItem.DisplayName}{countSuffix}");
            ImGui.TextDisabled($"{selectedItem.Type} | {selectedItem.SlotWidth}x{selectedItem.SlotHeight} slots | Stack {selectedItem.MaxStack}");
            ImGui.Spacing();
            if (!string.IsNullOrWhiteSpace(selectedItem.Description))
                ImGui.TextWrapped(selectedItem.Description);
        }
        else
        {
            ImGui.TextColored(new Vector4(0.90f, 0.89f, 0.82f, 1f), "No item selected");
            ImGui.TextDisabled("Move across the case with WASD, D-pad, or left stick.");
        }

        ImGui.Spacing();
        if (state.SaveCount > 0)
            ImGui.TextDisabled($"Saves used: {state.SaveCount}");
        if (state.UsingInventoryItem)
        {
            ImGui.Spacing();
            if (!string.IsNullOrWhiteSpace(state.UseTargetPrompt))
                ImGui.TextWrapped(state.UseTargetPrompt);

            if (selectedItem?.IsValidUseTarget == true)
            {
                ImGui.TextColored(new Vector4(0.46f, 0.68f, 0.98f, 1f), "This item can be used here.");
                ImGui.TextDisabled("E / X or click: Use item");
            }
            else if (selectedItem?.IsInvalidUseTarget == true)
            {
                ImGui.TextColored(new Vector4(0.78f, 0.48f, 0.42f, 1f), "This item does not fit this use.");
                ImGui.TextDisabled("Choose a blue highlighted item.");
            }
            else
            {
                ImGui.TextDisabled("Select a highlighted item to use.");
            }

            ImGui.TextDisabled("I / Back: Cancel use");
        }
        else if (state.CombiningInventoryItem)
        {
            ImGui.Spacing();
            if (selectedItem?.IsValidCombineTarget == true)
            {
                ImGui.TextColored(new Vector4(0.48f, 0.82f, 0.58f, 1f), "This item can be combined.");
                if (!string.IsNullOrWhiteSpace(state.CombinePreviewTitle))
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.78f, 0.90f, 0.70f, 1f), state.CombinePreviewTitle);
                    string resultSuffix = state.CombinePreviewResultCount > 1 ? $" x{state.CombinePreviewResultCount}" : "";
                    if (!string.IsNullOrWhiteSpace(state.CombinePreviewResultName))
                        ImGui.TextDisabled($"Result: {state.CombinePreviewResultName}{resultSuffix}");
                    if (!string.IsNullOrWhiteSpace(state.CombinePreviewDescription))
                        ImGui.TextWrapped(state.CombinePreviewDescription);
                }

                ImGui.TextDisabled("E / X or click: Combine");
            }
            else if (selectedItem?.IsCombineSource == true)
            {
                ImGui.TextColored(new Vector4(0.86f, 0.76f, 0.38f, 1f), "Combining from this item.");
                ImGui.TextDisabled("Select a highlighted target.");
            }
            else if (selectedItem?.IsInvalidCombineTarget == true)
            {
                ImGui.TextColored(new Vector4(0.78f, 0.48f, 0.42f, 1f), "This item cannot combine here.");
                ImGui.TextDisabled("Choose a highlighted item instead.");
            }
            else
            {
                ImGui.TextDisabled("Select a highlighted item to combine.");
            }

            ImGui.TextDisabled("I / Back: Cancel combine");
        }
        else if (state.MovingInventoryItem)
        {
            ImGui.Spacing();
            string moveHint = state.CanSwapMovingItem
                ? "Release here to swap items."
                : state.CanMergeMovingItem
                    ? "Release here to merge stacks."
                : state.CanPlaceMovingItem
                    ? "Move target is valid."
                    : "That item will not fit there.";
            string rotationText = state.MovingItemRotated ? "rotated" : "normal";
            ImGui.TextColored(
                state.CanPlaceMovingItem ? new Vector4(0.48f, 0.78f, 0.55f, 1f) : new Vector4(0.92f, 0.42f, 0.36f, 1f),
                moveHint);
            ImGui.TextDisabled($"Held footprint: {state.MovingItemSlotWidth}x{state.MovingItemSlotHeight} ({rotationText})");
            ImGui.TextDisabled("R / Y: Rotate");
            ImGui.TextDisabled("E / X: Place");
            ImGui.TextDisabled("I / Back: Cancel move");
        }
        else
        {
            ImGui.TextDisabled("E / X: Item actions");
            ImGui.TextDisabled("R / Y: Rotate selected item");
            ImGui.TextDisabled("Q / LB: Split stack");
            ImGui.TextDisabled("I / Back: Close");
        }

        ImGui.EndChild();
    }

    private static string ShortLabel(string text)
        => text.Length <= 10 ? text : text[..10];

    private static Vector4 GetItemToneColor(string itemType)
        => itemType.ToLowerInvariant() switch
        {
            "key" => new Vector4(0.95f, 0.74f, 0.36f, 1f),
            "puzzle" => new Vector4(0.58f, 0.80f, 0.86f, 1f),
            "consumable" => new Vector4(0.58f, 0.66f, 1.00f, 1f),
            "material" => new Vector4(0.72f, 0.70f, 0.54f, 1f),
            _ => new Vector4(0.76f, 0.78f, 0.74f, 1f)
        };

    private static string GetItemSymbol(string itemId, string itemType)
    {
        string normalized = itemId.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        if (normalized.Contains("key", StringComparison.Ordinal))
            return "KEY";
        if (normalized.Contains("ink", StringComparison.Ordinal))
            return "INK";
        if (normalized.Contains("crank", StringComparison.Ordinal))
            return "CRK";
        if (normalized.Contains("fuse", StringComparison.Ordinal))
            return "FUS";
        if (normalized.Contains("powder", StringComparison.Ordinal))
            return "PDR";
        if (normalized.Contains("bullet", StringComparison.Ordinal))
            return "AMO";
        if (normalized.Contains("scrap", StringComparison.Ordinal))
            return "SCR";

        return itemType.Length >= 3 ? itemType[..3].ToUpperInvariant() : "ITM";
    }
}

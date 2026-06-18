using System.Net;
using System.Text;

namespace Engine.UI.Rml;

internal readonly record struct RmlUiTextImage(string Source, int Width, int Height);

internal delegate bool RmlUiTextImageResolver(string text, string role, int pixelHeight, out RmlUiTextImage image);

internal static class RmlUiDocumentBuilder
{
    private const int StorageBoxDataSlotOffset = 1000;
    private const int InventoryActionDataSlotOffset = 2000;

    public static string Build(GameplayUiState state, RmlUiTextImageResolver? textImageResolver = null)
    {
        StringBuilder sb = new();
        sb.AppendLine("<rml>");
        sb.AppendLine("  <head>");
        sb.AppendLine("    <title>Gameplay UI</title>");
        sb.AppendLine("    <link type=\"text/rcss\" href=\"../Inventory/inventory.rcss\" />");
        sb.AppendLine("  </head>");
        sb.AppendLine("  <body>");
        sb.AppendLine("    <div id=\"gameplay-root\">");

        AppendCrosshair(sb, state);
        AppendPrompt(sb, state, textImageResolver);

        if (state.ItemCollectedOpen && state.CollectedItem != null)
            AppendCollectedItem(sb, state.CollectedItem, textImageResolver);

        if (state.SaveSlotPanelOpen)
        {
            AppendSaveSlotPanel(sb, state, textImageResolver);
        }
        else if (state.LoadSlotPanelOpen)
        {
            AppendLoadSlotPanel(sb, state, textImageResolver);
        }
        else if (state.PauseMenuOpen)
        {
            AppendPauseMenu(sb, state, textImageResolver);
        }
        else if (state.StorageOpen)
        {
            AppendStoragePanel(sb, state, textImageResolver);
        }
        else if (state.InventoryOpen)
        {
            if (state.UsingInventoryItem)
                AppendUseItemPanel(sb, state, textImageResolver);
            else
                AppendInventory(sb, state, textImageResolver);
        }

        sb.AppendLine("    </div>");
        sb.AppendLine("  </body>");
        sb.AppendLine("</rml>");
        return sb.ToString();
    }

    private static void AppendCrosshair(StringBuilder sb, GameplayUiState state)
    {
        if (!state.CrosshairVisible)
            return;

        sb.AppendLine($"      <div id=\"crosshair\" style=\"left: {Math.Max(0, state.CrosshairLeft)}px; top: {Math.Max(0, state.CrosshairTop)}px;\">");
        sb.AppendLine("        <div class=\"crosshair-line horizontal left\"></div>");
        sb.AppendLine("        <div class=\"crosshair-line horizontal right\"></div>");
        sb.AppendLine("        <div class=\"crosshair-line vertical top\"></div>");
        sb.AppendLine("        <div class=\"crosshair-line vertical bottom\"></div>");
        sb.AppendLine("        <div class=\"crosshair-dot\"></div>");
        sb.AppendLine("      </div>");
    }

    private static void AppendPrompt(StringBuilder sb, GameplayUiState state, RmlUiTextImageResolver? textImageResolver)
    {
        if (string.IsNullOrWhiteSpace(state.InteractionPrompt) && string.IsNullOrWhiteSpace(state.GameMessage))
            return;

        sb.AppendLine("      <section id=\"prompt-panel\">");
        if (!string.IsNullOrWhiteSpace(state.InteractionPrompt))
            sb.AppendLine($"        <div class=\"prompt-action\"><div class=\"input-pill\">{TextOrImage("E / X", "prompt-key", 15, textImageResolver)}</div><div class=\"prompt-copy\">{TextOrImage(state.InteractionPrompt, "prompt", 17, textImageResolver)}</div></div>");
        if (!string.IsNullOrWhiteSpace(state.GameMessage))
            sb.AppendLine($"        <div class=\"prompt-message\">{TextOrImage(state.GameMessage, "muted", 14, textImageResolver)}</div>");
        sb.AppendLine("      </section>");
    }

    private static void AppendStoragePanel(StringBuilder sb, GameplayUiState state, RmlUiTextImageResolver? textImageResolver)
    {
        sb.AppendLine("      <section id=\"storage-panel\" class=\"glass-panel\">");
        sb.AppendLine("        <div class=\"inventory-backplate\"></div>");
        sb.AppendLine("        <div class=\"storage-header\">");
        sb.AppendLine($"          <div class=\"storage-title\">{TextOrImage("Item Box", "title", 29, textImageResolver)}</div>");
        sb.AppendLine($"          <div class=\"storage-subtitle\">{TextOrImage("Shared safe storage", "subtitle", 12, textImageResolver)}</div>");
        sb.AppendLine("        </div>");

        AppendStorageGrid(sb, "storage-inventory-grid", "Inventory", state.InventoryItems, state.SelectedSlot, !state.StorageFocusStorage);
        AppendStorageGrid(sb, "storage-box-grid", "Storage", state.StorageItems, state.SelectedStorageSlot, state.StorageFocusStorage);

        sb.AppendLine("        <div class=\"storage-footer\">Mouse: Hover / Click - Tab / LB / RB: Switch side - E / X: Transfer - I / Back: Close</div>");

        if (state.StorageTransferPickerOpen)
        {
            string direction = state.StorageTransferFromStorage ? "Take" : "Store";
            sb.AppendLine("        <section id=\"storage-transfer-picker\" class=\"floating-menu\">");
            sb.AppendLine($"          <div class=\"menu-title\">{TextOrImage($"{direction} Stack", "subtitle", 12, textImageResolver)}</div>");
            sb.AppendLine($"          <div class=\"quantity-readout\">x{Math.Max(1, state.StorageTransferAmount)}</div>");
            sb.AppendLine("          <div class=\"menu-footer\">A/D or D-pad: Amount - E / X: Confirm - I / Back: Cancel</div>");
            sb.AppendLine("        </section>");
        }

        sb.AppendLine("      </section>");
    }

    private static void AppendStorageGrid(
        StringBuilder sb,
        string id,
        string title,
        IReadOnlyList<GameplayUiInventoryItem> items,
        int selectedSlot,
        bool focused)
    {
        Dictionary<int, GameplayUiInventoryItem> byOriginSlot = items.ToDictionary(item => item.SlotIndex);
        Dictionary<int, GameplayUiInventoryItem> byCoveredSlot = BuildCoveredSlotLookup(items);
        string focusedClass = focused ? " focused" : "";
        bool storageSide = string.Equals(id, "storage-box-grid", StringComparison.OrdinalIgnoreCase);

        sb.AppendLine($"        <div id=\"{Esc(id)}\" class=\"storage-grid-panel{focusedClass}\">");
        sb.AppendLine($"          <div class=\"storage-grid-title\">{Esc(title)}</div>");
        sb.AppendLine("          <div class=\"storage-grid\">");

        const int gridWidth = 8;
        const int gridHeight = 4;
        int slotCapacity = gridWidth * gridHeight;
        for (int slot = 0; slot < slotCapacity; slot++)
        {
            bool selected = focused && slot == selectedSlot;
            string selectedClass = selected ? " selected" : "";
            string slotStyle = BuildStorageSlotStyle(slot, gridWidth);
            int dataSlot = storageSide ? StorageBoxDataSlotOffset + slot : slot;
            bool covered = byCoveredSlot.TryGetValue(slot, out GameplayUiInventoryItem? coveredItem);
            bool origin = byOriginSlot.TryGetValue(slot, out GameplayUiInventoryItem? item);

            if (origin && item != null)
            {
                string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
                sb.AppendLine($"            <div class=\"slot filled{selectedClass}\" data-slot=\"{dataSlot}\"{slotStyle}>");
                sb.AppendLine($"              <div class=\"mini-icon {Esc(ItemToneClass(item.Type))}\">");
                AppendIconOrText(sb, item.IconPath, item.Id, item.Type, "mini");
                sb.AppendLine("              </div>");
                sb.AppendLine($"              <p class=\"slot-name\">{Esc(ShortLabel(item.DisplayName))}{Esc(countSuffix)}</p>");
                sb.AppendLine("            </div>");
            }
            else if (covered && coveredItem != null)
            {
                sb.AppendLine($"            <div class=\"slot filled footprint-covered{selectedClass}\" data-slot=\"{dataSlot}\"{slotStyle}>");
                sb.AppendLine("              <div class=\"covered-stitch\"></div>");
                sb.AppendLine($"              <p class=\"slot-name\">{Esc(ShortLabel(coveredItem.DisplayName))}</p>");
                sb.AppendLine("            </div>");
            }
            else
            {
                sb.AppendLine($"            <div class=\"slot empty{selectedClass}\" data-slot=\"{dataSlot}\"{slotStyle}></div>");
            }
        }

        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
    }

    private static void AppendCollectedItem(StringBuilder sb, GameplayUiCollectedItem item, RmlUiTextImageResolver? textImageResolver)
    {
        string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
        string toneClass = ItemToneClass(item.Type);
        sb.AppendLine("      <section id=\"collected-panel\" class=\"modal-card\">");
        sb.AppendLine("        <div class=\"modal-rule\"></div>");
        sb.AppendLine("        <div class=\"collected-layout\">");
        sb.AppendLine($"          <div class=\"item-showcase {Esc(toneClass)}\">");
        sb.AppendLine("            <div class=\"showcase-glow\"></div>");
        AppendIconOrText(sb, item.IconPath, item.Id, item.Type, "showcase");
        sb.AppendLine("            <div class=\"showcase-gridline\"></div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"collected-copy\">");
        sb.AppendLine($"            <div class=\"collected-kicker\">{TextOrImage(item.Title, "subtitle", 12, textImageResolver)}</div>");
        sb.AppendLine($"            <div class=\"collected-name\">{TextOrImage($"{item.DisplayName}{countSuffix}", "title", 24, textImageResolver)}</div>");
        sb.AppendLine($"            <div class=\"collected-meta\">{Esc(item.Type)} item</div>");
        sb.AppendLine($"            <div class=\"collected-footprint\">Case slots {item.SlotWidth} x {item.SlotHeight} - Stack {item.MaxStack}</div>");
        if (!string.IsNullOrWhiteSpace(item.Description))
            sb.AppendLine($"            <div class=\"collected-description\">{Esc(item.Description)}</div>");
        sb.AppendLine($"            <div class=\"collected-confirm\">{TextOrImage("E / X: Confirm", "footer", 13, textImageResolver)}</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </section>");
    }

    private static void AppendPauseMenu(StringBuilder sb, GameplayUiState state, RmlUiTextImageResolver? textImageResolver)
    {
        string resumeClass = state.SelectedPauseMenuIndex == 0 ? " selected" : "";
        string loadClass = state.SelectedPauseMenuIndex == 1 ? " selected" : "";

        sb.AppendLine("      <section id=\"pause-panel\" class=\"modal-card\">");
        sb.AppendLine($"        <div class=\"pause-title\">{TextOrImage("Paused", "title", 30, textImageResolver)}</div>");
        sb.AppendLine($"        <div class=\"pause-subtitle\">{TextOrImage("Interaction test", "subtitle", 12, textImageResolver)}</div>");
        sb.AppendLine("        <div id=\"pause-menu-list\">");
        sb.AppendLine($"          <div class=\"pause-menu-row{resumeClass}\" data-slot=\"0\" style=\"top: 0px;\">{TextOrImage("Resume", "menu", 17, textImageResolver)}</div>");
        sb.AppendLine($"          <div class=\"pause-menu-row{loadClass}\" data-slot=\"1\" style=\"top: 54px;\">{TextOrImage("Load Save", "menu", 17, textImageResolver)}</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine($"        <div class=\"pause-footer\">{TextOrImage("Mouse: Hover / Click - W/S or D-pad: Select - E / X: Confirm - Esc / I / Back: Resume", "footer", 11, textImageResolver)}</div>");
        sb.AppendLine("      </section>");
    }

    private static void AppendLoadSlotPanel(StringBuilder sb, GameplayUiState state, RmlUiTextImageResolver? textImageResolver)
    {
        int count = Math.Max(1, state.SaveSlots.Count);
        int panelHeight = 170 + count * 82;
        sb.AppendLine($"      <section id=\"load-slot-panel\" class=\"modal-card\" style=\"height: {panelHeight}px;\">");
        sb.AppendLine($"        <div class=\"save-title\">{TextOrImage("Load Game", "title", 27, textImageResolver)}</div>");
        sb.AppendLine($"        <div class=\"save-subtitle\">{TextOrImage("Choose saved progress", "subtitle", 12, textImageResolver)}</div>");
        sb.AppendLine("        <div id=\"save-slot-list\">");

        if (state.SaveSlots.Count == 0)
        {
            sb.AppendLine("          <div class=\"save-slot-row selected empty\" data-slot=\"0\" style=\"top: 0px;\">");
            sb.AppendLine($"            <div class=\"save-slot-name\">{TextOrImage("Slot 1", "menu", 16, textImageResolver)}</div>");
            sb.AppendLine($"            <div class=\"save-slot-meta\">{TextOrImage("Empty slot", "muted", 11, textImageResolver)}</div>");
            sb.AppendLine("          </div>");
        }
        else
        {
            for (int i = 0; i < state.SaveSlots.Count; i++)
            {
                GameplayUiSaveSlot slot = state.SaveSlots[i];
                string selectedClass = slot.SlotIndex == state.SelectedLoadSlotIndex ? " selected" : "";
                string emptyClass = slot.IsEmpty ? " empty" : " filled";
                sb.AppendLine($"          <div class=\"save-slot-row{selectedClass}{emptyClass}\" data-slot=\"{slot.SlotIndex}\" style=\"top: {i * 82}px;\">");
                sb.AppendLine($"            <div class=\"save-slot-name\">{TextOrImage(slot.Label, "menu", 16, textImageResolver)}</div>");
                if (slot.IsEmpty)
                {
                    sb.AppendLine($"            <div class=\"save-slot-meta\">{TextOrImage("Empty slot", "muted", 11, textImageResolver)}</div>");
                }
                else
                {
                    sb.AppendLine($"            <div class=\"save-slot-meta\">{TextOrImage($"{slot.AreaName} - Saves {slot.SaveCount}", "muted", 11, textImageResolver)}</div>");
                    sb.AppendLine($"            <div class=\"save-slot-playtime\">Play time {Esc(slot.PlayTime)}</div>");
                    sb.AppendLine($"            <div class=\"save-slot-time\">{Esc(slot.SavedAt)}</div>");
                    sb.AppendLine($"            <div class=\"save-slot-cargo\">{Esc(slot.SavePointName)} - Inventory {slot.InventoryCount} / Storage {slot.StorageCount}</div>");
                }
                sb.AppendLine("          </div>");
            }
        }

        sb.AppendLine("        </div>");
        sb.AppendLine($"        <div class=\"save-footer\">{TextOrImage("Mouse: Hover / Click - W/S or D-pad: Select - E / X: Load - Esc / I / Back: Back", "footer", 12, textImageResolver)}</div>");
        sb.AppendLine("      </section>");
    }

    private static void AppendSaveSlotPanel(StringBuilder sb, GameplayUiState state, RmlUiTextImageResolver? textImageResolver)
    {
        int count = Math.Max(1, state.SaveSlots.Count);
        int panelHeight = 170 + count * 82;
        sb.AppendLine($"      <section id=\"save-slot-panel\" class=\"modal-card\" style=\"height: {panelHeight}px;\">");
        sb.AppendLine($"        <div class=\"save-title\">{TextOrImage("Save Game", "title", 27, textImageResolver)}</div>");
        sb.AppendLine($"        <div class=\"save-subtitle\">{TextOrImage("Choose a typewriter slot", "subtitle", 12, textImageResolver)}</div>");
        sb.AppendLine("        <div id=\"save-slot-list\">");

        if (state.SaveSlots.Count == 0)
        {
            sb.AppendLine("          <div class=\"save-slot-row selected\" data-slot=\"0\" style=\"top: 0px;\">");
            sb.AppendLine($"            <div class=\"save-slot-name\">{TextOrImage("Slot 1", "menu", 16, textImageResolver)}</div>");
            sb.AppendLine($"            <div class=\"save-slot-meta\">{TextOrImage("Empty", "muted", 11, textImageResolver)}</div>");
            sb.AppendLine("          </div>");
        }
        else
        {
            for (int i = 0; i < state.SaveSlots.Count; i++)
            {
                GameplayUiSaveSlot slot = state.SaveSlots[i];
                string selectedClass = slot.SlotIndex == state.SelectedSaveSlotIndex ? " selected" : "";
                string emptyClass = slot.IsEmpty ? " empty" : " filled";
                sb.AppendLine($"          <div class=\"save-slot-row{selectedClass}{emptyClass}\" data-slot=\"{slot.SlotIndex}\" style=\"top: {i * 82}px;\">");
                sb.AppendLine($"            <div class=\"save-slot-name\">{TextOrImage(slot.Label, "menu", 16, textImageResolver)}</div>");
                if (slot.IsEmpty)
                {
                    sb.AppendLine($"            <div class=\"save-slot-meta\">{TextOrImage("Empty slot", "muted", 11, textImageResolver)}</div>");
                }
                else
                {
                    sb.AppendLine($"            <div class=\"save-slot-meta\">{TextOrImage($"{slot.AreaName} - Saves {slot.SaveCount}", "muted", 11, textImageResolver)}</div>");
                    sb.AppendLine($"            <div class=\"save-slot-playtime\">Play time {Esc(slot.PlayTime)}</div>");
                    sb.AppendLine($"            <div class=\"save-slot-time\">{Esc(slot.SavedAt)}</div>");
                    sb.AppendLine($"            <div class=\"save-slot-cargo\">{Esc(slot.SavePointName)} - Inventory {slot.InventoryCount} / Storage {slot.StorageCount}</div>");
                }
                sb.AppendLine("          </div>");
            }
        }

        sb.AppendLine("        </div>");
        if (state.SaveOverwriteConfirmOpen)
        {
            GameplayUiSaveSlot? selected = state.SaveSlots.FirstOrDefault(slot => slot.SlotIndex == state.SelectedSaveSlotIndex);
            string slotName = selected?.Label ?? $"Slot {state.SelectedSaveSlotIndex + 1}";
            sb.AppendLine("        <section id=\"save-overwrite-confirm\" class=\"floating-menu warning-menu\">");
            sb.AppendLine($"          <div class=\"menu-title\">{TextOrImage("Overwrite Save?", "warning", 12, textImageResolver)}</div>");
            sb.AppendLine($"          <div class=\"menu-copy\">{TextOrImage($"{slotName} already contains saved progress.", "muted", 12, textImageResolver)}</div>");
            sb.AppendLine("          <div class=\"menu-footer\">E / X: Overwrite - I / Back: Cancel</div>");
            sb.AppendLine("        </section>");
        }
        sb.AppendLine($"        <div class=\"save-footer\">{TextOrImage("Mouse: Hover / Click - W/S or D-pad: Select - E / X: Save - I / Back: Cancel", "footer", 12, textImageResolver)}</div>");
        sb.AppendLine("      </section>");
    }

    private static void AppendInventory(StringBuilder sb, GameplayUiState state, RmlUiTextImageResolver? textImageResolver)
    {
        Dictionary<int, GameplayUiInventoryItem> byOriginSlot = state.InventoryItems.ToDictionary(item => item.SlotIndex);
        Dictionary<int, GameplayUiInventoryItem> byCoveredSlot = BuildCoveredSlotLookup(state);
        GameplayUiInventoryItem? selected = byCoveredSlot.GetValueOrDefault(state.SelectedSlot);

        sb.AppendLine("      <section id=\"inventory-panel\" class=\"glass-panel\">");
        sb.AppendLine("        <div class=\"inventory-backplate\"></div>");
        sb.AppendLine("        <div id=\"inventory-header\">");
        sb.AppendLine("          <div>");
        sb.AppendLine($"            <p class=\"eyebrow\">{TextOrImage("Attache Case", "eyebrow", 12, textImageResolver)}</p>");
        sb.AppendLine($"            <h1>{TextOrImage("Inventory", "title", 31, textImageResolver)}</h1>");
        sb.AppendLine("          </div>");
        sb.AppendLine($"          <p class=\"slot-count\">{TextOrImage($"{state.UsedSlotCount}/{Math.Max(1, state.GridWidth * state.GridHeight)} occupied", "muted", 15, textImageResolver)}</p>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div id=\"case-panel\">");
        sb.AppendLine($"        <div id=\"inventory-grid\" class=\"cols-{Math.Max(1, state.GridWidth)}\">");

        int slotCapacity = Math.Max(1, state.GridWidth * state.GridHeight);
        for (int slot = 0; slot < slotCapacity; slot++)
        {
            string slotStyle = BuildSlotStyle(slot, state.GridWidth);
            bool isSelected = slot == state.SelectedSlot;
            string selectedClass = isSelected ? " selected" : "";
            if (state.MovingInventoryItem && slot == state.MovingFromSlot)
                selectedClass += " moving-source";
            if (state.MovingInventoryItem && slot == state.MovingTargetSlot)
                selectedClass += state.CanPlaceMovingItem ? " move-valid" : " move-invalid";
            bool covered = byCoveredSlot.TryGetValue(slot, out GameplayUiInventoryItem? coveredItem);
            bool origin = byOriginSlot.TryGetValue(slot, out GameplayUiInventoryItem? item);
            if (covered && coveredItem != null)
            {
                if (coveredItem.IsCombineSource)
                    selectedClass += " combine-source";
                else if (coveredItem.IsValidCombineTarget)
                    selectedClass += " combine-valid";
                else if (coveredItem.IsInvalidCombineTarget)
                    selectedClass += " combine-invalid";

                if (coveredItem.IsValidUseTarget)
                    selectedClass += " use-valid";
                else if (coveredItem.IsInvalidUseTarget)
                    selectedClass += " use-invalid";
            }

            if (origin && item != null)
            {
                string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
                string footprintClass = item.SlotWidth > 1 || item.SlotHeight > 1 ? " footprint-origin" : "";
                string rotatedClass = item.Rotated ? " rotated" : "";
                string toneClass = ItemToneClass(item.Type);
                sb.AppendLine($"          <div class=\"slot filled{footprintClass}{rotatedClass}{selectedClass}\" data-slot=\"{slot}\"{slotStyle}>");
                sb.AppendLine($"            <div class=\"mini-icon {Esc(toneClass)}\">");
                AppendIconOrText(sb, item.IconPath, item.Id, item.Type, "mini");
                sb.AppendLine("            </div>");
                sb.AppendLine($"            <p class=\"slot-name\">{Esc(ShortLabel(item.DisplayName))}{Esc(countSuffix)}</p>");
                sb.AppendLine($"            <p class=\"slot-footprint\">{item.SlotWidth} x {item.SlotHeight}</p>");
                sb.AppendLine("          </div>");
            }
            else if (covered && coveredItem != null)
            {
                sb.AppendLine($"          <div class=\"slot filled footprint-covered{selectedClass}\" data-slot=\"{slot}\"{slotStyle}>");
                sb.AppendLine("            <div class=\"covered-stitch\"></div>");
                sb.AppendLine($"            <p class=\"slot-name\">{Esc(ShortLabel(coveredItem.DisplayName))}</p>");
                sb.AppendLine("          </div>");
            }
            else
            {
                sb.AppendLine($"          <div class=\"slot empty{selectedClass}\" data-slot=\"{slot}\"{slotStyle}></div>");
            }
        }

        sb.AppendLine("        </div>");
        sb.AppendLine("        <aside id=\"description-panel\">");
        if (selected != null)
        {
            string countSuffix = selected.Count > 1 ? $" x{selected.Count}" : "";
            string toneClass = ItemToneClass(selected.Type);
            sb.AppendLine($"          <div class=\"item-preview-card {Esc(toneClass)}\">");
            AppendIconOrText(sb, selected.IconPath, selected.Id, selected.Type, "preview");
            sb.AppendLine("          </div>");
            sb.AppendLine($"          <h2>{TextOrImage($"{selected.DisplayName}{countSuffix}", "title", 23, textImageResolver)}</h2>");
            sb.AppendLine($"          <p class=\"muted\">{TextOrImage($"{selected.Type} | {selected.SlotWidth}x{selected.SlotHeight} slots | Stack {selected.MaxStack}", "muted", 13, textImageResolver)}</p>");
            if (!string.IsNullOrWhiteSpace(selected.Description))
                sb.AppendLine($"          <p class=\"description\">{Esc(selected.Description)}</p>");
        }
        else
        {
            sb.AppendLine("          <div class=\"item-preview-card empty-preview\"><div class=\"preview-symbol\">?</div></div>");
            sb.AppendLine($"          <h2>{TextOrImage("No item selected", "title", 23, textImageResolver)}</h2>");
            sb.AppendLine($"          <p class=\"muted\">{TextOrImage("Move across the case with WASD, D-pad, or left stick.", "muted", 13, textImageResolver)}</p>");
        }

        if (state.SaveCount > 0)
            sb.AppendLine($"          <p class=\"muted\">{TextOrImage($"Saves used: {state.SaveCount}", "muted", 13, textImageResolver)}</p>");

        if (state.UsingInventoryItem)
        {
            if (!string.IsNullOrWhiteSpace(state.UseTargetPrompt))
                sb.AppendLine($"          <p class=\"use-hint\">{TextOrImage(state.UseTargetPrompt, "muted", 13, textImageResolver)}</p>");

            if (selected?.IsValidUseTarget == true)
                sb.AppendLine($"          <p class=\"use-hint good\">{TextOrImage("This item can be used here. E / X: Use", "muted", 13, textImageResolver)}</p>");
            else if (selected?.IsInvalidUseTarget == true)
                sb.AppendLine($"          <p class=\"use-hint bad\">{TextOrImage("This item does not fit this use. Choose a highlighted item.", "warning", 13, textImageResolver)}</p>");
            else
                sb.AppendLine($"          <p class=\"use-hint\">{TextOrImage("Select a highlighted item to use.", "muted", 13, textImageResolver)}</p>");

            sb.AppendLine($"          <p class=\"footer-hint\">{TextOrImage("I / Back: Cancel use", "footer", 12, textImageResolver)}</p>");
        }
        else if (state.CombiningInventoryItem)
        {
            if (selected?.IsValidCombineTarget == true)
            {
                sb.AppendLine($"          <p class=\"combine-hint good\">{TextOrImage("This item can be combined. E / X: Combine", "muted", 13, textImageResolver)}</p>");
                if (!string.IsNullOrWhiteSpace(state.CombinePreviewTitle))
                {
                    string resultSuffix = state.CombinePreviewResultCount > 1 ? $" x{state.CombinePreviewResultCount}" : "";
                    sb.AppendLine("          <div class=\"combine-preview-card\">");
                    sb.AppendLine($"            <p class=\"eyebrow\">{TextOrImage("Recipe", "eyebrow", 12, textImageResolver)}</p>");
                    sb.AppendLine($"            <h2>{TextOrImage(state.CombinePreviewTitle, "title", 20, textImageResolver)}</h2>");
                    if (!string.IsNullOrWhiteSpace(state.CombinePreviewResultName))
                        sb.AppendLine($"            <p class=\"muted\">{TextOrImage($"Result: {state.CombinePreviewResultName}{resultSuffix}", "muted", 13, textImageResolver)}</p>");
                    if (!string.IsNullOrWhiteSpace(state.CombinePreviewDescription))
                        sb.AppendLine($"            <p class=\"description\">{Esc(state.CombinePreviewDescription)}</p>");
                    sb.AppendLine("          </div>");
                }
            }
            else if (selected?.IsCombineSource == true)
                sb.AppendLine($"          <p class=\"combine-hint source\">{TextOrImage("Combining from this item. Select a highlighted target.", "muted", 13, textImageResolver)}</p>");
            else if (selected?.IsInvalidCombineTarget == true)
                sb.AppendLine($"          <p class=\"combine-hint bad\">{TextOrImage("This item cannot combine here. Choose a highlighted item.", "warning", 13, textImageResolver)}</p>");
            else
                sb.AppendLine($"          <p class=\"combine-hint\">{TextOrImage("Select a highlighted item to combine.", "muted", 13, textImageResolver)}</p>");

            sb.AppendLine($"          <p class=\"footer-hint\">{TextOrImage("I / Back: Cancel combine", "footer", 12, textImageResolver)}</p>");
        }
        else if (state.MovingInventoryItem)
        {
            string moveHint = state.CanSwapMovingItem
                ? "Release here to swap items."
                : state.CanMergeMovingItem
                    ? "Release here to merge stacks."
                : state.CanPlaceMovingItem
                    ? "Move target is valid."
                    : "That item will not fit there.";
            sb.AppendLine($"          <p class=\"muted\">{TextOrImage(moveHint, "muted", 13, textImageResolver)}</p>");
            string rotationText = state.MovingItemRotated ? "rotated" : "normal";
            sb.AppendLine($"          <p class=\"muted\">{TextOrImage($"Held footprint: {state.MovingItemSlotWidth}x{state.MovingItemSlotHeight} ({rotationText})", "muted", 13, textImageResolver)}</p>");
            sb.AppendLine($"          <p class=\"footer-hint\">{TextOrImage("E / X: Place | R / Y: Rotate | I / Back: Cancel", "footer", 12, textImageResolver)}</p>");
        }
        else
        {
            sb.AppendLine($"          <p class=\"footer-hint\">{TextOrImage("Mouse: Drag / Click | E / X: Actions | R / Y: Rotate | Q / LB: Split | I / Back: Close", "footer", 12, textImageResolver)}</p>");
        }
        sb.AppendLine("        </aside>");
        sb.AppendLine("        </div>");
        AppendInventoryOverlays(sb, state, textImageResolver);
        sb.AppendLine("      </section>");
    }

    private static string BuildSlotStyle(int slot, int gridWidth)
    {
        const int slotStep = 80;
        int safeGridWidth = Math.Max(1, gridWidth);
        int col = slot % safeGridWidth;
        int row = slot / safeGridWidth;
        return $" style=\"left: {col * slotStep}px; top: {row * slotStep}px;\"";
    }

    private static string BuildStorageSlotStyle(int slot, int gridWidth)
    {
        const int slotStep = 62;
        int safeGridWidth = Math.Max(1, gridWidth);
        int col = slot % safeGridWidth;
        int row = slot / safeGridWidth;
        return $" style=\"left: {col * slotStep}px; top: {row * slotStep}px;\"";
    }

    private static void AppendInventoryOverlays(StringBuilder sb, GameplayUiState state, RmlUiTextImageResolver? textImageResolver)
    {
        if (state.InventoryActionMenuOpen)
        {
            sb.AppendLine("        <section id=\"inventory-action-menu\" class=\"floating-menu\">");
            sb.AppendLine($"          <div class=\"menu-title\">{TextOrImage("Item Actions", "subtitle", 12, textImageResolver)}</div>");
            IReadOnlyList<string> labels = state.InventoryActionLabels.Count > 0
                ? state.InventoryActionLabels
                : new[] { "Use", "Examine", "Move", "Combine", "Split", "Discard" };
            for (int i = 0; i < labels.Count; i++)
            {
                string selectedClass = i == state.SelectedInventoryActionIndex ? " selected" : "";
                sb.AppendLine($"          <div class=\"action-row{selectedClass}\" data-slot=\"{InventoryActionDataSlotOffset + i}\" style=\"top: {36 + i * 34}px;\">{TextOrImage(labels[i], "menu", 15, textImageResolver)}</div>");
            }
            sb.AppendLine($"          <div class=\"menu-footer\">{TextOrImage("Mouse: Hover / Click - W/S or D-pad: Select - E / X: Confirm - I / Back: Cancel", "footer", 11, textImageResolver)}</div>");
            sb.AppendLine("        </section>");
        }

        if (state.InventorySplitPickerOpen)
        {
            sb.AppendLine("        <section id=\"inventory-split-picker\" class=\"floating-menu\">");
            sb.AppendLine($"          <div class=\"menu-title\">{TextOrImage("Split Stack", "subtitle", 12, textImageResolver)}</div>");
            sb.AppendLine($"          <div class=\"quantity-readout\">x{Math.Max(1, state.InventorySplitAmount)}</div>");
            sb.AppendLine($"          <div class=\"menu-footer\">{TextOrImage("A/D or D-pad: Amount - E / X: Confirm - I / Back: Cancel", "footer", 11, textImageResolver)}</div>");
            sb.AppendLine("        </section>");
        }

        if (state.InventoryDiscardConfirmOpen)
        {
            sb.AppendLine("        <section id=\"inventory-discard-confirm\" class=\"floating-menu warning-menu\">");
            sb.AppendLine($"          <div class=\"menu-title\">{TextOrImage("Discard Item?", "warning", 12, textImageResolver)}</div>");
            sb.AppendLine($"          <div class=\"warning-copy\">{TextOrImage("Confirm discard", "warning", 20, textImageResolver)}</div>");
            sb.AppendLine($"          <div class=\"menu-footer\">{TextOrImage("E / X: Discard - I / Back: Cancel", "footer", 11, textImageResolver)}</div>");
            sb.AppendLine("        </section>");
        }
    }

    private static void AppendUseItemPanel(StringBuilder sb, GameplayUiState state, RmlUiTextImageResolver? textImageResolver)
    {
        List<GameplayUiInventoryItem> candidates = state.InventoryItems
            .Where(item => item.IsUseCandidate)
            .ToList();

        int panelHeight = 150 + Math.Max(1, candidates.Count) * 62;
        sb.AppendLine($"      <section id=\"use-item-panel\" class=\"modal-card\" style=\"height: {panelHeight}px;\">");
        sb.AppendLine($"        <div class=\"use-panel-title\">{TextOrImage("Use Item", "subtitle", 12, textImageResolver)}</div>");
        if (!string.IsNullOrWhiteSpace(state.UseTargetPrompt))
            sb.AppendLine($"        <div class=\"use-panel-prompt\">{TextOrImage(state.UseTargetPrompt, "muted", 13, textImageResolver)}</div>");

        sb.AppendLine("        <div id=\"use-item-list\">");
        if (candidates.Count == 0)
        {
            sb.AppendLine($"          <div class=\"empty-use-row\">{TextOrImage("No usable items are being carried.", "muted", 14, textImageResolver)}</div>");
        }
        else
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                GameplayUiInventoryItem item = candidates[i];
                string selectedClass = item.SlotIndex == state.SelectedSlot ? " selected" : "";
                string validClass = item.IsValidUseTarget ? " valid" : " invalid";
                string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
                string validity = item.IsValidUseTarget ? "Ready" : "Doesn't fit";
                sb.AppendLine($"          <div class=\"use-item-row{selectedClass}{validClass}\" data-slot=\"{item.SlotIndex}\" style=\"top: {i * 62}px;\">");
                sb.AppendLine($"            <div class=\"use-item-icon {Esc(ItemToneClass(item.Type))}\">");
                AppendIconOrText(sb, item.IconPath, item.Id, item.Type, "use");
                sb.AppendLine("            </div>");
                sb.AppendLine($"            <div class=\"use-item-name\">{TextOrImage($"{item.DisplayName}{countSuffix}", "menu", 15, textImageResolver)}</div>");
                sb.AppendLine($"            <div class=\"use-item-meta\">{TextOrImage($"{item.Type} item - {item.SlotWidth}x{item.SlotHeight}", "muted", 11, textImageResolver)}</div>");
                sb.AppendLine($"            <div class=\"use-item-validity\">{TextOrImage(validity, item.IsValidUseTarget ? "muted" : "warning", 12, textImageResolver)}</div>");
                sb.AppendLine("          </div>");
            }
        }
        sb.AppendLine("        </div>");
        sb.AppendLine($"        <div class=\"use-panel-footer\">{TextOrImage("W/S or D-pad: Select - E / X: Use - I / Back: Cancel", "footer", 12, textImageResolver)}</div>");
        sb.AppendLine("      </section>");
    }

    private static string ShortLabel(string text)
        => text.Length <= 10 ? text : text[..10];

    private static void AppendIconOrText(StringBuilder sb, string iconPath, string itemId, string itemType, string sizeClass)
    {
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            (int width, int height) = IconSize(sizeClass);
            sb.AppendLine($"              <img class=\"item-icon-image {Esc(sizeClass)}-icon-image\" src=\"{Esc(iconPath)}\" width=\"{width}\" height=\"{height}\" />");
            return;
        }

        sb.AppendLine($"              <span class=\"item-icon-text {Esc(sizeClass)}-symbol\">{Esc(ItemSymbol(itemId, itemType))}</span>");
    }

    private static (int Width, int Height) IconSize(string sizeClass)
        => sizeClass switch
        {
            "showcase" => (132, 120),
            "preview" => (82, 72),
            "use" => (36, 36),
            "mini" => (30, 30),
            _ => (64, 64)
        };

    private static string ItemToneClass(string itemType)
        => itemType.ToLowerInvariant() switch
        {
            "key" => "tone-key",
            "puzzle" => "tone-puzzle",
            "consumable" => "tone-consumable",
            "material" => "tone-material",
            _ => "tone-misc"
        };

    private static string ItemSymbol(string itemId, string itemType)
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

    private static Dictionary<int, GameplayUiInventoryItem> BuildCoveredSlotLookup(GameplayUiState state)
        => BuildCoveredSlotLookup(state.InventoryItems);

    private static Dictionary<int, GameplayUiInventoryItem> BuildCoveredSlotLookup(IReadOnlyList<GameplayUiInventoryItem> items)
    {
        Dictionary<int, GameplayUiInventoryItem> lookup = new();
        foreach (GameplayUiInventoryItem item in items)
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

    private static string TextOrImage(string text, string role, int pixelHeight, RmlUiTextImageResolver? textImageResolver)
    {
        if (textImageResolver != null &&
            textImageResolver(text, role, pixelHeight, out RmlUiTextImage image))
        {
            return $"<img class=\"ui-text-image ui-text-{Esc(role)}\" src=\"{Esc(image.Source)}\" width=\"{image.Width}\" height=\"{image.Height}\" />";
        }

        return Esc(text);
    }

    private static string Esc(string text)
        => WebUtility.HtmlEncode(text);
}

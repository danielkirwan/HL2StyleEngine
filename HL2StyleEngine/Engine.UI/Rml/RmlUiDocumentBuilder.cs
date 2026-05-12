using System.Net;
using System.Text;

namespace Engine.UI.Rml;

internal static class RmlUiDocumentBuilder
{
    public static string Build(GameplayUiState state)
    {
        StringBuilder sb = new();
        sb.AppendLine("<rml>");
        sb.AppendLine("  <head>");
        sb.AppendLine("    <title>Gameplay UI</title>");
        sb.AppendLine("    <link type=\"text/rcss\" href=\"../Inventory/inventory.rcss\" />");
        sb.AppendLine("  </head>");
        sb.AppendLine("  <body>");
        sb.AppendLine("    <div id=\"gameplay-root\">");

        AppendPrompt(sb, state);

        if (state.ItemCollectedOpen && state.CollectedItem != null)
            AppendCollectedItem(sb, state.CollectedItem);

        if (state.StorageOpen)
        {
            AppendStoragePanel(sb, state);
        }
        else if (state.InventoryOpen)
        {
            if (state.UsingInventoryItem)
                AppendUseItemPanel(sb, state);
            else
                AppendInventory(sb, state);
        }

        sb.AppendLine("    </div>");
        sb.AppendLine("  </body>");
        sb.AppendLine("</rml>");
        return sb.ToString();
    }

    private static void AppendPrompt(StringBuilder sb, GameplayUiState state)
    {
        if (string.IsNullOrWhiteSpace(state.InteractionPrompt) && string.IsNullOrWhiteSpace(state.GameMessage))
            return;

        sb.AppendLine("      <section id=\"prompt-panel\">");
        if (!string.IsNullOrWhiteSpace(state.InteractionPrompt))
            sb.AppendLine($"        <div class=\"prompt-action\"><div class=\"input-pill\">E / X</div><div class=\"prompt-copy\">{Esc(state.InteractionPrompt)}</div></div>");
        if (!string.IsNullOrWhiteSpace(state.GameMessage))
            sb.AppendLine($"        <div class=\"prompt-message\">{Esc(state.GameMessage)}</div>");
        sb.AppendLine("      </section>");
    }

    private static void AppendStoragePanel(StringBuilder sb, GameplayUiState state)
    {
        sb.AppendLine("      <section id=\"storage-panel\" class=\"glass-panel\">");
        sb.AppendLine("        <div class=\"inventory-backplate\"></div>");
        sb.AppendLine("        <div class=\"storage-header\">");
        sb.AppendLine("          <div class=\"storage-title\">Item Box</div>");
        sb.AppendLine("          <div class=\"storage-subtitle\">Shared safe storage</div>");
        sb.AppendLine("        </div>");

        AppendStorageGrid(sb, "storage-inventory-grid", "Inventory", state.InventoryItems, state.SelectedSlot, !state.StorageFocusStorage);
        AppendStorageGrid(sb, "storage-box-grid", "Storage", state.StorageItems, state.SelectedStorageSlot, state.StorageFocusStorage);

        sb.AppendLine("        <div class=\"storage-footer\">Tab / LB / RB: Switch side - E / X: Transfer - I / Back: Close</div>");

        if (state.StorageTransferPickerOpen)
        {
            string direction = state.StorageTransferFromStorage ? "Take" : "Store";
            sb.AppendLine("        <section id=\"storage-transfer-picker\" class=\"floating-menu\">");
            sb.AppendLine($"          <div class=\"menu-title\">{Esc(direction)} Stack</div>");
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
            bool covered = byCoveredSlot.TryGetValue(slot, out GameplayUiInventoryItem? coveredItem);
            bool origin = byOriginSlot.TryGetValue(slot, out GameplayUiInventoryItem? item);

            if (origin && item != null)
            {
                string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
                sb.AppendLine($"            <div class=\"slot filled{selectedClass}\"{slotStyle}>");
                sb.AppendLine($"              <div class=\"mini-icon {Esc(ItemToneClass(item.Type))}\">");
                AppendIconOrText(sb, item.IconPath, item.Id, item.Type, "mini");
                sb.AppendLine("              </div>");
                sb.AppendLine($"              <p class=\"slot-name\">{Esc(ShortLabel(item.DisplayName))}{Esc(countSuffix)}</p>");
                sb.AppendLine("            </div>");
            }
            else if (covered && coveredItem != null)
            {
                sb.AppendLine($"            <div class=\"slot filled footprint-covered{selectedClass}\"{slotStyle}>");
                sb.AppendLine("              <div class=\"covered-stitch\"></div>");
                sb.AppendLine($"              <p class=\"slot-name\">{Esc(ShortLabel(coveredItem.DisplayName))}</p>");
                sb.AppendLine("            </div>");
            }
            else
            {
                sb.AppendLine($"            <div class=\"slot empty{selectedClass}\"{slotStyle}></div>");
            }
        }

        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
    }

    private static void AppendCollectedItem(StringBuilder sb, GameplayUiCollectedItem item)
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
        sb.AppendLine($"            <div class=\"collected-kicker\">{Esc(item.Title)}</div>");
        sb.AppendLine($"            <div class=\"collected-name\">{Esc(item.DisplayName)}{Esc(countSuffix)}</div>");
        sb.AppendLine($"            <div class=\"collected-meta\">{Esc(item.Type)} item</div>");
        sb.AppendLine($"            <div class=\"collected-footprint\">Case slots {item.SlotWidth} x {item.SlotHeight} - Stack {item.MaxStack}</div>");
        if (!string.IsNullOrWhiteSpace(item.Description))
            sb.AppendLine($"            <div class=\"collected-description\">{Esc(item.Description)}</div>");
        sb.AppendLine("            <div class=\"collected-confirm\">E / X: Confirm</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </section>");
    }

    private static void AppendInventory(StringBuilder sb, GameplayUiState state)
    {
        Dictionary<int, GameplayUiInventoryItem> byOriginSlot = state.InventoryItems.ToDictionary(item => item.SlotIndex);
        Dictionary<int, GameplayUiInventoryItem> byCoveredSlot = BuildCoveredSlotLookup(state);
        GameplayUiInventoryItem? selected = byCoveredSlot.GetValueOrDefault(state.SelectedSlot);

        sb.AppendLine("      <section id=\"inventory-panel\" class=\"glass-panel\">");
        sb.AppendLine("        <div class=\"inventory-backplate\"></div>");
        sb.AppendLine("        <div id=\"inventory-header\">");
        sb.AppendLine("          <div>");
        sb.AppendLine("            <p class=\"eyebrow\">Attache Case</p>");
        sb.AppendLine("            <h1>Inventory</h1>");
        sb.AppendLine("          </div>");
        sb.AppendLine($"          <p class=\"slot-count\">{state.UsedSlotCount}/{Math.Max(1, state.GridWidth * state.GridHeight)} occupied</p>");
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
            sb.AppendLine($"          <h2>{Esc(selected.DisplayName)}{Esc(countSuffix)}</h2>");
            sb.AppendLine($"          <p class=\"muted\">{Esc(selected.Type)} | {selected.SlotWidth}x{selected.SlotHeight} slots | Stack {selected.MaxStack}</p>");
            if (!string.IsNullOrWhiteSpace(selected.Description))
                sb.AppendLine($"          <p class=\"description\">{Esc(selected.Description)}</p>");
        }
        else
        {
            sb.AppendLine("          <div class=\"item-preview-card empty-preview\"><div class=\"preview-symbol\">?</div></div>");
            sb.AppendLine("          <h2>No item selected</h2>");
            sb.AppendLine("          <p class=\"muted\">Move across the case with WASD, D-pad, or left stick.</p>");
        }

        if (state.SaveCount > 0)
            sb.AppendLine($"          <p class=\"muted\">Saves used: {state.SaveCount}</p>");

        if (state.UsingInventoryItem)
        {
            if (!string.IsNullOrWhiteSpace(state.UseTargetPrompt))
                sb.AppendLine($"          <p class=\"use-hint\">{Esc(state.UseTargetPrompt)}</p>");

            if (selected?.IsValidUseTarget == true)
                sb.AppendLine("          <p class=\"use-hint good\">This item can be used here. E / X: Use</p>");
            else if (selected?.IsInvalidUseTarget == true)
                sb.AppendLine("          <p class=\"use-hint bad\">This item does not fit this use. Choose a highlighted item.</p>");
            else
                sb.AppendLine("          <p class=\"use-hint\">Select a highlighted item to use.</p>");

            sb.AppendLine("          <p class=\"footer-hint\">I / Back: Cancel use</p>");
        }
        else if (state.CombiningInventoryItem)
        {
            if (selected?.IsValidCombineTarget == true)
            {
                sb.AppendLine("          <p class=\"combine-hint good\">This item can be combined. E / X: Combine</p>");
                if (!string.IsNullOrWhiteSpace(state.CombinePreviewTitle))
                {
                    string resultSuffix = state.CombinePreviewResultCount > 1 ? $" x{state.CombinePreviewResultCount}" : "";
                    sb.AppendLine("          <div class=\"combine-preview-card\">");
                    sb.AppendLine($"            <p class=\"eyebrow\">Recipe</p>");
                    sb.AppendLine($"            <h2>{Esc(state.CombinePreviewTitle)}</h2>");
                    if (!string.IsNullOrWhiteSpace(state.CombinePreviewResultName))
                        sb.AppendLine($"            <p class=\"muted\">Result: {Esc(state.CombinePreviewResultName)}{Esc(resultSuffix)}</p>");
                    if (!string.IsNullOrWhiteSpace(state.CombinePreviewDescription))
                        sb.AppendLine($"            <p class=\"description\">{Esc(state.CombinePreviewDescription)}</p>");
                    sb.AppendLine("          </div>");
                }
            }
            else if (selected?.IsCombineSource == true)
                sb.AppendLine("          <p class=\"combine-hint source\">Combining from this item. Select a highlighted target.</p>");
            else if (selected?.IsInvalidCombineTarget == true)
                sb.AppendLine("          <p class=\"combine-hint bad\">This item cannot combine here. Choose a highlighted item.</p>");
            else
                sb.AppendLine("          <p class=\"combine-hint\">Select a highlighted item to combine.</p>");

            sb.AppendLine("          <p class=\"footer-hint\">I / Back: Cancel combine</p>");
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
            sb.AppendLine($"          <p class=\"muted\">{Esc(moveHint)}</p>");
            string rotationText = state.MovingItemRotated ? "rotated" : "normal";
            sb.AppendLine($"          <p class=\"muted\">Held footprint: {state.MovingItemSlotWidth}x{state.MovingItemSlotHeight} ({Esc(rotationText)})</p>");
            sb.AppendLine("          <p class=\"footer-hint\">E / X: Place | R / Y: Rotate | I / Back: Cancel</p>");
        }
        else
        {
            sb.AppendLine("          <p class=\"footer-hint\">E / X: Actions | R / Y: Rotate | Q / LB: Split | I / Back: Close</p>");
        }
        sb.AppendLine("        </aside>");
        sb.AppendLine("        </div>");
        AppendInventoryOverlays(sb, state);
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

    private static void AppendInventoryOverlays(StringBuilder sb, GameplayUiState state)
    {
        if (state.InventoryActionMenuOpen)
        {
            sb.AppendLine("        <section id=\"inventory-action-menu\" class=\"floating-menu\">");
            sb.AppendLine("          <div class=\"menu-title\">Item Actions</div>");
            IReadOnlyList<string> labels = state.InventoryActionLabels.Count > 0
                ? state.InventoryActionLabels
                : new[] { "Use", "Examine", "Move", "Combine", "Split", "Discard" };
            for (int i = 0; i < labels.Count; i++)
            {
                string selectedClass = i == state.SelectedInventoryActionIndex ? " selected" : "";
                sb.AppendLine($"          <div class=\"action-row{selectedClass}\" style=\"top: {36 + i * 34}px;\">{Esc(labels[i])}</div>");
            }
            sb.AppendLine("          <div class=\"menu-footer\">W/S or D-pad: Select - E / X: Confirm - I / Back: Cancel</div>");
            sb.AppendLine("        </section>");
        }

        if (state.InventorySplitPickerOpen)
        {
            sb.AppendLine("        <section id=\"inventory-split-picker\" class=\"floating-menu\">");
            sb.AppendLine("          <div class=\"menu-title\">Split Stack</div>");
            sb.AppendLine($"          <div class=\"quantity-readout\">x{Math.Max(1, state.InventorySplitAmount)}</div>");
            sb.AppendLine("          <div class=\"menu-footer\">A/D or D-pad: Amount - E / X: Confirm - I / Back: Cancel</div>");
            sb.AppendLine("        </section>");
        }

        if (state.InventoryDiscardConfirmOpen)
        {
            sb.AppendLine("        <section id=\"inventory-discard-confirm\" class=\"floating-menu warning-menu\">");
            sb.AppendLine("          <div class=\"menu-title\">Discard Item?</div>");
            sb.AppendLine("          <div class=\"warning-copy\">Confirm discard</div>");
            sb.AppendLine("          <div class=\"menu-footer\">E / X: Discard - I / Back: Cancel</div>");
            sb.AppendLine("        </section>");
        }
    }

    private static void AppendUseItemPanel(StringBuilder sb, GameplayUiState state)
    {
        List<GameplayUiInventoryItem> candidates = state.InventoryItems
            .Where(item => item.IsUseCandidate)
            .ToList();

        int panelHeight = 150 + Math.Max(1, candidates.Count) * 62;
        sb.AppendLine($"      <section id=\"use-item-panel\" class=\"modal-card\" style=\"height: {panelHeight}px;\">");
        sb.AppendLine("        <div class=\"use-panel-title\">Use Item</div>");
        if (!string.IsNullOrWhiteSpace(state.UseTargetPrompt))
            sb.AppendLine($"        <div class=\"use-panel-prompt\">{Esc(state.UseTargetPrompt)}</div>");

        sb.AppendLine("        <div id=\"use-item-list\">");
        if (candidates.Count == 0)
        {
            sb.AppendLine("          <div class=\"empty-use-row\">No usable items are being carried.</div>");
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
                sb.AppendLine($"            <div class=\"use-item-name\">{Esc(item.DisplayName)}{Esc(countSuffix)}</div>");
                sb.AppendLine($"            <div class=\"use-item-meta\">{Esc(item.Type)} item - {item.SlotWidth}x{item.SlotHeight}</div>");
                sb.AppendLine($"            <div class=\"use-item-validity\">{Esc(validity)}</div>");
                sb.AppendLine("          </div>");
            }
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"use-panel-footer\">W/S or D-pad: Select - E / X: Use - I / Back: Cancel</div>");
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

    private static string Esc(string text)
        => WebUtility.HtmlEncode(text);
}

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

        if (state.InventoryOpen)
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
            sb.AppendLine($"        <p class=\"prompt-action\">E / X: {Esc(state.InteractionPrompt)}</p>");
        if (!string.IsNullOrWhiteSpace(state.GameMessage))
            sb.AppendLine($"        <p class=\"prompt-message\">{Esc(state.GameMessage)}</p>");
        sb.AppendLine("      </section>");
    }

    private static void AppendCollectedItem(StringBuilder sb, GameplayUiCollectedItem item)
    {
        string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
        sb.AppendLine("      <section id=\"collected-panel\">");
        sb.AppendLine($"        <p class=\"eyebrow\">{Esc(item.Title)}</p>");
        sb.AppendLine($"        <h1>{Esc(item.DisplayName)}{Esc(countSuffix)}</h1>");
        sb.AppendLine($"        <p class=\"muted\">{Esc(item.Type)} | {item.SlotWidth}x{item.SlotHeight} slots | Stack {item.MaxStack}</p>");
        sb.AppendLine($"        <p class=\"muted\">Case footprint: {item.SlotWidth} x {item.SlotHeight}</p>");
        if (!string.IsNullOrWhiteSpace(item.Description))
            sb.AppendLine($"        <p class=\"description\">{Esc(item.Description)}</p>");
        sb.AppendLine("        <p class=\"footer-hint\">E / X: Confirm</p>");
        sb.AppendLine("      </section>");
    }

    private static void AppendInventory(StringBuilder sb, GameplayUiState state)
    {
        Dictionary<int, GameplayUiInventoryItem> byOriginSlot = state.InventoryItems.ToDictionary(item => item.SlotIndex);
        Dictionary<int, GameplayUiInventoryItem> byCoveredSlot = BuildCoveredSlotLookup(state);
        GameplayUiInventoryItem? selected = byCoveredSlot.GetValueOrDefault(state.SelectedSlot);

        sb.AppendLine("      <section id=\"inventory-panel\">");
        sb.AppendLine("        <div id=\"inventory-header\">");
        sb.AppendLine("          <div>");
        sb.AppendLine("            <p class=\"eyebrow\">Item Box</p>");
        sb.AppendLine("            <h1>Inventory</h1>");
        sb.AppendLine("          </div>");
        sb.AppendLine($"          <p class=\"slot-count\">Slots: {state.UsedSlotCount}/{Math.Max(1, state.GridWidth * state.GridHeight)}</p>");
        sb.AppendLine("        </div>");
        sb.AppendLine($"        <div id=\"inventory-grid\" class=\"cols-{Math.Max(1, state.GridWidth)}\">");

        int slotCapacity = Math.Max(1, state.GridWidth * state.GridHeight);
        for (int slot = 0; slot < slotCapacity; slot++)
        {
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
                sb.AppendLine($"          <div class=\"slot filled{footprintClass}{rotatedClass}{selectedClass}\" data-slot=\"{slot}\">");
                sb.AppendLine($"            <p class=\"slot-name\">{Esc(ShortLabel(item.DisplayName))}{Esc(countSuffix)}</p>");
                sb.AppendLine($"            <p class=\"slot-footprint\">{item.SlotWidth}x{item.SlotHeight}</p>");
                sb.AppendLine("          </div>");
            }
            else if (covered && coveredItem != null)
            {
                sb.AppendLine($"          <div class=\"slot filled footprint-covered{selectedClass}\" data-slot=\"{slot}\">");
                sb.AppendLine($"            <p class=\"slot-name\">{Esc(ShortLabel(coveredItem.DisplayName))}</p>");
                sb.AppendLine("          </div>");
            }
            else
            {
                sb.AppendLine($"          <div class=\"slot empty{selectedClass}\" data-slot=\"{slot}\"></div>");
            }
        }

        sb.AppendLine("        </div>");
        sb.AppendLine("        <aside id=\"description-panel\">");
        if (selected != null)
        {
            string countSuffix = selected.Count > 1 ? $" x{selected.Count}" : "";
            sb.AppendLine($"          <h2>{Esc(selected.DisplayName)}{Esc(countSuffix)}</h2>");
            sb.AppendLine($"          <p class=\"muted\">{Esc(selected.Type)} | {selected.SlotWidth}x{selected.SlotHeight} slots | Stack {selected.MaxStack}</p>");
            if (!string.IsNullOrWhiteSpace(selected.Description))
                sb.AppendLine($"          <p class=\"description\">{Esc(selected.Description)}</p>");
        }
        else
        {
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
                sb.AppendLine("          <p class=\"combine-hint good\">This item can be combined. E / X: Combine</p>");
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
        sb.AppendLine("      </section>");
    }

    private static void AppendUseItemPanel(StringBuilder sb, GameplayUiState state)
    {
        List<GameplayUiInventoryItem> candidates = state.InventoryItems
            .Where(item => item.IsUseCandidate)
            .ToList();

        sb.AppendLine("      <section id=\"use-item-panel\">");
        sb.AppendLine("        <p class=\"eyebrow\">Use Item</p>");
        if (!string.IsNullOrWhiteSpace(state.UseTargetPrompt))
            sb.AppendLine($"        <p class=\"description\">{Esc(state.UseTargetPrompt)}</p>");

        sb.AppendLine("        <div id=\"use-item-list\">");
        if (candidates.Count == 0)
        {
            sb.AppendLine("          <p class=\"muted\">No usable items are being carried.</p>");
        }
        else
        {
            foreach (GameplayUiInventoryItem item in candidates)
            {
                string selectedClass = item.SlotIndex == state.SelectedSlot ? " selected" : "";
                string validClass = item.IsValidUseTarget ? " valid" : " invalid";
                string countSuffix = item.Count > 1 ? $" x{item.Count}" : "";
                string validity = item.IsValidUseTarget ? "Ready" : "Doesn't fit";
                sb.AppendLine($"          <div class=\"use-item-row{selectedClass}{validClass}\" data-slot=\"{item.SlotIndex}\">");
                sb.AppendLine($"            <span class=\"use-item-name\">{Esc(item.DisplayName)}{Esc(countSuffix)}</span>");
                sb.AppendLine($"            <span class=\"use-item-validity\">{Esc(validity)}</span>");
                sb.AppendLine("          </div>");
            }
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("        <p class=\"footer-hint\">W/S or D-pad: Select | E / X: Use | I / Back: Cancel</p>");
        sb.AppendLine("      </section>");
    }

    private static string ShortLabel(string text)
        => text.Length <= 10 ? text : text[..10];

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

    private static string Esc(string text)
        => WebUtility.HtmlEncode(text);
}

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
            AppendInventory(sb, state);

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
        sb.AppendLine("        <p class=\"eyebrow\">Item Collected</p>");
        sb.AppendLine($"        <h1>{Esc(item.DisplayName)}{Esc(countSuffix)}</h1>");
        sb.AppendLine($"        <p class=\"muted\">{Esc(item.Type)} | {item.SlotWidth}x{item.SlotHeight} slots | Stack {item.MaxStack}</p>");
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

        if (state.MovingInventoryItem)
        {
            string moveHint = state.CanSwapMovingItem
                ? "Release here to swap items."
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
            sb.AppendLine("          <p class=\"footer-hint\">E / X: Move | R / Y: Rotate | I / Back: Close</p>");
        }
        sb.AppendLine("        </aside>");
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

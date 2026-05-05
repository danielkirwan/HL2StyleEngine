namespace Game.Inventory;

public sealed class InventoryCombineRecipe
{
    public InventoryCombineRecipe(
        string firstItemId,
        string secondItemId,
        string resultItemId,
        int resultCount,
        int firstConsumedCount = 1,
        int secondConsumedCount = 1)
    {
        FirstItemId = firstItemId;
        SecondItemId = secondItemId;
        ResultItemId = resultItemId;
        ResultCount = Math.Max(1, resultCount);
        FirstConsumedCount = Math.Max(1, firstConsumedCount);
        SecondConsumedCount = Math.Max(1, secondConsumedCount);
    }

    public string FirstItemId { get; }
    public string SecondItemId { get; }
    public string ResultItemId { get; }
    public int ResultCount { get; }
    public int FirstConsumedCount { get; }
    public int SecondConsumedCount { get; }

    public bool Matches(string firstItemId, string secondItemId, out int firstConsumedCount, out int secondConsumedCount)
    {
        if (string.Equals(firstItemId, FirstItemId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(secondItemId, SecondItemId, StringComparison.OrdinalIgnoreCase))
        {
            firstConsumedCount = FirstConsumedCount;
            secondConsumedCount = SecondConsumedCount;
            return true;
        }

        if (string.Equals(firstItemId, SecondItemId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(secondItemId, FirstItemId, StringComparison.OrdinalIgnoreCase))
        {
            firstConsumedCount = SecondConsumedCount;
            secondConsumedCount = FirstConsumedCount;
            return true;
        }

        firstConsumedCount = 0;
        secondConsumedCount = 0;
        return false;
    }
}

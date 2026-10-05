using System.Numerics;

namespace Engine.UI;

// Both renderers and input tests use the same pixel geometry and canonical slot IDs.
public sealed class InventoryLayout
{
    public int Columns { get; }
    public int PrimaryRows { get; }
    public int OverflowRows { get; }
    public int Cell { get; }
    public Vector2 PrimaryOrigin { get; }
    public Vector2 OverflowOrigin { get; }
    public Vector2 DescriptionOrigin { get; }
    public int DescriptionWidth { get; }
    public bool Horizontal { get; }

    public InventoryLayout(int width, int height, int columns, int rows, int primaryRows)
    {
        Columns = Math.Max(1, columns);
        PrimaryRows = Math.Clamp(primaryRows, 1, rows);
        OverflowRows = Math.Max(0, rows - PrimaryRows);
        bool horizontal = width >= 960 && OverflowRows > 0;
        Horizontal = horizontal;
        int grids = horizontal ? 2 : 1;
        int shownRows = horizontal ? Math.Max(PrimaryRows, OverflowRows) : PrimaryRows + OverflowRows;
        Cell = Math.Max(18, Math.Min(64, Math.Min((width - 64 - (horizontal ? 40 : 0)) / (Columns * grids),
            (height - 210 - (!horizontal && OverflowRows > 0 ? 44 : 0)) / Math.Max(1, shownRows))));
        int totalWidth = grids * Columns * Cell + (horizontal ? 40 : 0);
        int top = Math.Max(48, (height - shownRows * Cell - 150) / 2);
        PrimaryOrigin = new Vector2((width - totalWidth) / 2, top);
        OverflowOrigin = horizontal ? PrimaryOrigin + new Vector2(Columns * Cell + 40, 0)
            : PrimaryOrigin + new Vector2(0, PrimaryRows * Cell + 44);
        DescriptionOrigin = new Vector2(PrimaryOrigin.X, (OverflowRows > 0 ? OverflowOrigin.Y + OverflowRows * Cell
            : PrimaryOrigin.Y + PrimaryRows * Cell) + 22);
        DescriptionWidth = totalWidth;
    }

    public Vector2 SlotOrigin(int slot)
    {
        int row = slot / Columns;
        bool overflow = row >= PrimaryRows;
        return (overflow ? OverflowOrigin : PrimaryOrigin) +
            new Vector2(slot % Columns * Cell, (overflow ? row - PrimaryRows : row) * Cell);
    }
    public Vector2 SlotCenter(int slot) => SlotOrigin(slot) + new Vector2(Cell / 2f);

    public int Navigate(int slot, int dx, int dy)
    {
        int row = slot / Columns, col = slot % Columns;
        if (!Horizontal)
            return Math.Clamp(row + dy, 0, PrimaryRows + OverflowRows - 1) * Columns + Math.Clamp(col + dx, 0, Columns - 1);
        if (row >= PrimaryRows) { row -= PrimaryRows; col += Columns; }
        col = Math.Clamp(col + dx, 0, Columns * 2 - 1);
        row = Math.Clamp(row + dy, 0, (col >= Columns ? OverflowRows : PrimaryRows) - 1);
        return (row + (col >= Columns ? PrimaryRows : 0)) * Columns + col % Columns;
    }
}

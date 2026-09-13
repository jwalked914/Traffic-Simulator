using Godot;
using System;

public static class GridMath
{
    // converts cell coordinates to local position of that cell's top-left corner
    public static Vector2 CellToLocal(Vector2I cell, float cellSize)
    {
        ValidateCellSize(cellSize);

        return new Vector2(cell.X * cellSize, cell.Y * cellSize);
    }

    // converts cell coordinates to local position of that cell's center
    // will be used later for placing objects at a cell's center
    public static Vector2 CellToLocalCenter(Vector2I cell, float cellSize)
    {
        return CellToLocal(cell, cellSize) + Vector2.One * cellSize * 0.5f;
    }

    // finds the cell containing a local position
    public static Vector2I LocalToCell(Vector2 localPosition, float cellSize)
    {
        ValidateCellSize(cellSize);

        return new Vector2I(Mathf.FloorToInt(localPosition.X / cellSize), Mathf.FloorToInt(localPosition.Y / cellSize));
    }

    // returns the local rectangle of one grid cell
    public static Rect2 GetCellRect(Vector2I cell, float cellSize)
    {
        ValidateCellSize(cellSize);

        return new Rect2(CellToLocal(cell, cellSize), Vector2.One * cellSize);
    }

    // returns the local rectangle occupied by a multi-cell object
    // will be used later for buildings, parking lots, etc.
    public static Rect2 GetMultiCellRect(Vector2I anchorCell, Vector2I sizeInCells, float cellSize)
    {
        ValidateCellSize(cellSize);

        if (sizeInCells.X <= 0 || sizeInCells.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInCells), "Multi-cell dimensions must be greater than zero.");
        }

        return new Rect2(CellToLocal(anchorCell, cellSize), new Vector2(sizeInCells.X * cellSize, sizeInCells.Y * cellSize));
    }

    // validates that the cell size is greater than zero
    private static void ValidateCellSize(float cellSize)
    {
        if (cellSize <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be greater than zero.");
        }
    }
}
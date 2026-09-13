using Godot;

public partial class RoadGraph : Node2D
{
    // width / height of each grid cell
    [Export] public float CellSize { get; set; } = 128.0f;

    // converts a cell coordinate to its top-left position in the grid
    public Vector2 CellToLocal(Vector2I cell)
    {
        return GridMath.CellToLocal(cell, CellSize);
    }

    // converts a cell coordinate to its center position in the grid
    // will be used for placing object's at a cell's center
    public Vector2 CellToLocalCenter(Vector2I cell)
    {
        return GridMath.CellToLocalCenter(cell, CellSize);
    }

    // finds the grid cell containing a position in local space
    public Vector2I LocalToCell(Vector2 localPosition)
    {
        return GridMath.LocalToCell(localPosition, CellSize);
    }

    // converts a cell coordinate to a world position
    // will be used for placing objects at a cell's position in the world
    public Vector2 CellToGlobal(Vector2I cell)
    {
        return ToGlobal(CellToLocal(cell));
    }

    // converts a cell coordinate to the world position of its center
    // will be used for placing objects at a cell's center in the world
    public Vector2 CellToGlobalCenter(Vector2I cell)
    {
        return ToGlobal(CellToLocalCenter(cell));
    }

    // finds the grid cell containing a world position
    // this is useful for mouse input
    public Vector2I GlobalToCell(Vector2 globalPosition)
    {
        return LocalToCell(ToLocal(globalPosition));
    }

    // returns the local rectangle of one grid cell
    public Rect2 GetCellLocalRect(Vector2I cell)
    {
        return GridMath.GetCellRect(cell, CellSize);
    }

    // returns the local rectangle occupied by a multi-cell structure
    // will be used for buildings, parking lots, etc.
    public Rect2 GetMultiCellLocalRect(Vector2I anchorCell, Vector2I sizeInCells)
    {
        return GridMath.GetMultiCellRect(anchorCell, sizeInCells, CellSize);
    }
    
    public override void _Ready()
    {
        VerifyCellConversions();
    }

    // verifies that converting to a position and back preserves the original coordinates
    private void VerifyCellConversions()
    {
        Vector2I originalCell = new Vector2I(-2, 3);
        Vector2 localPosition = CellToLocal(originalCell);
        Vector2I convertedBack = LocalToCell(localPosition);

        if (convertedBack != originalCell)
        {
            GD.PushError($"Cell conversion failed: " + $"{originalCell} became {convertedBack} after conversion.");
            return;
        }

        GD.Print("Cell conversion passed successfully.");
    }
}
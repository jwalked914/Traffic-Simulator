using Godot;
using System;

public class ParkingLayout
{
    public const int MaximumCapacity = 8192;
    public Rect2 Lot { get; }
    public Rect2 SideAisle { get; }
    public Vector2 SpaceSize { get; }
    public float AisleWidth { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int Capacity => Columns * Rows;

    private readonly float _cellSize;
    private readonly float _spacesLeft;

    // fits spaces and driving aisles into the parking area below the building
    public ParkingLayout(Vector2I anchor, Vector2I size, int parkingRows, float cellSize, float roadWidth)
    {
        if (parkingRows < 1 || parkingRows >= size.Y)
        {
            throw new ArgumentOutOfRangeException(nameof(parkingRows));
        }

        _cellSize = cellSize;
        roadWidth = Mathf.Clamp(roadWidth, 1, cellSize);
        // spaces have extra room around a car and aisles leave room to turn
        // should be useful for simulating drivers later
        SpaceSize = new Vector2(roadWidth * 0.75f, roadWidth * 1.5f);
        AisleWidth = roadWidth * 1.5f;

        Vector2I lotAnchor = anchor + new Vector2I(0, size.Y - parkingRows);
        Lot = GridMath.GetMultiCellRect(lotAnchor, new Vector2I(size.X, parkingRows), cellSize)
            .Grow(-roadWidth * 0.25f);

        // leave the right side clear so cars can reach every parking row
        float parkingWidth = Mathf.Max(0, Lot.Size.X - AisleWidth);
        Columns = Mathf.Max(0, Mathf.FloorToInt(parkingWidth / SpaceSize.X));
        Rows = Mathf.Max(0, Mathf.FloorToInt(Lot.Size.Y / (SpaceSize.Y + AisleWidth)));
        _spacesLeft = Lot.Position.X + (parkingWidth - Columns * SpaceSize.X) * 0.5f;
        SideAisle = new Rect2(new Vector2(Lot.End.X - AisleWidth, Lot.Position.Y),
            new Vector2(AisleWidth, Lot.Size.Y));
    }

    // counts aisle rows from the road side of the lot
    public Rect2 GetAisleRect(int row)
    {
        if (row < 0 || row >= Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        float bottom = Lot.End.Y - row * (SpaceSize.Y + AisleWidth);
        return new Rect2(new Vector2(Lot.Position.X, bottom - AisleWidth),
            new Vector2(Lot.Size.X, AisleWidth));
    }

    // finds a space by row and column, just above its driving aisle
    public Rect2 GetSpaceRect(int index)
    {
        if (index < 0 || index >= Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int row = index / Columns;
        int column = index % Columns;
        float top = GetAisleRect(row).Position.Y - SpaceSize.Y;
        return new Rect2(new Vector2(_spacesLeft + column * SpaceSize.X, top), SpaceSize);
    }

    // reaches a space through the aisles without crossing other spaces
    public Vector2[] GetEntryPath(int index, Vector2I accessCell)
    {
        Vector2 space = GetSpaceRect(index).GetCenter();
        Vector2 road = GridMath.CellToLocalCenter(accessCell, _cellSize);
        int row = index / Columns;
        float bottomAisle = GetAisleRect(0).GetCenter().Y;
        float targetAisle = GetAisleRect(row).GetCenter().Y;

        if (row == 0)
        {
            return new[] { road, new Vector2(road.X, bottomAisle), new Vector2(space.X, targetAisle), space };
        }

        float sideAisle = SideAisle.GetCenter().X;
        return new[]
        {
            road,
            new Vector2(road.X, bottomAisle),
            new Vector2(sideAisle, bottomAisle),
            new Vector2(sideAisle, targetAisle),
            new Vector2(space.X, targetAisle),
            space
        };
    }

    // follows the same clear aisles back to the road
    public Vector2[] GetExitPath(int index, Vector2I accessCell)
    {
        Vector2[] path = GetEntryPath(index, accessCell);
        Array.Reverse(path);
        return path;
    }
}

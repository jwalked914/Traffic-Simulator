using Godot;
using System.Collections.Generic;

public class RoadJunction
{
	// complex junctions may be larger than just one cell.
	// thus, we need a way to represent and allocate multiple cells
	// for one junction.
	public Vector2I AnchorCell; // top-left cell of junction
	public Vector2I FootprintSize; // # of cells; (1, 1) by default
	
	public List<Lane> Lanes = new();
	
	public RoadJunction(Vector2I anchorCell, Vector2I? footprintSize = null)
	{
		AnchorCell = anchorCell;
		FootprintSize = footprintSize ?? new Vector2I(1, 1);
	}
	
	public Rect2 GetLocalRect(Vector2I cellSize) =>
		GridMath.GetMultiCellRect(AnchorCell, FootprintSize, cellSize.X);

	// premade 4-way template: a horizontal and a vertical road crossing at center.
	// AddPath splits them at the center and connects the lanes automatically.
	public static List<List<Vector2I>> FourWayPaths(Vector2I center, int armLength = 1)
	{
		var horizontal = new List<Vector2I>();
		var vertical = new List<Vector2I>();

		for (int i = -armLength; i <= armLength; i++)
		{
			horizontal.Add(center + new Vector2I(i, 0));
			vertical.Add(center + new Vector2I(0, i));
		}

		return new List<List<Vector2I>> { horizontal, vertical };
	}

	// every cell the 4-way covers, used to check for overlap before placing
	public static HashSet<Vector2I> FourWayCells(Vector2I center, int armLength = 1)
	{
		var cells = new HashSet<Vector2I>();

		foreach (List<Vector2I> path in FourWayPaths(center, armLength))
		{
			cells.UnionWith(path);
		}

		return cells;
	}
}

public class JunctionLane
{
	public LaneID Id;
	public LaneType Type;
	public LaneID FromLane;
	public LaneID ToLane;
	public bool IsBypass;
}

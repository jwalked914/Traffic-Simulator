using Godot;
using System.Collections.Generic;

public class RoadNode
{
	// complex junctions may be larger than just one cell.
	// thus, we need a way to represent and allocate multiple cells
	// for one junction.
	public Vector2I AnchorCell; // top-left cell of junction
	public Vector2I FootprintSize; // # of cells; (1, 1) by default
	
	public List<LaneID> Lanes = new();
	
	public RoadNode(Vector2I anchorCell, Vector2I? footprintSize = null)
	{
		AnchorCell = anchorCell;
		FootprintSize = footprintSize ?? new Vector2I(1, 1);
	}
	
	public Rect2 GetLocalRect(RoadGraph roadGraph) =>
		roadGraph.GetMultiCellLocalRect(AnchorCell, FootprintSize);
}

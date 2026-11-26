using Godot;
using System.Collections.Generic;
using System.Linq;

public enum JunctionTemplateType { FourWay, T }

// premade junction shapes, stored as road paths of offsets around (0, 0).
// placing one rotates the offsets and moves them to the clicked cell;
// AddPath then splits the roads where they meet and connects the lanes.
public class JunctionTemplate
{
	public JunctionTemplateType Type { get; }
	public IReadOnlyList<IReadOnlyList<Vector2I>> PathOffsets { get; }

	private JunctionTemplate(JunctionTemplateType type, List<List<Vector2I>> pathOffsets)
	{
		Type = type;
		PathOffsets = pathOffsets;
	}

	// builds the shape for a template type
	public static JunctionTemplate Get(JunctionTemplateType type, int armLength = 1)
	{
		var paths = new List<List<Vector2I>>
		{
			// every template has a straight road through the center
			Line(-armLength, armLength, new Vector2I(1, 0))
		};

		switch (type)
		{
			case JunctionTemplateType.FourWay:
				paths.Add(Line(-armLength, armLength, new Vector2I(0, 1)));
				break;
			case JunctionTemplateType.T:
				paths.Add(Line(0, armLength, new Vector2I(0, 1))); // one arm from the center, facing down
				break;
		}

		return new JunctionTemplate(type, paths);
	}

	// the template's road paths in grid cells, rotated and centered on a cell
	public List<List<Vector2I>> GetPaths(Vector2I center, int rotationSteps = 0)
	{
		return PathOffsets
			.Select(path => path.Select(offset => center + Rotate(offset, rotationSteps)).ToList())
			.ToList();
	}

	// every cell the template covers
	public HashSet<Vector2I> GetCells(Vector2I center, int rotationSteps = 0)
	{
		return GetPaths(center, rotationSteps).SelectMany(path => path).ToHashSet();
	}

	// rotates an offset 90 degrees counterclockwise on screen per step,
	// the same direction buildings rotate with R
	public static Vector2I Rotate(Vector2I offset, int steps)
	{
		int turns = ((steps % 4) + 4) % 4;
		for (int i = 0; i < turns; i++)
		{
			offset = new Vector2I(offset.Y, -offset.X);
		}
		return offset;
	}

	// offsets from 'from' to 'to' along a direction, one cell at a time
	private static List<Vector2I> Line(int from, int to, Vector2I direction)
	{
		var cells = new List<Vector2I>();
		for (int i = from; i <= to; i++)
		{
			cells.Add(direction * i);
		}
		return cells;
	}
}

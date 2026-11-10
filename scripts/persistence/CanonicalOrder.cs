using Godot;
using System.Collections.Generic;
using System.Linq;

public class CanonicalOrder
{
	public static int CompareCells(Vector2I a, Vector2I b) =>
		a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
	
	public static int ComparePaths(IReadOnlyList<Vector2I> a, IReadOnlyList<Vector2I> b)
	{
		int n = System.Math.Min(a.Count, b.Count);
		for (int i = 0; i < n; ++i)
		{
			int c = CompareCells(a[i], b[i]);
			if (c != 0) return c;
		}
		return a.Count.CompareTo(b.Count);
	}
	
	// A->B and B->A are the same road, so store whichever direction sorts first
	public static List<Vector2I> CanonicalPath(IReadOnlyList<Vector2I> path)
	{
		var forward = path.ToList();
		var reverse = path.Reverse().ToList();
		return ComparePaths(reverse, forward) < 0 ? reverse : forward;
	}
}

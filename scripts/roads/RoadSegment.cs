using System.Collections.Generic;
using Godot;

public class RoadSegment
{
	public Vector2I A;
	public Vector2I B;
	public List<Lane> LanesAtoB = new();
	public List<Lane> LanesBtoA = new();

	public RoadSegment(Vector2I a, Vector2I b)
	{
		A = a;
		B = b;
	}
}

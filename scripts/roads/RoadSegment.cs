using System.Collections.Generic;
using Godot;

public class RoadSegment
{
	public Vector2I A;
	public Vector2I B;

	public List<Vector2I> Path;
	
	public List<Lane> LanesAtoB = new();
	public List<Lane> LanesBtoA = new();

	//speed limit for the road
	public float SpeedLimit { get; set; } = 35.0f;

	public RoadSegment(Vector2I a, Vector2I b, float speedLimit = 35.0f)
	{
		A = a;
		B = b;
		Path = new List<Vector2I> {a, b};
		SpeedLimit = speedLimit;
	}

	public RoadSegment(IReadOnlyList<Vector2I> path, float speedLimit = 35.0f)
	{
		A = path[0];
		B = path[path.Count - 1];
		Path = new List<Vector2I>(path);
		SpeedLimit = speedLimit;
	}
}

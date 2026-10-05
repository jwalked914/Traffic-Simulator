using Godot;
using System.Collections.Generic;
using System.Linq;

// Owns the road topology and keeps it in sync with LaneGraph
public class RoadNetwork
{
	private readonly List<RoadSegment> _segments = new();
	private readonly Dictionary<Vector2I, RoadJunction> _junctions = new();
	private readonly LaneGraph _laneGraph;

	private const int DefaultLanesPerDirection = 1;

	public IReadOnlyList<RoadSegment> Segments => _segments.AsReadOnly();
	public IReadOnlyDictionary<Vector2I, RoadJunction> Junctions => _junctions;

	public RoadNetwork(LaneGraph laneGraph)
	{
		_laneGraph = laneGraph;
	}

	public RoadJunction GetOrCreateJunction(Vector2I cell)
	{
		if (_junctions.TryGetValue(cell, out RoadJunction existing))
		{
			return existing;
		}

		var junction = new RoadJunction(cell);
		_junctions[cell] = junction;
		return junction;
	}

	public RoadJunction GetJunctionAt(Vector2I cell)
	{
		return _junctions.TryGetValue(cell, out RoadJunction junction) ? junction : null;
	}

	// Creates a segment between two cells, allocates its lanes, and wires those lanes
	// into the junctions at the endpoints.
	public RoadSegment AddSegment(Vector2I a, Vector2I b)
		{
			return AddSegment(new List<Vector2I> { a, b });
		}

	public RoadSegment AddSegment(IReadOnlyList<Vector2I> path)
	{
		var segment = new RoadSegment(path);
		CreateLanesForSegment(segment);

		RoadJunction junctionA = GetOrCreateJunction(segment.A);
		RoadJunction junctionB = GetOrCreateJunction(segment.B);

		RegisterSegmentLanesAtJunction(segment, junctionA);
		RegisterSegmentLanesAtJunction(segment, junctionB);

		ConnectThroughLanes(junctionA);
		ConnectThroughLanes(junctionB);
		
		_segments.Add(segment);
		return segment;
	}

	public void RemoveSegment(RoadSegment segment)
	{
		if (!_segments.Remove(segment))
		{
			return;
		}

		foreach (Lane lane in segment.LanesAtoB.Concat(segment.LanesBtoA))
		{
			_laneGraph.RemoveLane(lane.Id);
		}

		RemoveSegmentLanesFromJunction(segment, segment.A);
		RemoveSegmentLanesFromJunction(segment, segment.B);
	}

	private void CreateLanesForSegment(RoadSegment segment)
	{
		for (int i = 0; i < DefaultLanesPerDirection; i++)
		{
			LaneID abID = _laneGraph.CreateLaneID();
			segment.LanesAtoB.Add(new Lane(abID, i, LaneType.Driving, 0.0f));

			LaneID baID = _laneGraph.CreateLaneID();
			segment.LanesBtoA.Add(new Lane(baID, i, LaneType.Driving, 0.0f));
		}
	}

	private void RegisterSegmentLanesAtJunction(RoadSegment segment, RoadJunction junction)
	{
		foreach (Lane lane in segment.LanesAtoB.Concat(segment.LanesBtoA))
		{
			if (!junction.Lanes.Contains(lane))
			{
				junction.Lanes.Add(lane);
			}
		}
	}

	private void RemoveSegmentLanesFromJunction(RoadSegment segment, Vector2I cell)
	{
		RoadJunction junction = GetJunctionAt(cell);

		if (junction == null)
		{
			return;
		}

		foreach (Lane lane in segment.LanesAtoB.Concat(segment.LanesBtoA))
		{
			junction.Lanes.Remove(lane); 
		}

		if (junction.Lanes.Count == 0)
		{
			_junctions.Remove(cell);
		}
	}

	// TODO: replace with proper default junction lane connections
	private void ConnectThroughLanes(RoadJunction junction)
	{
		List<Lane> incoming = junction.Lanes;
		List<Lane> outgoing = junction.Lanes;

		foreach (Lane from in incoming)
		{
			foreach (Lane to in outgoing)
			{
				if (from.Id != to.Id)
				{
					_laneGraph.Connect(from.Id, to.Id);
				}
			}
		}
	}
}

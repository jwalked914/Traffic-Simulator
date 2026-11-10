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

		_segments.Add(segment);
		ConnectThroughLanes(junctionA);
		ConnectThroughLanes(junctionB);
		
		return segment;
	}
	
	// splits a dragged path at junctions and skips road sections already occupied
	public List<RoadSegment> AddPath(IReadOnlyList<Vector2I> path)
	{
		var created = new List<RoadSegment>();
		if (path.Count < 2) return created;

		var seenEdges = new HashSet<(Vector2I, Vector2I)>();
		var connectionsPerCell = new Dictionary<Vector2I, int>();
		var edgeIsNew = new bool[path.Count - 1];
		for (int i = 0; i < edgeIsNew.Length; i++)
		{
			Vector2I from = path[i];
			Vector2I to = path[i + 1];
			bool firstVisit = !seenEdges.Contains((to, from)) && seenEdges.Add((from, to));
			edgeIsNew[i] = firstVisit && !EdgeExists(from, to);
			if (firstVisit)
			{
				connectionsPerCell[from] = connectionsPerCell.GetValueOrDefault(from) + 1;
				connectionsPerCell[to] = connectionsPerCell.GetValueOrDefault(to) + 1;
			}
		}

		for (int i = 0; i < path.Count; i++)
		{
			bool touchesNewEdge = (i > 0 && edgeIsNew[i - 1]) || (i < edgeIsNew.Length && edgeIsNew[i]);
			if (touchesNewEdge) SplitSegmentsAt(path[i]);
		}

		var current = new List<Vector2I> { path[0] };
		for (int i = 0; i < edgeIsNew.Length; i++)
		{
			if (edgeIsNew[i]) current.Add(path[i + 1]);
			bool endPiece = !edgeIsNew[i] || i == edgeIsNew.Length - 1 ||
				IsSegmentEndpoint(path[i + 1]) || path[i + 1] == path[0] ||
				connectionsPerCell[path[i + 1]] > 2;
			if (endPiece)
			{
				if (current.Count > 1) created.Add(AddSegment(current));
				current = new List<Vector2I> { path[i + 1] };
			}
		}
		return created;
	}

	private bool EdgeExists(Vector2I a, Vector2I b)
	{
		foreach (RoadSegment s in _segments)
		{
			for (int i = 1; i < s.Path.Count; i++)
			{
				if ((s.Path[i - 1] == a && s.Path[i] == b) ||
					(s.Path[i - 1] == b && s.Path[i] == a))
				{
					return true;
				}
			}
		}
		return false;
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
	
	private bool IsSegmentEndpoint(Vector2I cell)
	{
		return _segments.Any(s => s.A == cell || s.B == cell);
	}
	
	private bool SegmentExistsBetween(Vector2I a, Vector2I b)
	{
		return _segments.Any(s => s.Path.Count == 2 &&
			((s.A == a && s.B == b) || (s.A == b && s.B == a)));
	}
	
	private void SplitSegmentsAt(Vector2I cell)
	{
		foreach (RoadSegment segment in _segments.ToList())
		{
			int index = -1;
			for (int i = 1; i < segment.Path.Count - 1; ++i)
			{
				if (segment.Path[i] == cell) 
				{
					index = i;
					break;
				}
			}
			if (index < 0) continue;
			
			var first = segment.Path.Take(index + 1).ToList();
			var second = segment.Path.Skip(index).ToList();
			
			RemoveSegment(segment);
			AddSegment(first);
			AddSegment(second);
			return;
		}
	}
	
	public void Clear()
	{
		var laneIDs = _segments
			.SelectMany(s => s.LanesAtoB.Concat(s.LanesBtoA))
			.Select(l => l.Id)
			.ToList();
			
		_laneGraph.RemoveLanes(laneIDs);
		_segments.Clear();
		_junctions.Clear();
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
	
	private void ConnectThroughLanes(RoadJunction junction)
	{
		var incoming = new List<Lane>();
		var outgoing = new List<Lane>();
		foreach (RoadSegment segment in _segments)
		{
			if (segment.A == junction.AnchorCell)
			{
				incoming.AddRange(segment.LanesBtoA);
				outgoing.AddRange(segment.LanesAtoB);
			}
			if (segment.B == junction.AnchorCell)
			{
				incoming.AddRange(segment.LanesAtoB);
				outgoing.AddRange(segment.LanesBtoA);
			}
		}

		foreach (Lane from in incoming)
		{
			foreach (Lane to in outgoing)
			{
				if (from.Id != to.Id)
				{
					if (!_laneGraph.GetOutgoing(from.Id).Any(connection => connection.To == to.Id))
					{
						_laneGraph.Connect(from.Id, to.Id);
					}
				}
			}
		}
	}
}

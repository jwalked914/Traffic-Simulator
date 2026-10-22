using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class LaneGraph : Node
{
	private Dictionary<LaneID, List<LaneConnection>> _outgoing = new();
	
	// implements Linear Congruential Generator for unique lane-ids
	private const ulong Multiplier = 0x5851F42D4C957F2D;
	private const ulong Increment = 0x14057B7EF767814F;
	
	private ulong _lastID = 1;
	
	public LaneID CreateLaneID()
	{
		unchecked {_lastID = _lastID * Multiplier + Increment;}
		return new LaneID(_lastID);
	}

	public void Connect(LaneID from, LaneID to)
	{
		if (!_outgoing.ContainsKey(from)) _outgoing[from] = new();
		_outgoing[from].Add(new LaneConnection(from, to));
	}
	
	public void Clear() => _outgoing.Clear();

	public IEnumerable<LaneConnection> GetOutgoing(LaneID lane) =>
		_outgoing.TryGetValue(lane, out var list) ? list : Enumerable.Empty<LaneConnection>();

	// removes one route while keeping the lane's other connections
	public void Disconnect(LaneID from, LaneID to)
	{
		if (_outgoing.TryGetValue(from, out List<LaneConnection> connections))
		{
			connections.RemoveAll(connection => connection.To == to);
			if (connections.Count == 0)
			{
				_outgoing.Remove(from);
			}
		}
	}

	// removes a group of lanes and incoming connections
	public void RemoveLanes(IEnumerable<LaneID> lanes)
	{
		var removed = new HashSet<LaneID>(lanes);
		foreach (LaneID lane in removed)
		{
			_outgoing.Remove(lane);
		}

		foreach (List<LaneConnection> connections in _outgoing.Values)
		{
			connections.RemoveAll(connection => removed.Contains(connection.To));
		}
	}

	public void RemoveLane(LaneID lane)
	{
		_outgoing.Remove(lane);

		foreach (List<LaneConnection> connections in _outgoing.Values)
		{
			connections.RemoveAll(connection => connection.To == lane);
		}
	}
}

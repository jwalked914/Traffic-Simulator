using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class LaneGraph : Node
{
	private Dictionary<LaneID, List<LaneConnection>> _outgoing = new();
	private int _nextLaneID = 0;

	public LaneID CreateLaneID() => new LaneID(_nextLaneID++);

	public void Connect(LaneID from, LaneID to)
	{
		if (!_outgoing.ContainsKey(from)) _outgoing[from] = new();
		_outgoing[from].Add(new LaneConnection(from, to));
	}

	public IEnumerable<LaneConnection> GetOutgoing(LaneID lane) =>
		_outgoing.TryGetValue(lane, out var list) ? list : Enumerable.Empty<LaneConnection>();

	public void RemoveLane(LaneID lane)
	{
		_outgoing.Remove(lane);

		foreach (List<LaneConnection> connections in _outgoing.Values)
		{
			connections.RemoveAll(connection => connection.To == lane);
		}
	}
}

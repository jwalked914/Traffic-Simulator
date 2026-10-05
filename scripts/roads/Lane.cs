using Godot;

public class Lane
{
	public LaneID Id;
	public int Index;
	public LaneType Type;
	public float SpeedLimit;

	public Lane(LaneID id, int index, LaneType type, float speedLimit)
	{
		Id = id;
		Index = index;
		Type = type;
		SpeedLimit = speedLimit;
	}
}

public readonly struct LaneID
{
	public readonly int Value;
	public LaneID(int value) => Value = value;

	public override bool Equals(object obj) => obj is LaneID other && Value == other.Value;
	public override int GetHashCode() => Value.GetHashCode();
	public static bool operator ==(LaneID a, LaneID b) => a.Value == b.Value;
	public static bool operator !=(LaneID a, LaneID b) => !(a == b);
}

// in case we want to model different lane types later: parking, shoulder, bike lanes, etc.
public enum LaneType {Driving}

public readonly struct LaneConnection
{
	public readonly LaneID From;
	public readonly LaneID To;

	public LaneConnection(LaneID from, LaneID to)
	{
		From = from;
		To = to;
	}
}

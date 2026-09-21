public struct LaneConnection
{
	public LaneID From;
	public LaneID To;

	public LaneConnection(LaneID from, LaneID to)
	{
		From = from;
		To = to;
	}
}

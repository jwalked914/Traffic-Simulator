using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum StructureType
{
	House,
	Shop,
	School
}

public class Structure
{
	public Vector2I AnchorCell { get; }
	public StructureType Type { get; private set; }
	public Vector2I SizeInCells { get; private set; }
	public int ParkingRows { get; private set; }
	public Rect2I Bounds => new Rect2I(AnchorCell, SizeInCells);
	public ParkingLayout Parking { get; private set; }
	public int ParkingCapacity => Parking.Capacity;
	public int ReservedCount => _reservations.Count;
	public RoadSegment AccessRoad { get; internal set; }
	public Vector2I? AccessCell { get; internal set; }
	public bool IsConnected => AccessRoad != null;
	public IReadOnlyList<Lane> ParkingLanes => _parkingLanes.AsReadOnly();
	public IReadOnlyList<Lane> AisleLanes => _aisleLanes.AsReadOnly();
	public Lane EntryLane { get; private set; }
	public Lane ExitLane { get; private set; }
	public event Action ParkingChanged;

	private readonly List<Lane> _parkingLanes = new();
	private readonly List<Lane> _aisleLanes = new();
	private readonly Dictionary<long, int> _reservations = new();
	private readonly HashSet<int> _reservedSpaces = new();
	private readonly float _cellSize;
	private readonly float _roadWidth;
	private int _nextFreeSpace;

	public Structure(Vector2I anchor, StructureType type, Vector2I size, int parkingRows,
		float cellSize = 128.0f, float roadWidth = 32.0f)
	{
		AnchorCell = anchor;
		_cellSize = cellSize;
		_roadWidth = roadWidth;
		Configure(type, size, parkingRows);
	}

	public static bool IsValid(StructureType type, Vector2I size, int parkingRows) =>
		Enum.IsDefined(typeof(StructureType), type) && size.X >= 1 && size.X <= 32 && size.Y >= 2 &&
		size.Y <= 32 && parkingRows >= 1 && parkingRows < size.Y;

	// checks the new layout before changing the building's settings
	internal void Configure(StructureType type, Vector2I size, int parkingRows)
	{
		if (!IsValid(type, size, parkingRows))
		{
			throw new ArgumentException(
				"Use a width between 1 and 32, a height between 2 and 32, and at least one building and parking row.");
		}
		var parking = new ParkingLayout(AnchorCell, size, parkingRows, _cellSize, _roadWidth);
		if (parking.Capacity == 0)
		{
			throw new ArgumentException("The parking area needs room for spaces and a driving aisle.");
		}
		if (parking.Capacity > ParkingLayout.MaximumCapacity)
		{
			throw new ArgumentException("Reduce the parking area for this road scale.");
		}

		Type = type;
		SizeInCells = size;
		ParkingRows = parkingRows;
		Parking = parking;
	}

	public Vector2 GetParkingPosition(int index) => Parking.GetSpaceRect(index).GetCenter();

	public Vector2[] GetParkingEntryPath(int index)
	{
		if (!IsConnected)
		{
			throw new InvalidOperationException("This building has no road access.");
		}

		return Parking.GetEntryPath(index, AccessCell.Value);
	}

	public Vector2[] GetParkingExitPath(int index)
	{
		if (!IsConnected)
		{
			throw new InvalidOperationException("This building has no road access.");
		}

		return Parking.GetExitPath(index, AccessCell.Value);
	}

	// gives each space its own route through the lot's entry and exit aisles
	internal void CreateParkingLanes(LaneGraph graph)
	{
		EntryLane = CreateAisleLane(graph);
		ExitLane = CreateAisleLane(graph);
		for (int row = 0; row < Parking.Rows; row++)
		{
			Lane arrival = CreateAisleLane(graph);
			Lane departure = CreateAisleLane(graph);
			graph.Connect(EntryLane.Id, arrival.Id);
			graph.Connect(departure.Id, ExitLane.Id);

			for (int column = 0; column < Parking.Columns; column++)
			{
				int index = row * Parking.Columns + column;
				var space = new Lane(graph.CreateLaneID(), index, LaneType.Parking, 0);
				_parkingLanes.Add(space);
				graph.Connect(arrival.Id, space.Id);
				graph.Connect(space.Id, departure.Id);
			}
		}
	}

	private Lane CreateAisleLane(LaneGraph graph)
	{
		var lane = new Lane(graph.CreateLaneID(), _aisleLanes.Count, LaneType.ParkingAisle, 0);
		_aisleLanes.Add(lane);
		return lane;
	}

	// removes the lot's lanes and any connections that still point to them
	internal void RemoveParkingLanes(LaneGraph graph)
	{
		graph.RemoveLanes(_parkingLanes.Concat(_aisleLanes).Select(lane => lane.Id));
		_parkingLanes.Clear();
		_aisleLanes.Clear();
		EntryLane = null;
		ExitLane = null;
		_nextFreeSpace = 0;
	}

	public bool IsSpaceReserved(int index) => _reservedSpaces.Contains(index);

	// keeps one space per vehicle while skipping earlier reserved spaces
	public bool TryReserveParking(long vehicleId, out LaneID destination)
	{
		destination = default;
		if (_reservations.TryGetValue(vehicleId, out int existing))
		{
			destination = _parkingLanes[existing].Id;
			return true;
		}

		if (!IsConnected || ReservedCount == ParkingCapacity) return false;

		for (int i = _nextFreeSpace; i < _parkingLanes.Count; i++)
		{
			if (IsSpaceReserved(i)) continue;
			_reservations.Add(vehicleId, i);
			_reservedSpaces.Add(i);
			_nextFreeSpace = i + 1;
			ParkingChanged?.Invoke();
			destination = _parkingLanes[i].Id;
			return true;
		}

		return false;
	}

	// frees the vehicle's space and makes it available for the next search
	public bool ReleaseParking(long vehicleId)
	{
		if (!_reservations.Remove(vehicleId, out int index))
		{
			return false;
		}

		_reservedSpaces.Remove(index);
		_nextFreeSpace = Math.Min(_nextFreeSpace, index);
		ParkingChanged?.Invoke();
		return true;
	}

}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum StructureType
{
	House,
	Shop,
	School,
	Producer,
	Consumer
}

public class Structure
{
	public Vector2I AnchorCell { get; }
	public StructureType Type { get; private set; }
	public Vector2I SizeInCells { get; private set; }
	public int ParkingRows { get; private set; }
	public int DriverCount { get; private set; }
	public int RotationSteps { get; }
	public Vector2I FootprintSize => GetFootprintSize(SizeInCells, RotationSteps);
	public Rect2I Bounds => new Rect2I(AnchorCell, FootprintSize);
	public ParkingLayout Parking { get; private set; }
	public int ParkingCapacity => Parking?.Capacity ?? 0;
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
		float cellSize = 128.0f, float roadWidth = 32.0f, int driverCount = 0, int rotationSteps = 0)
	{
		if (rotationSteps < 0 || rotationSteps > 3) throw new ArgumentOutOfRangeException(nameof(rotationSteps));
		AnchorCell = anchor;
		RotationSteps = rotationSteps;
		_cellSize = cellSize;
		_roadWidth = roadWidth;
		Configure(type, size, parkingRows, driverCount);
	}

	public static int GetDriverLimit(StructureType type) => type switch
	{
		StructureType.House => 4,
		StructureType.Producer => 10000,
		_ => 0
	};

	public static bool IsTrafficEndpoint(StructureType type) =>
		type == StructureType.Producer || type == StructureType.Consumer;

	public static bool HasParking(StructureType type) => type == StructureType.Shop || type == StructureType.School;

	public static Vector2I GetFootprintSize(Vector2I size, int rotationSteps) =>
		rotationSteps % 2 == 0 ? size : new Vector2I(size.Y, size.X);

	public static bool IsValid(StructureType type, Vector2I size, int parkingRows)
	{
		if (IsTrafficEndpoint(type)) return size == Vector2I.One && parkingRows == 0;
		bool parkingValid = HasParking(type) ? parkingRows >= 1 && parkingRows < size.Y : parkingRows == 0;
		return Enum.IsDefined(typeof(StructureType), type) && size.X >= 1 && size.X <= 32 && size.Y >= 1 &&
			size.Y <= 32 && parkingValid;
	}

	// checks the new layout before changing the building's settings
	internal void Configure(StructureType type, Vector2I size, int parkingRows, int driverCount)
	{
		if (driverCount < 0 || driverCount > GetDriverLimit(type))
		{
			throw new ArgumentOutOfRangeException(nameof(driverCount));
		}
		if (!IsValid(type, size, parkingRows))
		{
			throw new ArgumentException(
				"Use valid building dimensions.");
		}
		ParkingLayout parking = null;
		if (HasParking(type))
		{
			parking = new ParkingLayout(AnchorCell, size, parkingRows, _cellSize, _roadWidth);
			if (parking.Capacity == 0)
			{
				throw new ArgumentException("The parking area needs room for spaces and a driving aisle.");
			}
			if (parking.Capacity > ParkingLayout.MaximumCapacity)
			{
				throw new ArgumentException("Reduce the parking area for this road scale.");
			}
		}

		Type = type;
		SizeInCells = size;
		ParkingRows = parkingRows;
		DriverCount = driverCount;
		Parking = parking;
	}

	public Vector2 GetParkingPosition(int index) => GetPlacementTransform() * Parking.GetSpaceRect(index).GetCenter();

	public Vector2 GetTrafficEndpointPosition() => GetPlacementTransform() * GridMath.CellToLocalCenter(AnchorCell, _cellSize);

	public Transform2D GetPlacementTransform()
		=> GetPlacementTransform(AnchorCell, SizeInCells, _cellSize, RotationSteps);

	public static Transform2D GetPlacementTransform(Vector2I anchor, Vector2I size, float cellSize, int rotationSteps)
	{
		Vector2 origin = GridMath.CellToLocal(anchor, cellSize);
		Vector2 center = origin + (Vector2)size * cellSize * 0.5f;
		Vector2 placedCenter = origin + (Vector2)GetFootprintSize(size, rotationSteps) * cellSize * 0.5f;
		return new Transform2D(-rotationSteps * Mathf.Pi * 0.5f, placedCenter).TranslatedLocal(-center);
	}

	public Vector2 GetRoadFacingDirection() => GetPlacementTransform().BasisXform(Vector2.Down);

	public Vector2 GetTrafficOpeningPosition() =>
		GridMath.CellToLocalCenter(AccessCell.Value, _cellSize) - GetRoadFacingDirection() * _roadWidth * 0.5f;

	public IEnumerable<Vector2I> GetRoadAccessCells()
	{
		Transform2D transform = GetPlacementTransform();
		for (int x = 0; x < SizeInCells.X; x++)
		{
			Vector2I cell = AnchorCell + new Vector2I(x, SizeInCells.Y);
			yield return GridMath.LocalToCell(transform * GridMath.CellToLocalCenter(cell, _cellSize), _cellSize);
		}
	}

	private Vector2I GetOriginalAccessCell() => GridMath.LocalToCell(
		GetPlacementTransform().AffineInverse() * GridMath.CellToLocalCenter(AccessCell.Value, _cellSize), _cellSize);

	private Vector2[] RotatePath(Vector2[] path)
	{
		Transform2D transform = GetPlacementTransform();
		for (int i = 0; i < path.Length; i++) path[i] = transform * path[i];
		return path;
	}

	public Vector2[] GetParkingEntryPath(int index)
	{
		if (!IsConnected)
		{
			throw new InvalidOperationException("This building has no road access.");
		}

		return RotatePath(Parking.GetEntryPath(index, GetOriginalAccessCell()));
	}

	public Vector2[] GetParkingExitPath(int index)
	{
		if (!IsConnected)
		{
			throw new InvalidOperationException("This building has no road access.");
		}

		return RotatePath(Parking.GetExitPath(index, GetOriginalAccessCell()));
	}

	// creates access lanes and parking routes where needed
	internal void CreateParkingLanes(LaneGraph graph)
	{
		EntryLane = CreateAisleLane(graph);
		ExitLane = CreateAisleLane(graph);
		if (Parking == null) return;
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
		LaneType laneType = IsTrafficEndpoint(Type) ? LaneType.Driving : LaneType.ParkingAisle;
		var lane = new Lane(graph.CreateLaneID(), _aisleLanes.Count, laneType, 0);
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
	
	public void ClearParkingReservations()
	{
		_reservations.Clear();
		_reservedSpaces.Clear();
		_nextFreeSpace = 0;
		ParkingChanged?.Invoke();
	}

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

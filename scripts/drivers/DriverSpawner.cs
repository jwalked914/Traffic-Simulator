using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class DriverSpawner : Node2D
{
	public bool IsRunning { get; private set; }
	public int SpawnedCount { get; private set; }
	public int ConsumedCount { get; private set; }
	public int ReturnedCount { get; private set; }
	public int ActiveCount => _drivers.Count;
	public double ElapsedSeconds { get; private set; }
	public event Action StateChanged;

	private RoadGraph _roads;
	private LaneGraph _lanes;
	private StructureGraph _structures;
	[Export] public double VisitMinutes { get; set; } = 10.0;

	private SimClock _clock;
	private double _duration;
	private readonly HashSet<Driver> _drivers = new();
	private readonly List<SpawnPlan> _plans = new();

	private sealed record RoadRoute(LaneID[] Lanes, Vector2[] Points, float Distance, float[] SpeedLimits);
	private sealed record DriverTrip(Structure Destination, RoadRoute Outbound, RoadRoute ReturnRoute = null);

	private sealed class ResidentTrip
	{
		public Structure Home;
		public DriverTrip Trip;
		public int Space = -1;
		public double? LeaveAt;
		public bool Returning;
		public int LotEntranceWaypoint;
		public int LotExitWaypoint;
		public Vector2[] Maneuver;
		public int FirstManeuverWaypoint = -1;
	}

	private readonly Dictionary<Driver, ResidentTrip> _residents = new();

	private sealed class SpawnPlan
	{
		public Structure Source;
		public DriverTrip Trip;
		public int Spawned;
	}

	public override void _Ready()
	{
		_roads = GetNode<RoadGraph>("../RoadGraph");
		_lanes = GetNode<LaneGraph>("../LaneGraph");
		_structures = GetNode<StructureGraph>("../StructureGraph");
		_clock = GetNode<SimClock>("../SimClock");
	}

	// find usable routes once
	// this will run before drivers are spawned
	public bool TryStart(double durationSeconds, out string error)
	{
		error = "";
		var consumers = _structures.Structures.Where(s => s.Type == StructureType.Consumer && s.IsConnected).ToList();
		var buildings = _structures.Structures.Where(s => s.IsConnected && s.ParkingCapacity > 0).ToList();

		_plans.Clear();
		var roadLanes = new Dictionary<LaneID, (RoadSegment Segment, bool Reverse)>();
		var laneLengths = new Dictionary<LaneID, float>();
		foreach (RoadSegment segment in _roads.Segments)
		{
			float length = 0;
			for (int i = 1; i < segment.Path.Count; i++)
			{
				length += _roads.CellToGlobalCenter(segment.Path[i - 1]).DistanceTo(_roads.CellToGlobalCenter(segment.Path[i]));
			}
			foreach (Lane lane in segment.LanesAtoB)
			{
				roadLanes[lane.Id] = (segment, false);
				laneLengths[lane.Id] = length;
			}
			foreach (Lane lane in segment.LanesBtoA)
			{
				roadLanes[lane.Id] = (segment, true);
				laneLengths[lane.Id] = length;
			}
		}

		foreach (Structure source in _structures.Structures)
		{
			if (source.DriverCount == 0) continue;

			bool resident = source.Type == StructureType.House;
			var trips = new List<DriverTrip>();
			if (source.IsConnected)
			{
				foreach (Structure destination in resident ? buildings : consumers)
				{
					RoadRoute outbound = FindRoute(source, destination, roadLanes, laneLengths);
					if (outbound == null) continue;
					RoadRoute returnRoute = resident ? FindRoute(destination, source, roadLanes, laneLengths) : null;
					if (resident && returnRoute == null) continue;
					trips.Add(new DriverTrip(destination, outbound, returnRoute));
				}
			}

			if (trips.Count == 0)
			{
				if (resident) continue;
				_plans.Clear();
				error = consumers.Count == 0
					? "Connect at least one consumer to a road."
					: $"Connect {source.Type} at {source.AnchorCell} to a reachable consumer.";
				return false;
			}

			_plans.Add(new SpawnPlan { Source = source, Trip = trips.MinBy(trip => trip.Outbound.Distance) });
		}

		if (_plans.Count == 0)
		{
			error = "Set a driver count on a producer or connect a house with drivers to a reachable shop or school.";
			return false;
		}

		SpawnedCount = 0;
		ConsumedCount = 0;
		ReturnedCount = 0;
		ElapsedSeconds = 0;
		_duration = durationSeconds;
		IsRunning = true;
		StateChanged?.Invoke();
		return true;
	}

	// spread each source's total across the run, then let the last drivers finish
	// might need to be changed when we implement the actual simulation, but this works for testing
	public override void _Process(double delta)
	{
		if (!IsRunning) return;
		ElapsedSeconds = Math.Min(_duration, ElapsedSeconds + delta);
		UpdateResidents();
		ReserveParkingForArrivals(waiting: true);
		ReserveParkingForArrivals(waiting: false);
		StartParkingManeuvers();
		bool spawned = false;
		foreach (SpawnPlan plan in _plans)
		{
			while (plan.Spawned < plan.Source.DriverCount &&
				ElapsedSeconds >= plan.Spawned * _duration / plan.Source.DriverCount)
			{
				//wait if another vehicle is blocking spawn
				if (!SpawnDriver(plan))
				{
					break;
				}
				spawned = true;
			}
		}
		if (spawned) StateChanged?.Invoke();
	}

	private bool SpawnDriver(SpawnPlan plan)
	{
		Vector2 sourceOpening = GetOpening(plan.Source);
		Vector2 sourceDirection = GetFacingDirection(plan.Source);
		bool resident = plan.Source.Type == StructureType.House;
		DriverTrip trip = plan.Trip;
		if (resident && !IsHomeClear(plan.Source, departing: true)) return false;

		Vector2 sourceMouth = ProjectToOpening(trip.Outbound.Points[0], sourceOpening, sourceDirection);
		if (resident) sourceMouth = GetHomeMouth(sourceOpening, sourceDirection, departing: true);
		Vector2 spawnPosition = sourceMouth - sourceDirection * _roads.RoadWidth;
		foreach (Driver existingDriver in _drivers)
		{
			if (existingDriver.GlobalPosition.DistanceTo(spawnPosition) < 34.0f) return false;
		}

		var points = new List<Vector2> { spawnPosition };
		if (resident) points.Add(GetHomeApproach(sourceMouth, sourceDirection));
		var speedLimits = Enumerable.Repeat(trip.Outbound.SpeedLimits[0], points.Count).ToList();
		points.AddRange(trip.Outbound.Points);
		speedLimits.AddRange(trip.Outbound.SpeedLimits);
		int lotEntranceWaypoint = points.Count - 1;
		Vector2 destinationOpening = default;
		Vector2 destinationDirection = default;
		if (resident)
		{
			Vector2[] approach = trip.Destination.GetParkingApproachPath();
			points.AddRange(approach.Select(_structures.ToGlobal));
			speedLimits.AddRange(Enumerable.Repeat(35.0f, approach.Length));
		}
		else
		{
			destinationOpening = GetOpening(trip.Destination);
			destinationDirection = GetFacingDirection(trip.Destination);
			Vector2 mouth = ProjectToOpening(points[^1], destinationOpening, destinationDirection);
			points.Add(mouth - destinationDirection * _roads.RoadWidth);
			speedLimits.Add(trip.Outbound.SpeedLimits[^1]);
		}

		var driver = new Driver
		{
			Name = $"Driver{SpawnedCount + 1}",
			DriverId = SpawnedCount + 1,
			SourceLane = plan.Source.ExitLane.Id,
			DestinationLane = trip.Destination.EntryLane.Id
		};
		driver.Route.AddRange(trip.Outbound.Lanes);
		if (resident)
		{
			_residents.Add(driver, new ResidentTrip { Home = plan.Source, Trip = trip, LotEntranceWaypoint = lotEntranceWaypoint });
		}
		driver.Arrived += OnDriverArrived;
		//give driver access to other cars
		driver.SetOtherDrivers(_drivers);
		_drivers.Add(driver);
		AddChild(driver);
		driver.SetWaypoints(points, speedLimits);
		driver.SetTunnelOpenings(sourceOpening, sourceDirection, destinationOpening, destinationDirection);
		plan.Spawned++;
		SpawnedCount++;
		return true;
	}

	// finds the shortest road route and keeps its speed limits
	private RoadRoute FindRoute(Structure source, Structure destination,
		Dictionary<LaneID, (RoadSegment Segment, bool Reverse)> roadLanes, Dictionary<LaneID, float> laneLengths)
	{
		var probe = new Driver { SourceLane = source.ExitLane.Id, DestinationLane = destination.EntryLane.Id };
		bool found = probe.CalculateRoute(_lanes, laneLength: lane => laneLengths.GetValueOrDefault(lane));
		LaneID[] route = probe.Route.ToArray();
		probe.Free();
		if (!found) return null;

		var speedLimits = new List<float>();
		Vector2[] points = BuildRoadPoints(route, roadLanes, speedLimits);
		return points.Length < 2 ? null : new RoadRoute(route, points,
			route.Sum(lane => laneLengths.GetValueOrDefault(lane)), speedLimits.ToArray());
	}

	// uses the front edge for houses and the tunnel mouth for endpoints
	private Vector2 GetOpening(Structure structure)
	{
		Vector2 opening = structure.GetTrafficOpeningPosition();
		if (structure.Type == StructureType.House)
		{
			opening -= structure.GetRoadFacingDirection() * (_roads.CellSize * 0.5f - _roads.RoadWidth * 0.5f);
		}
		return _structures.ToGlobal(opening);
	}

	// gets the direction the entrance faces in world space
	private Vector2 GetFacingDirection(Structure structure) =>
		_structures.GlobalTransform.BasisXform(structure.GetRoadFacingDirection()).Normalized();

	// separates arriving and departing cars
	private Vector2 GetHomeMouth(Vector2 opening, Vector2 facing, bool departing)
	{
		Vector2 side = new Vector2(-facing.Y, facing.X);
		return opening + side * (_roads.RoadWidth * (departing ? 0.25f : -0.25f));
	}

	// marks where the house driveway meets the road
	private Vector2 GetHomeApproach(Vector2 mouth, Vector2 facing) =>
		mouth + facing * (_roads.CellSize * 0.5f - _roads.RoadWidth * 0.5f);

	// gives returning cars room to reach a shared house entrance, as in if there is an exiting car in the driveway
	// and a returning car coming at the same time, this should avoid weird collisions
	private bool IsHomeClear(Structure home, bool departing)
	{
		foreach (var entry in _residents)
		{
			if (entry.Value.Home.AccessCell != home.AccessCell) continue;
			if (departing && entry.Value.Returning) return false;
			if (!departing && !entry.Value.Returning && entry.Key.IsMoving && entry.Key.NextWaypoint <= 2) return false;
		}
		return true;
	}

	// lines up the opening with the car's road lane
	private static Vector2 ProjectToOpening(Vector2 point, Vector2 opening, Vector2 direction) =>
		point - direction * (point - opening).Dot(direction);

	// checks the planned path against the parts of other maneuvers still ahead
	private bool HasManeuverConflict(ResidentTrip visit, Vector2[] path, bool departing)
	{
		foreach (var entry in _residents)
		{
			ResidentTrip other = entry.Value;
			if (other == visit || other.Trip.Destination != visit.Trip.Destination ||
				other.Maneuver == null || (!departing && !other.Returning)) continue;

			bool started = other.FirstManeuverWaypoint >= 0 && entry.Key.NextWaypoint >= other.FirstManeuverWaypoint;
			int next = started ? entry.Key.NextWaypoint - other.FirstManeuverWaypoint + 1 : 1;
			Vector2 start = started ? entry.Key.GlobalPosition : other.Maneuver[0];
			if (PathsOverlap(path, other.Maneuver, next, start)) return true;
		}
		return false;
	}

	// checks whether two cars need the same space along their paths
	internal static bool PathsOverlap(Vector2[] path, Vector2[] other, int next, Vector2 start)
	{
		for (int i = next; i < other.Length; i++)
		{
			Rect2 occupied = GetSweptBounds(start, other[i]);
			for (int j = 1; j < path.Length; j++)
			{
				Rect2 crossing = GetSweptBounds(path[j - 1], path[j]);
				if (occupied.Intersects(crossing)) return true;
			}
			start = other[i];
		}
		return false;
	}

	// includes the car's size when checking a stretch of its path
	private static Rect2 GetSweptBounds(Vector2 from, Vector2 to)
	{
		Vector2 direction = (to - from).Normalized().Abs();
		Vector2 padding = direction * CarVisual.HalfLength + new Vector2(direction.Y, direction.X) * CarVisual.HalfWidth;
		Rect2 segment = new Rect2(from, Vector2.Zero).Expand(to);
		return new Rect2(segment.Position - padding, segment.Size + padding * 2);
	}

	// starts return trips when clear and frees spaces once cars leave the lot
	private void UpdateResidents()
	{
		foreach (var entry in _residents)
		{
			Driver driver = entry.Key;
			ResidentTrip visit = entry.Value;
			Structure lot = visit.Trip.Destination;
			if (visit.Returning && visit.Space >= 0 && driver.NextWaypoint >= visit.LotExitWaypoint)
			{
				lot.ReleaseParking(driver.DriverId);
				visit.Space = -1;
				visit.Maneuver = null;
			}

			if (!visit.Returning && visit.LeaveAt <= _clock.SimTime && IsHomeClear(visit.Home, departing: false))
			{
				Vector2[] exit = lot.GetParkingExitPath(visit.Space).Select(_structures.ToGlobal).ToArray();
				if (!HasManeuverConflict(visit, exit, departing: true)) StartReturn(driver, visit, exit);
			}
		}
	}

	// gives queued drivers first claim before reserving spaces for arriving cars
	private void ReserveParkingForArrivals(bool waiting)
	{
		foreach (var entry in _residents)
		{
			Driver driver = entry.Key;
			ResidentTrip visit = entry.Value;
			if (visit.Returning || visit.Space >= 0 || driver.IsWaitingInLane != waiting ||
				driver.NextWaypoint < visit.LotEntranceWaypoint) continue;

			Structure lot = visit.Trip.Destination;
			if (lot.ReservedCount >= lot.ParkingCapacity ||
				HasArrivalAhead(driver, lot) || !lot.TryReserveParking(driver.DriverId, out _)) continue;

			visit.Space = lot.GetReservedSpace(driver.DriverId);
			visit.Maneuver = [_structures.ToGlobal(lot.GetParkingApproachPath()[^1]),
				.. lot.GetParkingPathFromQueue(visit.Space).Select(_structures.ToGlobal)];
		}
	}

	// holds a reserved car at the queue until its path to the stall is clear
	private void StartParkingManeuvers()
	{
		foreach (var entry in _residents)
		{
			Driver driver = entry.Key;
			ResidentTrip visit = entry.Value;
			if (visit.Maneuver == null || visit.FirstManeuverWaypoint >= 0 ||
				HasManeuverConflict(visit, visit.Maneuver, departing: false)) continue;

			visit.FirstManeuverWaypoint = driver.WaypointCount;
			driver.IsWaitingInLane = false;
			driver.ContinueWaypoints(visit.Maneuver.Skip(1));
		}
	}

	// keeps a car from taking the last space behind a driver in the same queue
	private bool HasArrivalAhead(Driver driver, Structure lot)
	{
		float distance = driver.GetRemainingRouteDistance();
		foreach (var entry in _residents)
		{
			Driver other = entry.Key;
			ResidentTrip visit = entry.Value;
			if (other == driver || visit.Trip.Destination != lot || visit.Returning || visit.Space >= 0 ||
				other.NextWaypoint < visit.LotEntranceWaypoint) continue;

			float otherDistance = other.GetRemainingRouteDistance();
			if (otherDistance < distance || (otherDistance == distance && other.DriverId < driver.DriverId)) return true;
		}
		return false;
	}

	// leaves the reserved space through the lot before heading home
	private void StartReturn(Driver driver, ResidentTrip visit, Vector2[] exit)
	{
		Structure lot = visit.Trip.Destination;
		RoadRoute route = visit.Trip.ReturnRoute;
		var points = new List<Vector2>(exit);
		points.AddRange(route.Points);
		Vector2 opening = GetOpening(visit.Home);
		Vector2 facing = GetFacingDirection(visit.Home);
		Vector2 homeMouth = GetHomeMouth(opening, facing, departing: false);
		points.Add(GetHomeApproach(homeMouth, facing));
		points.Add(homeMouth - facing * _roads.RoadWidth);

		visit.Returning = true;
		visit.Maneuver = exit;
		visit.FirstManeuverWaypoint = 1;
		visit.LotExitWaypoint = exit.Length;
		driver.SourceLane = lot.ExitLane.Id;
		driver.DestinationLane = visit.Home.EntryLane.Id;
		driver.Route.Clear();
		driver.Route.AddRange(route.Lanes);
		var speedLimits = Enumerable.Repeat(35.0f, exit.Length).ToList();
		speedLimits.AddRange(route.SpeedLimits);
		speedLimits.AddRange(Enumerable.Repeat(route.SpeedLimits[^1], 2));
		driver.SetWaypoints(points, speedLimits, backOut: true);
		driver.SetTunnelOpenings(default, default, opening, facing);
	}

	// use the path in each road lane's travel direction
	private Vector2[] BuildRoadPoints(IReadOnlyList<LaneID> route,
		Dictionary<LaneID, (RoadSegment Segment, bool Reverse)> roadLanes, List<float> speedLimits)
	{
		var centers = new List<Vector2>();
		foreach (LaneID lane in route)
		{
			if (!roadLanes.TryGetValue(lane, out var road)) continue;
			IEnumerable<Vector2I> cells = road.Segment.Path;
			if (road.Reverse) cells = cells.Reverse();
			foreach (Vector2I cell in cells)
			{
				Vector2 center = _roads.CellToGlobalCenter(cell);
				// share one corner between the incoming and outgoing lanes
				if (centers.Count > 0 && centers[^1] == center) continue;
				centers.Add(center);
				speedLimits.Add(road.Segment.SpeedLimit);
			}
		}
		return Driver.CreateLaneWaypoints(centers, _roads.RoadWidth * 0.25f).ToArray();
	}

	// parks a visiting resident or removes a driver whose trip is done
	private void OnDriverArrived(Driver driver)
	{
		if (_residents.TryGetValue(driver, out ResidentTrip visit))
		{
			if (!visit.Returning)
			{
				if (visit.FirstManeuverWaypoint < 0)
				{
					driver.IsWaitingInLane = true;
				}
				else
				{
					visit.Maneuver = null;
					visit.LeaveAt = _clock.SimTime + Math.Max(0, VisitMinutes) * 60;
				}
				StateChanged?.Invoke();
				return;
			}

			if (visit.Space >= 0) visit.Trip.Destination.ReleaseParking(driver.DriverId);
			_residents.Remove(driver);
			ReturnedCount++;
		}
		else
		{
			ConsumedCount++;
		}

		_drivers.Remove(driver);
		driver.Arrived -= OnDriverArrived;
		RemoveChild(driver);
		driver.QueueFree();
		if (_drivers.Count == 0 && _plans.All(plan => plan.Spawned == plan.Source.DriverCount))
		{
			_plans.Clear();
			IsRunning = false;
			ClearParking();
		}
		StateChanged?.Invoke();
	}

	// remove active drivers before allowing the map to be edited again
	public void Reset()
	{
		foreach (Driver driver in _drivers)
		{
			driver.Arrived -= OnDriverArrived;
			RemoveChild(driver);
			driver.QueueFree();
		}
		_drivers.Clear();
		_residents.Clear();
		_plans.Clear();
		ClearParking();
		SpawnedCount = 0;
		ConsumedCount = 0;
		ReturnedCount = 0;
		ElapsedSeconds = 0;
		IsRunning = false;
		StateChanged?.Invoke();
	}

	private void ClearParking()
	{
		foreach (Structure structure in _structures.Structures) structure.ClearParkingReservations();
	}
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class DriverSpawner : Node2D
{
    public bool IsRunning { get; private set; }
    public int SpawnedCount { get; private set; }
    public int ConsumedCount { get; private set; }
    public int ActiveCount => _drivers.Count;
    public double ElapsedSeconds { get; private set; }
    public event Action StateChanged;

    private RoadGraph _roads;
    private LaneGraph _lanes;
    private StructureGraph _structures;
    private double _duration;
    private readonly HashSet<Driver> _drivers = new();
    private readonly List<SpawnPlan> _plans = new();

    private sealed record DriverTrip(Structure Consumer, LaneID[] Lanes, Vector2[] RoadPoints);

    private sealed class SpawnPlan
    {
        public Structure Source;
        public readonly List<DriverTrip> Trips = new();
        public int Spawned;
    }

    public override void _Ready()
    {
        _roads = GetNode<RoadGraph>("../RoadGraph");
        _lanes = GetNode<LaneGraph>("../LaneGraph");
        _structures = GetNode<StructureGraph>("../StructureGraph");
    }

    // find usable routes once
    // this will run before drivers are spawned
    public bool TryStart(double durationSeconds, out string error)
    {
        error = "";
        var consumers = _structures.Structures.Where(s => s.Type == StructureType.Consumer && s.IsConnected).ToList();
        if (consumers.Count == 0)
        {
            error = "Connect at least one consumer to a road.";
            return false;
        }

        _plans.Clear();
        var roadLanes = new Dictionary<LaneID, (RoadSegment Segment, bool Reverse)>();
        foreach (RoadSegment segment in _roads.Segments)
        {
            foreach (Lane lane in segment.LanesAtoB) roadLanes[lane.Id] = (segment, false);
            foreach (Lane lane in segment.LanesBtoA) roadLanes[lane.Id] = (segment, true);
        }

        // route producer drivers to consumers
        foreach (Structure source in _structures.Structures.Where(s => s.Type == StructureType.Producer && s.DriverCount > 0))
        {
            var plan = new SpawnPlan { Source = source };
            if (source.IsConnected)
            {
                foreach (Structure consumer in consumers)
                {
                    var probe = new Driver { SourceLane = source.ExitLane.Id, DestinationLane = consumer.EntryLane.Id };
                    bool foundRoute = probe.CalculateRoute(_lanes);
                    LaneID[] route = probe.Route.ToArray();
                    probe.Free();
                    if (!foundRoute) continue;
                    Vector2[] points = BuildRoadPoints(route, roadLanes);
                    if (points.Length < 2) continue;
                    plan.Trips.Add(new DriverTrip(consumer, route, points));
                }
            }

            if (plan.Trips.Count == 0)
            {
                _plans.Clear();
                error = $"Connect {source.Type} at {source.AnchorCell} to a reachable consumer.";
                return false;
            }
            _plans.Add(plan);
        }

        if (_plans.Count == 0)
        {
            error = "Set a driver count on a producer first.";
            return false;
        }

        SpawnedCount = 0;
        ConsumedCount = 0;
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
        DriverTrip trip = plan.Trips[plan.Spawned % plan.Trips.Count];
        Vector2 sourceOpening = _structures.ToGlobal(plan.Source.GetTrafficOpeningPosition());
        Vector2 consumerOpening = _structures.ToGlobal(trip.Consumer.GetTrafficOpeningPosition());
        Vector2 sourceDirection = _structures.GlobalTransform.BasisXform(plan.Source.GetRoadFacingDirection()).Normalized();
        Vector2 consumerDirection = _structures.GlobalTransform.BasisXform(trip.Consumer.GetRoadFacingDirection()).Normalized();
        Vector2 sourceMouth = trip.RoadPoints[0] - sourceDirection * (trip.RoadPoints[0] - sourceOpening).Dot(sourceDirection);
        Vector2 consumerMouth = trip.RoadPoints[^1] - consumerDirection * (trip.RoadPoints[^1] - consumerOpening).Dot(consumerDirection);
        var points = new List<Vector2>();
        points.Add(sourceMouth - sourceDirection * _roads.RoadWidth);
        points.AddRange(trip.RoadPoints);
        points.Add(consumerMouth - consumerDirection * _roads.RoadWidth);

        // check if car is too close to spawn position
        Vector2 spawnPosition = points[0];

        foreach (Driver existingDriver in _drivers)
        {
            if (existingDriver.GlobalPosition.DistanceTo(spawnPosition) < 34.0f)
            {
                return false;
            }
        }

        var driver = new Driver
        {
            Name = $"Driver{SpawnedCount + 1}",
            DriverId = SpawnedCount + 1,
            SourceLane = plan.Source.ExitLane.Id,
            DestinationLane = trip.Consumer.EntryLane.Id
        };
        driver.Route.AddRange(trip.Lanes);
        driver.Arrived += OnDriverArrived;
        //give driver access to other cars
        driver.SetOtherDrivers(_drivers);
        _drivers.Add(driver);
        AddChild(driver);
        driver.SetWaypoints(points);
        driver.SetTunnelOpenings(sourceOpening, sourceDirection, consumerOpening, consumerDirection);
        plan.Spawned++;
        SpawnedCount++;
        return true;
    }

    // use the path in each road lane's travel direction
    private Vector2[] BuildRoadPoints(IReadOnlyList<LaneID> route,
        Dictionary<LaneID, (RoadSegment Segment, bool Reverse)> roadLanes)
    {
        var points = new List<Vector2>();
        foreach (LaneID lane in route)
        {
            if (!roadLanes.TryGetValue(lane, out var road)) continue;
            IEnumerable<Vector2I> cells = road.Segment.Path;
            if (road.Reverse) cells = cells.Reverse();
            Vector2[] centers = cells.Select(_roads.CellToGlobalCenter).ToArray();
            points.AddRange(Driver.CreateLaneWaypoints(centers, _roads.RoadWidth * 0.25f));
        }
        return points.ToArray();
    }

    // remove a driver when it reaches the consumer
    private void OnDriverArrived(Driver driver)
    {
        _drivers.Remove(driver);
        driver.Arrived -= OnDriverArrived;
        RemoveChild(driver);
        driver.QueueFree();
        ConsumedCount++;
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
        _plans.Clear();
        ClearParking();
        SpawnedCount = 0;
        ConsumedCount = 0;
        ElapsedSeconds = 0;
        IsRunning = false;
        StateChanged?.Invoke();
    }

    private void ClearParking()
    {
        foreach (Structure structure in _structures.Structures) structure.ClearParkingReservations();
    }
}

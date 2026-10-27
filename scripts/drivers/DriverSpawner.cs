
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class DriverSpawner : Node2D
{
    private RoadGraph _roadGraph;
    private LaneGraph _laneGraph;

    private bool _hasSpawnedDriver = false;

    //  offset for one lane on a road.
    private const float LaneOffset = 8.0f;

    public override void _Ready()
    {
        _roadGraph = GetNode<RoadGraph>("../RoadGraph");
        _laneGraph = GetNode<LaneGraph>("../LaneGraph");

        _roadGraph.RoadPlaced += OnRoadPlaced;

        TrySpawnDriver();
    }

    public override void _ExitTree()
    {
        if (_roadGraph != null)
        {
            _roadGraph.RoadPlaced -= OnRoadPlaced;
        }
    }

    private void OnRoadPlaced(RoadSegment segment)
    {
        TrySpawnDriver();
    }

    private void TrySpawnDriver()
    {
        if (_hasSpawnedDriver || _roadGraph == null)
        {
            return;
        }

        List<RoadSegment> segments = _roadGraph.Segments.ToList();

        if (segments.Count == 0)
        {
            return;
        }

        // Start with the first road and follow connected segments.
        List<Vector2I> routeCells = BuildConnectedRoute(segments);

        if (routeCells.Count < 2)
        {
            GD.PushWarning("Not enough connected road cells to spawn a driver.");
            return;
        }

        var waypoints = new List<Vector2>();

        foreach (Vector2I cell in routeCells)
        {
            waypoints.Add(_roadGraph.CellToGlobalCenter(cell));
        }

        var driver = new Driver
        {
            Name = "TestDriver",
            DriverId = 1,
            Personality = DriverPersonality.Normal
        };

        AddChild(driver);

        // Places the car on one side of the road
        driver.SetLaneWaypoints(waypoints, LaneOffset);

        _hasSpawnedDriver = true;

        GD.Print(
            $"Driver spawned with {routeCells.Count} road waypoints."
        );
    }

    private List<Vector2I> BuildConnectedRoute(
        List<RoadSegment> segments)
    {
        var route = new List<Vector2I>();
        var usedSegments = new HashSet<RoadSegment>();

        RoadSegment current = segments[0];

        // begin at the first endpoint and travel to end
        route.AddRange(current.Path);
        usedSegments.Add(current);

        Vector2I currentEndpoint = current.B;

        while (true)
        {
            RoadSegment next = segments.FirstOrDefault(segment =>
                !usedSegments.Contains(segment) &&
                (segment.A == currentEndpoint ||
                 segment.B == currentEndpoint));

            if (next == null)
            {
                break;
            }

            // Reverse segment if needed so the paths join
            List<Vector2I> nextPath = new List<Vector2I>(next.Path);

            if (next.A == currentEndpoint)
            {
                // route already starts at the shared end
            }
            else
            {
                nextPath.Reverse();
            }

            // Skip shared end to avoid duplicating
            route.AddRange(nextPath.Skip(1));

            usedSegments.Add(next);
            currentEndpoint = nextPath[nextPath.Count - 1];
        }

        return route;
    }
}


using Godot;
using System;
using System.Collections.Generic;

public enum DriverPersonality
{
    Cautious,
    Normal,
    Aggressive
}

public partial class Driver : Node2D
{
    //driver id
    public int DriverId { get; set; }
    //driver personality
    public DriverPersonality Personality { get; set; } = DriverPersonality.Normal;

    //drivers speed
    public float Speed { get; private set; }

    //start and destination
    //TODO: will need to connect these to spawn (house?) and consumer structures
    public LaneID? SourceLane { get; set; }
    public LaneID? DestinationLane { get; set; }

    //sets speed based on personality
    public void AssignSpeed()
    {
        switch (Personality)
        {
            case DriverPersonality.Cautious:
                Speed = 30.0f;
                break;

            case DriverPersonality.Normal:
                Speed = 50.0f;
                break;

            case DriverPersonality.Aggressive:
                Speed = 75.0f;
                break;
        }
    }

    //car graphics
    //colors for spawned cars
    private static readonly Color[] AvailableCarColors =
    {
        new Color("#E53935"), // Red
        new Color("#FB8C00"), // Orange
        new Color("#FDD835"), // Yellow
        new Color("#43A047"), // Green
        new Color("#1E88E5"), // Blue
        new Color("#8E44AD")  // Purple
    };

    //random number generated to choose random color for car
    private static readonly RandomNumberGenerator Random =
    CreateRandomGenerator();

    private static RandomNumberGenerator CreateRandomGenerator()
    {
        var random = new RandomNumberGenerator();
        random.Randomize();
        return random;
    }

    //default for now
    public Color CarColor { get; private set; } = Colors.White;

    //assigns random car color, has to be called right now
    public void AssignRandomCarColor()
    {
        int colorIndex = Random.RandiRange(
            0,
            AvailableCarColors.Length - 1
        );

        CarColor = AvailableCarColors[colorIndex];

        UpdateCarAppearance();
    }

    private CarVisual _carGraphics;

    public CarVisual CarGraphics
    {
        get => _carGraphics;
        set
        {
            _carGraphics = value;
            UpdateCarAppearance();
        }
    }

    public override void _Ready()
    {
        // Set driver's speed from personality
        AssignSpeed();

        // choose color for car
        AssignRandomCarColor();

        // Create car visual
        CarGraphics = new CarVisual();
        CarGraphics.Name = "CarVisual";
        AddChild(CarGraphics);
    }


    //path the driver will travel through
    public List<LaneID> Route { get; private set; } = new List<LaneID>();

    
    // world coordinates
    private readonly List<Vector2> _waypoints = new List<Vector2>();

    // The next waypoint the driver is going to
    private int _currentWaypoint = 0;

    // true when route to follow
    public bool IsMoving { get; private set; } = false;

    private float _laneOffset = 0.0f;

    public void SetLaneWaypoints(
        IEnumerable<Vector2> waypoints,
        float laneOffset)   
    {
        _laneOffset = laneOffset;
        SetWaypoints(waypoints);
        var points = new List<Vector2>(waypoints);
        var offsetPoints = new List<Vector2>();

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 tangent;

            if (i == 0)
            {
                tangent = points[1] - points[0];
            }
            else if (i == points.Count - 1)
            {
                tangent = points[i] - points[i - 1];
            }
            else
            {
                tangent = points[i + 1] - points[i - 1];
            }

            tangent = tangent.Normalized();

            Vector2 normal = new Vector2(-tangent.Y, tangent.X);
            offsetPoints.Add(points[i] + normal * laneOffset);
        }

        SetWaypoints(offsetPoints);
    }   

    

    public void SetWaypoints(IEnumerable<Vector2> waypoints)
    {
        _waypoints.Clear();
        _waypoints.AddRange(waypoints);

        _currentWaypoint = 0;
        IsMoving = _waypoints.Count > 0;

        if (IsMoving)
        {
            // Start at the first point in the route.
            GlobalPosition = _waypoints[0];
            _currentWaypoint = 1;
        }
    }

    
    public override void _Process(double delta)
    {
        // for no route
        if (!IsMoving || _currentWaypoint >= _waypoints.Count)
        {
            IsMoving = false;
            return;
        }

        Vector2 target = _waypoints[_currentWaypoint];
        Vector2 direction = target - GlobalPosition;

        if (direction.Length() < 1.0f)
        {
            GlobalPosition = target;
            _currentWaypoint++;

            if (_currentWaypoint >= _waypoints.Count)
            {
                IsMoving = false;
            }

            return;
        }

        // face forward to route
        Rotation = direction.Angle();

        // move to next point.
        float distance = Speed * (float)delta;

        GlobalPosition = GlobalPosition.MoveToward(
            target,
            distance
        );

        // check that car made it 
        if (GlobalPosition.DistanceTo(target) < 1.0f)
        {
            GlobalPosition = target;
            _currentWaypoint++;

            if (_currentWaypoint >= _waypoints.Count)
            {
                IsMoving = false;
            }
        }
    }




    //calculates route with existing LaneGraph
    public bool CalculateRoute(
        LaneGraph laneGraph,
        Func<LaneID, LaneID, float> heuristic = null)
    {
        Route.Clear();

        if (laneGraph == null || !SourceLane.HasValue || !DestinationLane.HasValue)
        {
            return false;
        }

        LaneID source = SourceLane.Value;
        LaneID destination = DestinationLane.Value;

        //TODO: update the heuristic once lane positions are implemented
        //kinda like djikstra's right now
        heuristic ??= (from, to) => 0.0f;

        var openSet = new PriorityQueue<LaneID, float>();
        var cameFrom = new Dictionary<LaneID, LaneID>();
        var gScore = new Dictionary<LaneID, float>();

        gScore[source] = 0.0f;

        openSet.Enqueue(source, heuristic(source, destination));

        while (openSet.Count > 0)
        {
            LaneID current = openSet.Dequeue();

            if (current == destination)
            {
                //rebuild route
                Route.Add(current);

                while (current != source)
                {
                    current = cameFrom[current];
                    Route.Add(current);
                }

                Route.Reverse();
                return true;
            }

            foreach (LaneConnection connection
                in laneGraph.GetOutgoing(current))
            {
                LaneID neighbor = connection.To;

                float tentativeScore = gScore[current] + 1.0f;

                if (!gScore.ContainsKey(neighbor) ||
                    tentativeScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeScore;

                    float priority = tentativeScore
                        + heuristic(neighbor, destination);

                    openSet.Enqueue(neighbor, priority);
                }
            }
        }

        //no route found
        return false;
    }

    // applies car color
    public void UpdateCarAppearance()
    {
        if (CarGraphics != null)
        {
            CarGraphics.SetCarColor(CarColor);
        }
    }
}

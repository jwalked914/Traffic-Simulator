
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
    // assigned by the producer's route to a consumer
    public LaneID? SourceLane { get; set; }
    public LaneID? DestinationLane { get; set; }
    public event Action<Driver> Arrived;
    private Vector2 _sourceOpening;
    private Vector2 _sourceDirection;
    private bool _hasTunnelOpenings;
    private bool _hasLeftTunnel;
    private bool _enteringTunnel;

    //sets speed based on personality
    private float GetSpeedForLimit(float speedLimit)
    {
        float targetSpeed = speedLimit;
        switch (Personality)
        {
            case DriverPersonality.Cautious:
                targetSpeed -= 5.0f;
                break;

            case DriverPersonality.Normal:
                //normal drivers follow speed limit
                break;

            case DriverPersonality.Aggressive:
                targetSpeed += 10.0f;
                break;
        }

        //convert mph to units for simulation
        // 35 mph, normal at 120 units per second
        return targetSpeed * (120.0f / 35.0f);
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
        // Set driver's speed from personality (default befor on road)
        Speed = GetSpeedForLimit(35.0f);

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
    //speed limit for each waypoint
    private readonly List<float> _waypointSpeedLimits = new List<float>();

    // The next waypoint the driver is going to
    private int _currentWaypoint = 0;
    internal int NextWaypoint => _currentWaypoint;
    internal int WaypointCount => _waypoints.Count;
    private bool _backOut;

    //other Drivers currently in the simulation
    private IEnumerable<Driver> _otherDrivers;

    private const float SafeFollowDistance = 32.0f;
    private const float DetectionDistance = 70.0f;
    private const float LaneDetectionWidth = 10.0f;

    //distance for cars crossing directions
    private const float IntersectionConflictDistance = 36.0f;


    public void SetOtherDrivers(IEnumerable<Driver> drivers)
    {
        _otherDrivers = drivers;
    }

    //signaled intersections in this run
    private IReadOnlyCollection<JunctionController> _junctions;
    //the signaled junction the driver is driving through, and the one it's waiting at
    private JunctionController _insideJunction;
    private JunctionController _waitingAt;

    //near a signaled junction, the signal decides who goes instead of the driver-id rule
    internal bool NearControlledJunction { get; private set; }

    public void SetJunctionControllers(IReadOnlyCollection<JunctionController> junctions)
    {
        _junctions = junctions;
    }

    //true when the driver has to wait at a stop line this frame
    private bool HeldAtJunction(Vector2 direction, double delta)
    {
        NearControlledJunction = false;
        if (_junctions == null || _junctions.Count == 0) return false;

        //driving through a junction: release it once past it and heading away
        if (_insideJunction != null)
        {
            Vector2 fromCenter = GlobalPosition - _insideJunction.Center;
            if (fromCenter.Length() > _insideJunction.StopDistance && fromCenter.Dot(direction) > 0)
            {
                _insideJunction.Exit(this);
                _insideJunction = null;
            }
            else
            {
                NearControlledJunction = true;
                return false;
            }
        }

        foreach (JunctionController junction in _junctions)
        {
            Vector2 toCenter = junction.Center - GlobalPosition;
            float distance = toCenter.Length();
            if (distance > junction.StopDistance * 1.5f) continue;

            NearControlledJunction = true;

            //only stop for a junction ahead, once at its stop line
            if (distance > junction.StopDistance || toCenter.Dot(direction) <= 0) continue;

            if (junction.RequestEntry(this, delta))
            {
                _insideJunction = junction;
                _waitingAt = null;
                return false;
            }

            _waitingAt = junction;
            return true;
        }

        _waitingAt = null;
        return false;
    }

    //lets go of any junction when the driver leaves the road
    private void LeaveJunctionControl()
    {
        _insideJunction?.Forget(this);
        _waitingAt?.Forget(this);
        _insideJunction = null;
        _waitingAt = null;
        NearControlledJunction = false;
    }

    private float GetMovementSpeed(Vector2 direction)
    {
        float movementSpeed = Speed;
        if (_otherDrivers == null)
        {
            return movementSpeed;
        }

        Vector2 sideDirection = new Vector2(-direction.Y, direction.X);

        foreach (Driver other in _otherDrivers)
        {
            //prevent checking itself
            if (other == this || (!other.IsMoving && !other.IsWaitingInLane))
            {
                continue;
            }

            //cars waiting at the junction this driver is crossing never block it
            if (_insideJunction != null && other._waitingAt == _insideJunction)
            {
                continue;
            }

            Vector2 difference = other.GlobalPosition - GlobalPosition;

            float distance = difference.Length();

            //get other car direction
            Vector2 otherDirection = new Vector2(Mathf.Cos(other.Rotation), Mathf.Sin(other.Rotation));

            //check if traveling in different directions
            float directionDot = Mathf.Abs(direction.Dot(otherDirection));

            bool directionCross = directionDot < 0.866f;

            //handle cars in deadlock crossing (signaled junctions handle this themselves)
            if (other.IsMoving && directionCross && distance < IntersectionConflictDistance &&
                !NearControlledJunction && !other.NearControlledJunction)
            {
                //lower driver id gets temp priority
                if (DriverId < other.DriverId)
                {
                    continue;
                }
                else
                {
                    return 0.0f;
                }
            }

            // distance ahead of driver
            float distanceAhead = difference.Dot(direction);
            //distance between the lanes
            float sideDistance = Mathf.Abs(difference.Dot(sideDirection));

            // only check cars in same lane
            if (distanceAhead > 0 && distanceAhead < DetectionDistance && sideDistance < LaneDetectionWidth)
            {
                if (distanceAhead <= SafeFollowDistance)
                {
                    return 0.0f;
                }

                // slow down when approaching another car
                float speedFactor = Mathf.Clamp((distanceAhead - SafeFollowDistance) / (DetectionDistance - SafeFollowDistance), 0.0f, 1.0f);

                movementSpeed = Mathf.Min(movementSpeed, Speed * speedFactor);

            }
        }
        return movementSpeed;
    }

    // true when route to follow
    public bool IsMoving { get; private set; } = false;
    internal bool IsWaitingInLane { get; set; }

    public void SetLaneWaypoints(
        IEnumerable<Vector2> waypoints,
        float laneOffset)   
    {
        var points = new List<Vector2>(waypoints);
        SetWaypoints(CreateLaneWaypoints(points, laneOffset));
    }

    // creates a list of waypoints that are offset from the center, for visually showing cars in the correct lane
    public static List<Vector2> CreateLaneWaypoints(IReadOnlyList<Vector2> points, float laneOffset)
    {
        var offsetPoints = new List<Vector2>(points.Count);
        if (points.Count < 2)
        {
            offsetPoints.AddRange(points);
            return offsetPoints;
        }

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
                Vector2 incoming = (points[i] - points[i - 1]).Normalized();
                Vector2 outgoing = (points[i + 1] - points[i]).Normalized();
                float corner = 1.0f + incoming.Dot(outgoing);
                if (corner > 0.5f)
                {
                    // keep the corner aligned with both lane edges
                    Vector2 turn = (incoming + outgoing) / corner;
                    offsetPoints.Add(points[i] + new Vector2(-turn.Y, turn.X) * laneOffset);
                    continue;
                }
                tangent = points[i + 1] - points[i - 1];
            }

            tangent = tangent.Normalized();

            Vector2 normal = new Vector2(-tangent.Y, tangent.X);
            offsetPoints.Add(points[i] + normal * laneOffset);
        }

        return offsetPoints;
    }   

    

    //set waypoints with default 35
    public void SetWaypoints(IEnumerable<Vector2> waypoints)
    {
        var points = new List<Vector2>(waypoints);

        var speedLimits = new List<float>();

        for (int i = 0; i < points.Count; i++)
        {
            speedLimits.Add(35.0f);
        }

        SetWaypoints(points, speedLimits);
    }

    //set waypoints with speed limit
    public void SetWaypoints(
        IEnumerable<Vector2> waypoints,
        IEnumerable<float> speedLimits,
        bool backOut = false)
    {
        _waypoints.Clear();
        _waypoints.AddRange(waypoints);
        _backOut = backOut;

        _waypointSpeedLimits.Clear();
        _waypointSpeedLimits.AddRange(speedLimits);

        while (_waypointSpeedLimits.Count < _waypoints.Count)
        {
            _waypointSpeedLimits.Add(35.0f);
        }

        _currentWaypoint = 0;
        IsMoving = _waypoints.Count > 0;

        if (IsMoving)
        {
            // Start at the first point in the route.
            GlobalPosition = _waypoints[0];
            _currentWaypoint = 1;
        }
    }

    internal void ContinueWaypoints(IEnumerable<Vector2> waypoints)
    {
        foreach (Vector2 waypoint in waypoints)
        {
            _waypoints.Add(waypoint);
            _waypointSpeedLimits.Add(35.0f);
        }
        IsMoving = _currentWaypoint < _waypoints.Count;
    }

    internal float GetRemainingRouteDistance()
    {
        float distance = 0;
        Vector2 position = GlobalPosition;
        for (int i = _currentWaypoint; i < _waypoints.Count; i++)
        {
            distance += position.DistanceTo(_waypoints[i]);
            position = _waypoints[i];
        }
        return distance;
    }

    public void SetTunnelOpenings(Vector2 source, Vector2 sourceDirection, Vector2 consumer, Vector2 consumerDirection)
    {
        _sourceOpening = source;
        _sourceDirection = sourceDirection;
        _hasTunnelOpenings = true;
        _hasLeftTunnel = false;
        _enteringTunnel = false;
        CarGraphics.SetTunnelOpenings(source, sourceDirection, consumer, consumerDirection);
    }

    private void UpdateTunnelClipping()
    {
        if (!_hasTunnelOpenings) return;
        if (!_hasLeftTunnel && (GlobalPosition - _sourceOpening).Dot(_sourceDirection) > CarVisual.HalfLength * GlobalScale.X)
        {
            _hasLeftTunnel = true;
            CarGraphics.FinishLeavingTunnel();
        }
        if (!_enteringTunnel && _currentWaypoint >= _waypoints.Count - 2)
        {
            _enteringTunnel = true;
            CarGraphics.BeginEnteringTunnel();
        }
    }

    
    public override void _Process(double delta)
    {
        // for no route
        if (!IsMoving) return;
        UpdateTunnelClipping();
        if (_currentWaypoint >= _waypoints.Count)
        {
            CompleteRoute();
            return;
        }

        Vector2 target = _waypoints[_currentWaypoint];
        Vector2 direction = target - GlobalPosition;

        if (direction.Length() < 1.0f)
        {
            GlobalPosition = target;
            _currentWaypoint++;
            UpdateTunnelClipping();

            if (_currentWaypoint >= _waypoints.Count)
            {
                CompleteRoute();
            }

            return;
        }

        // face forward to route
        bool reversing = _backOut && _currentWaypoint == 1;
        Rotation = (reversing ? -direction : direction).Angle();

        //update speed based on speed limit
        int speedIndex = Mathf.Clamp(_currentWaypoint, 0, _waypointSpeedLimits.Count - 1);

        Speed = GetSpeedForLimit(_waypointSpeedLimits[speedIndex]);

        // stop at signals, then check for other cars
        bool held = HeldAtJunction(direction.Normalized(), delta);
        float movementSpeed = held ? 0.0f : GetMovementSpeed(direction.Normalized());
        if (reversing) movementSpeed *= 0.5f;

        // move to next point with adjusted speed
        float distance = movementSpeed * (float)delta;

        GlobalPosition = GlobalPosition.MoveToward(
            target,
            distance
        );
        UpdateTunnelClipping();

        // check that car made it 
        if (GlobalPosition.DistanceTo(target) < 1.0f)
        {
            GlobalPosition = target;
            _currentWaypoint++;
            UpdateTunnelClipping();

            if (_currentWaypoint >= _waypoints.Count)
            {
                CompleteRoute();
            }
        }
    }
    
    private void CompleteRoute()
    {
        LeaveJunctionControl();
        IsMoving = false;
        Arrived?.Invoke(this);
    }

    //calculates route with existing LaneGraph
    public bool CalculateRoute(
        LaneGraph laneGraph,
        Func<LaneID, LaneID, float> heuristic = null,
        Func<LaneID, float> laneLength = null)
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

                float tentativeScore = gScore[current] + (laneLength?.Invoke(neighbor) ?? 1.0f);

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

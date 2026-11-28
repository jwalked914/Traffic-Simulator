using Godot;
using System.Collections.Generic;

// runtime traffic control for one signaled intersection, created when the sim starts
// drivers call RequestEntry every frame while they wait at the stop line
// each signal type's rules live in its own section below, picked by Settings.Type
public class JunctionController
{
	public const double StopSeconds = 0.5;

	public Vector2I Cell { get; }
	public Vector2 Center { get; }       // global position of the junction center
	public float StopDistance { get; }   // how far from the center cars wait
	public SignalSettings Settings { get; }

	// drivers currently driving through the junction
	private readonly HashSet<Driver> _inside = new();

	// all-way stop: how long each car has been stopped, and the line of cars done stopping
	private readonly Dictionary<Driver, double> _stoppedFor = new();
	private readonly List<Driver> _queue = new();

	private JunctionController(Vector2I cell, Vector2 center, float stopDistance, SignalSettings settings)
	{
		Cell = cell;
		Center = center;
		StopDistance = stopDistance;
		Settings = settings;
	}

	// builds a controller for signal types that are implemented, otherwise null (no signal)
	public static JunctionController Create(Vector2I cell, Vector2 center, float stopDistance, SignalSettings settings) =>
		settings.Type switch
		{
			SignalType.AllWayStop => new JunctionController(cell, center, stopDistance, settings),
			_ => null // two-way stops and traffic lights are not built yet
		};

	// called every frame by a driver waiting at the stop line; true means it may go
	public bool RequestEntry(Driver driver, double delta) => Settings.Type switch
	{
		SignalType.AllWayStop => RequestAllWayStop(driver, delta),
		_ => true
	};

	// the driver has cleared the junction
	public void Exit(Driver driver) => _inside.Remove(driver);

	// the driver left the sim (arrived or reset), wherever it was
	public void Forget(Driver driver)
	{
		_inside.Remove(driver);
		_queue.Remove(driver);
		_stoppedFor.Remove(driver);
	}

	// for timed signals like traffic lights
	public void Update(double delta) { }

	// all-way stop: every car stops, then they go one at a time
	// in the order they finished stopping
	private bool RequestAllWayStop(Driver driver, double delta)
	{
		// a full stop first
		if (!_queue.Contains(driver))
		{
			double stopped = _stoppedFor.GetValueOrDefault(driver) + delta;
			_stoppedFor[driver] = stopped;
			if (stopped < StopSeconds) return false;

			_stoppedFor.Remove(driver);
			_queue.Add(driver);
		}

		// then wait until the junction is empty and it's this driver's turn
		if (_inside.Count > 0 || _queue[0] != driver) return false;

		_queue.RemoveAt(0);
		_inside.Add(driver);
		return true;
	}
}

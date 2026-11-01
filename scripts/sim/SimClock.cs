/*
    This class tracks simulation time, handles checkpoints, and clock formatting
    Also possesses auto checkpoints which can help when determing scoring scenarios
*/
using Godot;
using System;
using System.Collections.Generic;

public partial class SimClock : Node
{
    [Export] public double StartHour {get; set;} = 8.0;
    [Export] public double SimSecondsPerRealSecond { get; set;} = 60.0;
    [Export] public double CheckpointInterval { get; set; } = 0;

    public double SimTime { get; private set; }
    public bool Running { get; private set; }
    public float Speed { get; private set; }  = 1f; // TODO: add some kind of ui element to change this

    public event Action<double> Ticked;
    public event Action StateChanged;
    public event Action<int, double, double> CheckpointAdded; // number, sim time, time interval
    
    private readonly List<double> _checkpoints = new();
    private double _nextAutocheckpoint;
    public IReadOnlyList<double> Checkpoints => _checkpoints;

    public static string Format(double seconds)
    {
        
        int s = (int)seconds;
        return $"{s / 3600:00}:{s / 60 % 60:00}";
    }

    public string TimeText => Format(SimTime);
    
    // starts paused at the start hour
    public override void _Ready() => Reset();

    // advance sim time and tell listeners how much time as elapsed
    public override void _Process(double delta)
    {
        if (!Running) return;
        double dt = delta * SimSecondsPerRealSecond * Speed;
        SimTime += dt;
        Ticked?.Invoke(dt);

        if (CheckpointInterval > 0 && SimTime >= _nextAutocheckpoint)
        {
            AddCheckpoint();
            _nextAutocheckpoint += CheckpointInterval;
        }
    }

    // starts and stops sim
    public void Toggle()
    {
        Running = !Running;
        StateChanged?.Invoke();
    }

    // resets sim time
    public void Reset()
    {
        Running = false;
        SimTime = StartHour * 3600;
        _checkpoints.Clear();
        _nextAutocheckpoint = SimTime + CheckpointInterval;
        StateChanged?.Invoke();
    }

    // records current sim time and how long since the last checkpoint
    public void AddCheckpoint()
    {
        double last = _checkpoints.Count > 0 ? _checkpoints[^1] : StartHour * 3600;
        _checkpoints.Add(SimTime);
        // NOTE: split(SimTime - last) is unused for right now; reserved for periodic scoring
        CheckpointAdded?.Invoke(_checkpoints.Count, SimTime, SimTime - last);
    }


    // sets sim speed
    /*public void SetSpeed(float speed)
    {
        Speed = speed;
        StateChanged?.Invoke();
    }*/

}
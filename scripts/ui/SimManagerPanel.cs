using Godot;
using System;

public partial class SimManagerPanel : Control
{
	public event Action ToggleRequested;
	public event Action StopRequested;

	private readonly Texture2D _playIcon = GD.Load<Texture2D>("res://assets/play_white.svg");
	private readonly Texture2D _pauseIcon = GD.Load<Texture2D>("res://assets/pause_white.svg");
	private SimClock _simclock;
	private Button _startsim;
	private Button _stopsim;
	private Button _flag;
	private Label _simtime;
	private Label _status;
	private CheckpointTimeline _timeline;
	private HSlider _speedSlider;
	private Label _speedValue;
	private static readonly float[] Speeds = { 1f, 2f, 4f, 8f};

	// connects the simulation buttons, time display, and checkpoint timeline
	public override void _Ready()
	{
		const string row = "PanelContainer/Content/HBoxContainer/";
		const string speedRow = "PanelContainer/Content/SpeedRow/";
		_simclock = GetNode<SimClock>("../../World/SimClock");
		_startsim = GetNode<Button>(row + "StartSimButton");
		_stopsim = GetNode<Button>(row + "StopSimButton");
		_flag = GetNode<Button>(row + "CheckpointButton");
		_simtime = GetNode<Label>(row + "ClockColumn/SimTimeLabel");
		_timeline = GetNode<CheckpointTimeline>(row + "ClockColumn/CheckpointTimeline");
		_status = GetNode<Label>("PanelContainer/Content/Status");
		_timeline.Clock = _simclock;
		_startsim.Pressed += () => ToggleRequested?.Invoke();
		_stopsim.Pressed += () => StopRequested?.Invoke();
		_flag.Pressed += _simclock.AddCheckpoint;

		_speedSlider = GetNode<HSlider>(speedRow + "SpeedSlider");
		_speedValue = GetNode<Label>(speedRow + "SpeedValue");
		_speedSlider.ValueChanged += v => _simclock.SetSpeed(Speeds[(int)v]);

	}

	// updates the time readout and moves the timeline marker every frame
	public override void _Process(double delta)
	{
		_simtime.Text = _simclock.TimeText;
		_timeline.QueueRedraw();
	}

	// shows the run state and driver counts
	public void Refresh(bool active, int spawned, int drivers, int arrived)
	{
		_startsim.Icon = _simclock.Running ? _pauseIcon : _playIcon;
		_startsim.TooltipText = _simclock.Running ? "Pause simulation" : active ? "Resume simulation" : "Start simulation";
		_startsim.TooltipText += $"\nCreated: {spawned}  |  Active: {drivers}  |  Arrived: {arrived}";
		_stopsim.Disabled = !active;
		_flag.Disabled = !_simclock.Running;
		_status.Hide();
		
		_speedSlider.SetValueNoSignal(Array.IndexOf(Speeds, _simclock.Speed));
		_speedValue.Text = $"{_simclock.Speed}x";
	}

	// shows a message under the controls
	public void SetMessage(string message)
	{
		_status.Text = message;
		_status.Show();
	}

	
}
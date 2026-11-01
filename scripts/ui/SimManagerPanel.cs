using Godot;
using System;

public partial class SimManagerPanel : Control
{
<<<<<<< HEAD
	public event Action ToggleRequested;
	public event Action StopRequested;
=======
	private Texture2D _playIcon = GD.Load<Texture2D>("res://assets/play_white.svg");
	private Texture2D _pauseIcon = GD.Load<Texture2D>("res://assets/pause_white.svg");
>>>>>>> inspector-road-settings

	private readonly Texture2D _playIcon = GD.Load<Texture2D>("res://assets/play_white.svg");
	private readonly Texture2D _pauseIcon = GD.Load<Texture2D>("res://assets/pause_white.svg");
	private SimClock _simclock;
	private Button _startsim;
<<<<<<< HEAD
	private Button _stopsim;
	private Label _simtime;
	private Label _status;

	// connects the simulation buttons and time display
	public override void _Ready()
	{
		_simclock = GetNode<SimClock>("../../World/SimClock");
		_startsim = GetNode<Button>("PanelContainer/Content/HBoxContainer/StartSimButton");
		_stopsim = GetNode<Button>("PanelContainer/Content/HBoxContainer/StopSimButton");
		_simtime = GetNode<Label>("PanelContainer/Content/HBoxContainer/SimTimeLabel");
		_status = GetNode<Label>("PanelContainer/Content/Status");
		_startsim.Pressed += () => ToggleRequested?.Invoke();
		_stopsim.Pressed += () => StopRequested?.Invoke();
	}

	// updates the time shown on the panel
	public override void _Process(double delta) => _simtime.Text = _simclock.TimeText;

	// shows the run state and driver counts
	public void Refresh(bool active, int spawned, int drivers, int arrived)
	{
		_startsim.Icon = _simclock.Running ? _pauseIcon : _playIcon;
		_startsim.TooltipText = _simclock.Running ? "Pause simulation" : active ? "Resume simulation" : "Start simulation";
		_stopsim.Disabled = !active;
		_startsim.TooltipText += $"\nCreated: {spawned}  |  Active: {drivers}  |  Arrived: {arrived}";
		_status.Hide();
	}

	public void SetMessage(string message)
	{
		_status.Text = message;
		_status.Show();
	}
}
=======
	private Button _flag;
	private Label _simtime;
	private CheckpointTimeline _timeline;

	// finds the clock and panel controls, then connects button actions
	public override void _Ready()
	{
		_simclock = GetNode<SimClock>("../../World/SimClock");
		_startsim = GetNode<Button>("PanelContainer/HBoxContainer/StartSimButton");
		_flag = GetNode<Button>("PanelContainer/HBoxContainer/CheckpointButton");
		_simtime = GetNode<Label>("PanelContainer/HBoxContainer/ClockColumn/SimTimeLabel");
		_timeline = GetNode<CheckpointTimeline>("PanelContainer/HBoxContainer/ClockColumn/CheckpointTimeline");

		_timeline.Clock = _simclock;
		_startsim.Pressed += _simclock.Toggle;
		_flag.Pressed += _simclock.AddCheckpoint;
		_simclock.StateChanged += Refresh;
		Refresh();
	}

	// disconnects from the clock when leaving the simulator
	public override void _ExitTree()
	{
		if (_simclock != null) _simclock.StateChanged -= Refresh;
	}

	// updates the time readout and moves the timeline marker every frame
	public override void _Process(double delta)
	{
		_simtime.Text = _simclock.TimeText;
		_timeline.QueueRedraw();
	}

	// swaps the play/pause icon and only allows flags while the sim is running
	private void Refresh()
	{
		_startsim.Icon = _simclock.Running ? _pauseIcon : _playIcon;
		_flag.Disabled = !_simclock.Running;
	}
}
>>>>>>> inspector-road-settings

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

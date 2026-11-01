using Godot;
using System;

public partial class SimManagerPanel : Control
{
	private Texture2D _playIcon = GD.Load<Texture2D>("res://assets/play_white.svg");
	private Texture2D _pauseIcon = GD.Load<Texture2D>("res://assets/pause_white.svg");

	private SimClock _simclock;
	private Button _startsim;
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
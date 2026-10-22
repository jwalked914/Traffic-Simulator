using Godot;
using System;

public partial class SimManagerPanel : Control
{
	public event Action SaveRequested; // TODO: needs to be wired to save

	private Texture2D _playIcon = GD.Load<Texture2D>("res://assets/play_white.svg");
	private Texture2D _pauseIcon = GD.Load<Texture2D>("res://assets/pause_white.svg");

	private SimClock _simclock;
	private Button _startsim;
	private Button _savesim;
	private Label _simtime;



	public override void _Ready()
	{
		_simclock = GetNode<SimClock>("../../World/SimClock");
		_startsim = GetNode<Button>("PanelContainer/HBoxContainer/StartSimButton");
		_savesim = GetNode<Button>("PanelContainer/HBoxContainer/SaveSimButton");
		_simtime = GetNode<Label>("PanelContainer/HBoxContainer/SimTimeLabel");
	

		_startsim.Pressed += _simclock.Toggle;
		_savesim.Pressed += () => SaveRequested?.Invoke();
        _simclock.StateChanged += Refresh;
        Refresh();
	}

	// disconnects clock when leaving simulator
	public override void _ExitTree()
	{
		if (_simclock != null) _simclock.StateChanged -= Refresh;
	}

	// update clock time every frame
	public override void _Process(double delta) => _simtime.Text = _simclock.TimeText;

	private void Refresh()
	{
		_startsim.Icon = _simclock.Running ? _pauseIcon : _playIcon;
		_savesim.Disabled = _simclock.Running;
	}
	
}

using Godot;
using System;
using System.Runtime.CompilerServices;

public enum ToolType
{
	None,
	Road,
	Junction,
	Location
}
public partial class ToolBox : Control
{
	[Signal] public delegate void ToolSelectedEventHandler(int tool);

	public ToolType CurrentTool { get; private set; } = ToolType.None;

	private Button roadButton;
	private Button junctionButton;
	private Button locationButton;

	public override void _Ready()
	{
		roadButton = GetNode<Button>("PanelContainer/ToolBoxButtons/RoadButton");
		junctionButton = GetNode<Button>("PanelContainer/ToolBoxButtons/JunctionButton");
		locationButton = GetNode<Button>("PanelContainer/ToolBoxButtons/LocationButton");

		roadButton.Toggled += (pressed) => OnToolToggled(pressed, ToolType.Road);
		junctionButton.Toggled += (pressed) => OnToolToggled(pressed, ToolType.Junction);
		locationButton.Toggled += (pressed) => OnToolToggled(pressed, ToolType.Location);
	}

	private void OnToolToggled(bool pressed, ToolType tool)
	{
		CurrentTool = pressed ? tool : ToolType.None;

		GD.Print($"Tool selected: {CurrentTool}");
		EmitSignal(SignalName.ToolSelected, (int)CurrentTool);
		// *** OBJECT PLACEMENT LOGIC GOES HERE ***
	}
}


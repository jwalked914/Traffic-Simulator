using Godot;

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
		EmitSignal(SignalName.ToolSelected, (int)CurrentTool);
	}
}


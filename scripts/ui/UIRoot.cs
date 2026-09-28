using Godot;

// root of UI layer
// other systems read CurrentTool
// listen for ToolChanged here.
public partial class UIRoot : CanvasLayer
{

	[Signal] public delegate void ToolChangedEventHandler(int tool);

	// loads main menu scene
	[Export(PropertyHint.File, "*.tscn")] public string MainMenuScene { get; set; } = "res://scenes/MainMenu.tscn";

	public ToolType CurrentTool{ get; private set; } = ToolType.None;

	private ToolBox toolBox;
	private InspectorPanel inspectorPanel;
	private MenuButton simMenuButton;

	public override void _Ready()
	{
		toolBox = GetNode<ToolBox>("ToolBox");
		inspectorPanel = GetNode<InspectorPanel>("InspectorPanel");
		
		simMenuButton = GetNode<MenuButton>("SimMenuButton");
		simMenuButton.GetPopup().IdPressed += OnSimMenuItemPressed;

		toolBox.ToolSelected+= OnToolSelected;
	}


	private void OnToolSelected(int tool)
	{
		CurrentTool = (ToolType)tool;
		inspectorPanel.ShowForTool(CurrentTool);
		EmitSignal(SignalName.ToolChanged, tool);
	}

	private void OnSimMenuItemPressed(long id)
	{
		switch (id)
		{
			case 0: // main menu
				GetTree().ChangeSceneToFile(MainMenuScene);
				break;

			case 1: // settings?
				break;	

			case 2:	 // close app
				GetTree().Quit();
				break;
		}
	}
}

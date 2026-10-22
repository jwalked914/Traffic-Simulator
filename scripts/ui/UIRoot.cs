using Godot;

public partial class UIRoot : CanvasLayer
{
    [Signal] public delegate void ToolChangedEventHandler(int tool);
    [Export(PropertyHint.File, "*.tscn")]
    public string MainMenuScene { get; set; } = "res://scenes/MainMenu.tscn";
    public ToolType CurrentTool { get; private set; } = ToolType.None;
    private ToolBox _toolBox;
    private InspectorPanel _inspector;
    private StructureGraph _structures;

    // connects inspector actions to building edits and placement settings
    public override void _Ready()
    {
        _toolBox = GetNode<ToolBox>("ToolBox");
        _inspector = GetNode<InspectorPanel>("InspectorPanel");
        _structures = GetNode<StructureGraph>("../World/StructureGraph");
        var roads = GetNode<RoadGraph>("../World/RoadGraph");
        _inspector.SetParkingScale(roads.CellSize, roads.RoadWidth);
        GetNode<MenuButton>("SimMenuButton").GetPopup().IdPressed += OnSimMenuItemPressed;
        _toolBox.ToolSelected += OnToolSelected;
        _inspector.DraftChanged += _structures.SetDraft;
        _inspector.ApplyRequested += ApplyStructure;
        _inspector.DeleteRequested += DeleteStructure;
        _inspector.NewRequested += () => _toolBox.SelectTool(ToolType.Structure);
        _inspector.CloseRequested += () => _toolBox.SelectTool(ToolType.None);
        _structures.SelectionChanged += OnStructureSelected;
        _structures.StructuresChanged += _inspector.RefreshDetails;
        _structures.PlacementRejected += _inspector.SetMessage;
    }

    // disconnects building updates when leaving the simulator
    public override void _ExitTree()
    {
        if (_structures == null) return;
        _structures.SelectionChanged -= OnStructureSelected;
        _structures.StructuresChanged -= _inspector.RefreshDetails;
        _structures.PlacementRejected -= _inspector.SetMessage;
    }

    // clears the old building selection before switching tools
    private void OnToolSelected(int tool)
    {
        CurrentTool = (ToolType)tool;
        _structures.Select(null);
        _inspector.ShowForTool(CurrentTool);
        EmitSignal(SignalName.ToolChanged, tool);
    }

    // shows the selected building or returns to the active tool's settings
    private void OnStructureSelected(Structure structure)
    {
        if (structure == null) _inspector.ShowForTool(CurrentTool);
        else _inspector.ShowStructure(structure);
    }

    // applies the inspector's values or shows why the edit was rejected
    private void ApplyStructure()
    {
        if (!_structures.TryEdit(_structures.Selected, _inspector.ChosenType,
            _inspector.ChosenSize, _inspector.ChosenParkingRows, out string error))
            _inspector.SetMessage(error);
        else _inspector.ShowStructure(_structures.Selected);
    }

    // removes the selected building or shows why it cannot be removed
    private void DeleteStructure()
    {
        if (!_structures.TryRemove(_structures.Selected, out string error))
            _inspector.SetMessage(error);
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

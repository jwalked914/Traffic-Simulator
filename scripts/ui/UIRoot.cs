using Godot;
using System;
using System.Linq;

public partial class UIRoot : CanvasLayer
{
	[Signal] public delegate void ToolChangedEventHandler(int tool);
	[Export(PropertyHint.File, "*.tscn")]
	public string MainMenuScene { get; set; } = "res://scenes/MainMenu.tscn";
	public ToolType CurrentTool { get; private set; } = ToolType.None;
	private ToolBox _toolBox;
	private InspectorPanel _inspector;
	private StructureGraph _structures;
	private ScenarioManager _scenario;
	
	// save/load dialogs
	private FileDialog _fileDialog;
	private ConfirmationDialog _confirmDiscard;
	private AcceptDialog _messageDialog;
	private bool _dialogIsSave;
	private Action _pendingAction;
	
	private const string SaveDir = "user://scenarios";

	// connects inspector actions to building edits and placement settings
	public override void _Ready()
	{
		_toolBox = GetNode<ToolBox>("ToolBox");
		_inspector = GetNode<InspectorPanel>("InspectorPanel");
		_structures = GetNode<StructureGraph>("../World/StructureGraph");
		_scenario = GetNode<ScenarioManager>("../World/ScenarioManager");
		var roads = GetNode<RoadGraph>("../World/RoadGraph");
		_inspector.SetParkingScale(roads.CellSize, roads.RoadWidth);
				
		GetNode<MenuButton>("SimMenuButton").GetPopup().IdPressed += OnSimMenuItemPressed;
		
		DirAccess.MakeDirRecursiveAbsolute(SaveDir);
		
		_fileDialog = new FileDialog
		{
			Access = FileDialog.AccessEnum.Userdata,
			Filters = new[] { "*.json ; Traffic scenario" },
			CurrentDir = SaveDir
		};
		_fileDialog.FileSelected += OnFileSelected;
		AddChild(_fileDialog);

		_confirmDiscard = new ConfirmationDialog { DialogText = "Discard unsaved changes?" };
		_confirmDiscard.Confirmed += () => { _pendingAction?.Invoke(); _pendingAction = null; };
		AddChild(_confirmDiscard);

		_messageDialog = new AcceptDialog { Title = "Scenario" };
		AddChild(_messageDialog);

		_toolBox.ToolSelected += OnToolSelected;
		_inspector.DraftChanged += _structures.SetDraft;
		_inspector.ApplyRequested += ApplyStructure;
		_inspector.DeleteRequested += DeleteStructure;
		_inspector.NewRequested += () => _toolBox.SelectTool(ToolType.Structure);
		_inspector.CloseRequested += () => _toolBox.SelectTool(ToolType.None);
		_structures.SelectionChanged += OnStructureSelected;
		_structures.StructuresChanged += _inspector.RefreshDetails;
		_structures.PlacementRejected += _inspector.SetMessage;

		// load a scenario selected from the main menu, if one was selected
		string path = ScenarioLoadRequest.PendingPath;
		if (!string.IsNullOrEmpty(path))
		{
			ScenarioLoadRequest.PendingPath = null;
			ImportResult result = _scenario.LoadFrom(path);
			_toolBox.SelectTool(ToolType.None);
			ReportErrors(result);
		}
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
			case 0: // new
				RunUnlessDirty(NewScenario);
				break;
				
			case 1: // save
				if (_scenario.CurrentPath == null) ShowFileDialog(save: true);
				else ReportErrors(_scenario.SaveTo(_scenario.CurrentPath));
				break;

			case 2: // save as
				ShowFileDialog(save: true);
				break;

			case 3: // load
				RunUnlessDirty(() => ShowFileDialog(save: false));
				break;

			case 4: // open save folder
				OS.ShellShowInFileManager(ProjectSettings.GlobalizePath(SaveDir));
				break;

			case 5: // main menu
				GetTree().ChangeSceneToFile(MainMenuScene);
				break;

			case 6: // settings?
				break;

			case 7:	 // close app
				GetTree().Quit();
				break;
		}
	}
	
	private void NewScenario()
	{
		_scenario.NewScenario();
		_toolBox.SelectTool(ToolType.None);
	}

	// asks before throwing away unsaved work
	private void RunUnlessDirty(Action action)
	{
		if (_scenario.IsDirty)
		{
			_pendingAction = action;
			_confirmDiscard.PopupCentered();
		}
		else action();
	}

	private void ShowFileDialog(bool save)
	{
		_dialogIsSave = save;
		_fileDialog.FileMode = save ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile;
		_fileDialog.Title = save ? "Save Scenario" : "Load Scenario";
		_fileDialog.PopupCentered(new Vector2I(700, 450));
	}

	private void OnFileSelected(string path)
	{
		if (_dialogIsSave)
		{
			if (!path.EndsWith(".json")) path += ".json";
			ReportErrors(_scenario.SaveTo(path));
			return;
		}

		ImportResult result = _scenario.LoadFrom(path);
		_toolBox.SelectTool(ToolType.None);   // same reset as New
		ReportErrors(result);
	}

	private void ReportErrors(ImportResult result)
	{
		if (result.Success) return;
		_messageDialog.DialogText = string.Join("\n", result.Errors.Take(10));
		_messageDialog.PopupCentered();
	}
}

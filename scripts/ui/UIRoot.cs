using Godot;
using System;
using System.Linq;

public partial class UIRoot : CanvasLayer
{
	[Signal] public delegate void ToolChangedEventHandler(int tool);
	[Export(PropertyHint.File, "*.tscn")]
	public string MainMenuScene { get; set; } = "res://scenes/MainMenu.tscn";
	public ToolType CurrentTool { get; private set; } = ToolType.None;
	public bool IsSimulating => _drivers != null && _drivers.IsRunning;

	private ToolBox _toolBox;
	private InspectorPanel _inspector;
	private StructureGraph _structures;
	private DriverSpawner _drivers;
	private SimClock _clock;
	private SimManagerPanel _simulationPanel;
	private ScenarioManager _scenario;
	private GridView _grid;
	private RoadGraph _roads;
	private PasteController _paste;
	private SettingsPopup settingsWindow;

	// save/load dialogs
	private FileDialog _fileDialog;
	private ConfirmationDialog _confirmDiscard;
	private AcceptDialog _messageDialog;
	private bool _dialogIsSave;
	private Action _pendingAction;

	private const string SaveDir = "user://scenarios";

	// connects inspector actions, simulation controls, and save/load
	public override void _Ready()
	{
		_toolBox = GetNode<ToolBox>("ToolBox");
		_inspector = GetNode<InspectorPanel>("InspectorPanel");
		_structures = GetNode<StructureGraph>("../World/StructureGraph");
		_drivers = GetNode<DriverSpawner>("../World/DriverSpawner");
		_clock = GetNode<SimClock>("../World/SimClock");
		_scenario = GetNode<ScenarioManager>("../World/ScenarioManager");
		_grid = GetNode<GridView>("../World/GridView");
		_simulationPanel = GetNode<SimManagerPanel>("SimManagerPanel");
		_paste = GetNode<PasteController>("../World/PasteController");
		settingsWindow = new SettingsPopup();
		AddChild(settingsWindow);

		_simulationPanel.ToggleRequested += StartSimulation;
		_simulationPanel.StopRequested += StopSimulation;
		_drivers.StateChanged += RefreshSimulationControls;
		_clock.StateChanged += RefreshSimulationControls;
		_clock.DayEnded += OnDayEnded;

		_roads = GetNode<RoadGraph>("../World/RoadGraph");
		_inspector.SetParkingScale(_roads.CellSize, _roads.RoadWidth);
		//connect road settings to rod graph
		_inspector.RoadSettingsChanged += OnRoadSettingsChanged;

		//apply starting speed limit
		_roads.SetSpeedLimit(_inspector.SpeedLimit);

		//connect junction template dropdown to road graph and apply the starting template
		_inspector.JunctionTemplateChanged += _roads.SetJunctionTemplate;
		_roads.SetJunctionTemplate(_inspector.ChosenTemplate);
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

		RefreshSimulationControls();
	}

	// handles all keyboard shortcuts
	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true }) return;
		if (IsSimulating) return; // editing shortcuts are off during a run

		if (@event.IsActionPressed("toggle_erase", false, true)) _toolBox.ToggleErase();
		else if (@event.IsActionPressed("save", false, true)) OnSimMenuItemPressed(1);
		else if (@event.IsActionPressed("copy", false, true)) CopySelection();
		else if (@event.IsActionPressed("paste", false, true)) BeginPaste();
		else if (@event.IsActionPressed("ui_cancel", false, true))
		{
			if (_paste.IsActive) _paste.Cancel();
			else _roads.ClearSelection();
		}
		else if (@event.IsActionPressed("delete_selection", false, true))
		{
			if (_scenario.DeleteSelection(out string error) && error != "")
				_inspector.SetMessage(error);
		}
		else return;

		GetViewport().SetInputAsHandled();
	}

	//gets road settings whenever inspector changes
	private void OnRoadSettingsChanged(int lanesPerDirections, int speedLimit)
	{
		_roads.SetSpeedLimit(speedLimit);
	}

	// disconnects updates when leaving the simulator
	public override void _ExitTree()
	{
		if (_structures == null) return;
		_inspector.RoadSettingsChanged -= OnRoadSettingsChanged;
		_inspector.JunctionTemplateChanged -= _roads.SetJunctionTemplate;
		_structures.SelectionChanged -= OnStructureSelected;
		_structures.StructuresChanged -= _inspector.RefreshDetails;
		_structures.PlacementRejected -= _inspector.SetMessage;
		_drivers.StateChanged -= RefreshSimulationControls;
		_clock.StateChanged -= RefreshSimulationControls;
		_clock.DayEnded -= OnDayEnded;
		_simulationPanel.ToggleRequested -= StartSimulation;
		_simulationPanel.StopRequested -= StopSimulation;
	}

	// clears the old building selection before switching tools
	private void OnToolSelected(int tool)
	{
		if (IsSimulating && tool != (int)ToolType.None) return;
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
			_inspector.ChosenSize, _inspector.ChosenParkingRows, out string error, _inspector.ChosenDriverCount))
			_inspector.SetMessage(error);
		else _inspector.ShowStructure(_structures.Selected);
	}

	// removes the selected building or shows why it cannot be removed
	private void DeleteStructure()
	{
		if (!_structures.TryRemove(_structures.Selected, out string error))
			_inspector.SetMessage(error);
	}

	private void CopySelection()
	{
		ScenarioData data = _scenario.CaptureSelection();
		if (data == null) return; // keep the user's existing clipboard
        DisplayServer.ClipboardSet(ScenarioSerializer.ToJson(data));
        _roads.ClearSelection();
    }

    private void BeginPaste()
    {
        _toolBox.SelectTool(ToolType.None);   // fires ToolChanged, which cancels any old paste
        ReportErrors(_paste.Begin());
    }

	// starts a run or pauses and resumes the current run
	private void StartSimulation()
	{
		if (!_drivers.IsRunning)
		{
			if (!_drivers.TryStart(_clock.SpawnWindowSeconds, out string error))
			{
				_simulationPanel.SetMessage(error);
				return;
			}
			_clock.Reset();
		}
		_clock.Toggle();
	}

	// clears traffic and returns to build mode
	private void StopSimulation()
	{
		_drivers.Reset();
		_clock.Reset();
	}

	// updates the controls for the current simulation state
	private void RefreshSimulationControls()
	{
		if (!IsSimulating && _clock.Running) _clock.Toggle();
		_drivers.ProcessMode = _clock.Running ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
		_toolBox.SetDisabled(IsSimulating);
		_toolBox.Visible = !IsSimulating;
		_grid.SetEditingEnabled(!IsSimulating);
		if (IsSimulating)
		{
			if (CurrentTool != ToolType.None || _structures.Selected != null)
			{
				_toolBox.SelectTool(ToolType.None);
			}
			_inspector.Hide();
		}
		_simulationPanel.Refresh(IsSimulating, _drivers.SpawnedCount, _drivers.ActiveCount, _drivers.ConsumedCount, _drivers.ReturnedCount);
	}

	private void OnSimMenuItemPressed(long id)
	{
		// new, save, save as, and load change the map, so stop the run first
		if (IsSimulating && id <= 3)
		{
			_simulationPanel.SetMessage("Stop the simulation before saving or loading.");
			return;
		}

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

			case 6: // settings
				settingsWindow.ShowSettings();
				break;

			case 7: // close app
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

	private void OnDayEnded()
	{
		StopSimulation();
		_simulationPanel.SetMessage("Day complete.");
	}
}

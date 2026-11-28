using Godot;
using System;
using System.Collections.Generic;

public partial class InspectorPanel : Control
{

	// inspector panel road fields
	[ExportGroup("Lane Limits")]
	[Export] public int MinLanes { get; set; } = 1;
	[Export] public int MaxLanes { get; set; } = 4;

	[ExportGroup("Speed Limits")]
	[Export] public int MinSpeed { get; set; } = 15;
	[Export] public int MaxSpeed { get; set; } = 75;
	[Export] public int SpeedStep{ get; set; } = 5;

	public event Action<int, int> RoadSettingsChanged;
	public int LanesPerDirection { get; private set; } = 1;
	public int SpeedLimit { get; private set; } = 35;

	public event Action<StructureType, Vector2I, int, int> DraftChanged;
	public event Action ApplyRequested;
	public event Action DeleteRequested;
	public event Action NewRequested;
	public event Action CloseRequested;
	public StructureType ChosenType => (StructureType)_buildingType.GetSelectedId();
	public Vector2I ChosenSize => new Vector2I((int)_widthInput.Value, (int)_heightInput.Value);
	public int ChosenParkingRows => (int)_parkingRowsInput.Value;
	public int ChosenDriverCount => (int)_driverCountInput.Value;

	public event Action<JunctionTemplateType> JunctionTemplateChanged;
	public JunctionTemplateType ChosenTemplate => (JunctionTemplateType)_templateOption.GetSelectedId();

	public event Action<SignalType> SignalTypeChanged;
	public SignalType ChosenSignal => (SignalType)_signalOption.GetSelectedId();

	// signal given to templates placed with the junction tool
	public event Action<SignalType> PlacementSignalChanged;
	public SignalType PlacementSignal { get; private set; } = SignalType.None;
	// true while the dropdown edits a picked intersection instead of the placement signal
	private bool _editingJunction;

	// roads values and buttons
	private Label _laneValue;
	private Label _speedValue;
	private Button _decreaseLaneButton;
	private Button _increaseLaneButton;
	private Button _decreaseSpeedButton;
	private Button _increaseSpeedButton;

	private Control _lanesField;
	private Control _speedField;
	private Control _signalField;
	private Control _templateField;
	private OptionButton _templateOption;
	private OptionButton _signalOption;
	private VBoxContainer _buildingFields;
	private Control _typeIcon;
	private Label _titleLabel;
	private Label _typeLabel;
	private Label _subtitleLabel;
	private Label _detailsLabel;
	private Label _messageLabel;
	private OptionButton _buildingType;
	private SpinBox _widthInput;
	private SpinBox _heightInput;
	private SpinBox _parkingRowsInput;
	private SpinBox _driverCountInput;
	private Control _driverCountField;
	private Label _driverCountLabel;
	private readonly Dictionary<StructureType, (Vector2I Size, int ParkingRows, int DriverCount)> _buildingDefaults = new();
	private Button _closeButton;
	private Button _applyButton;
	private Button _deleteButton;
	private Button _newButton;
	private Structure _selectedStructure;
	private bool _updatingControls;
	private float _cellSize = 128.0f;
	private float _roadWidth = 32.0f;

	// uses the road's scale when showing parking capacity
    public void SetParkingScale(float cellSize, float roadWidth)
    {
        _cellSize = cellSize;
        _roadWidth = roadWidth;
        RefreshDetails();
    }

    public override void _Ready()
    {
        FindControls();
        PopulateBuildingTypes();
        PopulateJunctionTemplates();
        PopulateSignalTypes();
        LoadBuildingSettings(_buildingDefaults[ChosenType]);
        ConnectEvents();
        RefreshRoadLabels();
        Visible = false;
    }

    private void FindControls()
    {
        var content = GetNode<VBoxContainer>("PanelContainer/InspectorContent");
        _closeButton = content.GetNode<Button>("HeaderRow/CloseButton");
        _titleLabel = content.GetNode<Label>("HeaderRow/SelectedObjectLabel");
        _typeLabel = content.GetNode<Label>("TypeLabel");
        _subtitleLabel = content.GetNode<Label>("TypeSubtitle");
        _typeIcon = content.GetNode<Control>("TypeIcon");
        _lanesField = content.GetNode<Control>("LanesField");
        _speedField = content.GetNode<Control>("SpeedField");
        _signalField = content.GetNode<Control>("SignalField");
        _templateField = content.GetNode<Control>("TemplateField");
        _templateOption = _templateField.GetNode<OptionButton>("OptionButton");
        _signalOption = _signalField.GetNode<OptionButton>("OptionButton");

        _buildingFields = content.GetNode<VBoxContainer>("StructureFields");
        _buildingType = _buildingFields.GetNode<OptionButton>("BuildingType");
        _widthInput = _buildingFields.GetNode<SpinBox>("Width");
        _heightInput = _buildingFields.GetNode<SpinBox>("Height");
        _parkingRowsInput = _buildingFields.GetNode<SpinBox>("ParkingRows");
        _driverCountField = _buildingFields.GetNode<Control>("DriverCountField");
        _driverCountInput = _driverCountField.GetNode<SpinBox>("DriverCount");
        _driverCountLabel = _driverCountField.GetNode<Label>("Label");
        _detailsLabel = _buildingFields.GetNode<Label>("Details");
        _messageLabel = _buildingFields.GetNode<Label>("Message");
        _applyButton = _buildingFields.GetNode<Button>("ApplyButton");
        _newButton = _buildingFields.GetNode<Button>("NewButton");
        _deleteButton = _buildingFields.GetNode<Button>("DeleteButton");

        _laneValue = content.GetNode<Label>("LanesField/HBoxContainer/LaneValue");
        _speedValue = content.GetNode<Label>("SpeedField/HBoxContainer/SpeedValue");
        _decreaseLaneButton = content.GetNode<Button>("LanesField/HBoxContainer/DecreaseLaneButton");
        _increaseLaneButton = content.GetNode<Button>("LanesField/HBoxContainer/IncreaseLaneButton");
        _decreaseSpeedButton = content.GetNode<Button>("SpeedField/HBoxContainer/DecreaseSpeedButton");
        _increaseSpeedButton = content.GetNode<Button>("SpeedField/HBoxContainer/IncreaseSpeedButton");
    }

    // keep dropdown choices matched to the building types
    private void PopulateBuildingTypes()
    {
        foreach (StructureType type in Enum.GetValues<StructureType>())
        {
            _buildingType.AddItem(type.ToString(), (int)type);
            int driverCount = type switch
            {
                StructureType.House => 1,
                StructureType.Producer => 10,
                _ => 0
            };
            bool endpoint = Structure.IsTrafficEndpoint(type);
            Vector2I size = endpoint ? Vector2I.One : ChosenSize;
            int parkingRows = Structure.HasParking(type) ? ChosenParkingRows : 0;
            _buildingDefaults[type] = (size, parkingRows, driverCount);
        }
    }

    // keep dropdown choices matched to the junction templates
    private void PopulateJunctionTemplates()
    {
        _templateOption.Clear();
        foreach (JunctionTemplateType type in Enum.GetValues<JunctionTemplateType>())
        {
            _templateOption.AddItem(type.ToString(), (int)type);
        }
        _templateOption.Select(0);
    }

    // keep dropdown choices matched to the signal types
    private void PopulateSignalTypes()
    {
        _signalOption.Clear();
        foreach (SignalType type in Enum.GetValues<SignalType>())
        {
            string name = type switch
            {
                SignalType.AllWayStop => "All-way stop",
                SignalType.TwoWayStop => "Two-way stop",
                SignalType.TrafficLight => "Traffic light",
                _ => "None"
            };
            _signalOption.AddItem(name, (int)type);
        }
        _signalOption.Select(0);
    }

    // sends button actions and field changes to the rest of the ui
    private void ConnectEvents()
    {
        _closeButton.Pressed += () => CloseRequested?.Invoke();
        _applyButton.Pressed += () => ApplyRequested?.Invoke();
        _newButton.Pressed += () => NewRequested?.Invoke();
        _deleteButton.Pressed += () => DeleteRequested?.Invoke();

        _buildingType.ItemSelected += OnBuildingTypeSelected;
        _templateOption.ItemSelected += _ => JunctionTemplateChanged?.Invoke(ChosenTemplate);
        _signalOption.ItemSelected += _ =>
        {
            if (_updatingControls) return;

            if (_editingJunction)
            {
                SignalTypeChanged?.Invoke(ChosenSignal);
            }
            else
            {
                PlacementSignal = ChosenSignal;
                PlacementSignalChanged?.Invoke(PlacementSignal);
            }
        };
        _widthInput.ValueChanged += _ => OnDraftChanged();
        _heightInput.ValueChanged += _ => OnDraftChanged();
        _parkingRowsInput.ValueChanged += _ => OnDraftChanged();
        _driverCountInput.ValueChanged += _ => OnDraftChanged();

        _decreaseLaneButton.Pressed += () => SetLanes(LanesPerDirection - 1);
        _increaseLaneButton.Pressed += () => SetLanes(LanesPerDirection + 1);
        _decreaseSpeedButton.Pressed += () => SetSpeed(SpeedLimit - SpeedStep);
        _increaseSpeedButton.Pressed += () => SetSpeed(SpeedLimit + SpeedStep);
    }

    // each building type saves its own settings
    // should make placing multiple buildings of the same type much easier
    private void OnBuildingTypeSelected(long index)
    {
        if (_updatingControls) return;
        LoadBuildingSettings(_buildingDefaults[ChosenType]);
        OnDraftChanged();
    }

    // keeps room for the building and sends an update for each user change
    private void OnDraftChanged()
    {
        if (_updatingControls)
        {
            return;
        }

        _updatingControls = true;
        _parkingRowsInput.MaxValue = Structure.HasParking(ChosenType) ? _heightInput.Value - 1 : 0;
        _updatingControls = false;

        _buildingDefaults[ChosenType] = (ChosenSize, ChosenParkingRows, ChosenDriverCount);
        DraftChanged?.Invoke(ChosenType, ChosenSize, ChosenParkingRows, ChosenDriverCount);
        RefreshDetails();
        SetMessage("");
    }

    // clears the building selection and shows settings for the chosen tool
    public void ShowForTool(ToolType tool)
    {
        _selectedStructure = null;
        _editingJunction = false;
        ShowFieldsForTool(tool);

        if (tool == ToolType.Structure)
        {
            LoadBuildingSettings(_buildingDefaults[ChosenType]);
            DraftChanged?.Invoke(ChosenType, ChosenSize, ChosenParkingRows, ChosenDriverCount);
            _titleLabel.Text = "New building";
            _typeLabel.Text = "Click to place | Press R to rotate";
            _subtitleLabel.Text = "Click an existing building to edit";
        }
        else if (tool == ToolType.Junction)
        {
            _titleLabel.Text = "Junction Tool";
            _typeLabel.Text = "Click to place | Press R to rotate";
            _subtitleLabel.Text = "New junctions get the signal below";

            // the dropdown shows the placement signal until an intersection is picked
            _updatingControls = true;
            _signalOption.Select(_signalOption.GetItemIndex((int)PlacementSignal));
            _updatingControls = false;
        }
        else
        {
            _titleLabel.Text = $"{tool} Tool";
            _typeLabel.Text = "Drag to place a road";
            _subtitleLabel.Text = "Shift + Drag to delete";
        }

        SetMessage("");
        RefreshDetails();
    }

	// shows the signal settings of the intersection picked with the junction tool
	public void ShowJunction(Vector2I cell, SignalSettings signal)
	{
		_selectedStructure = null;
		ShowFieldsForTool(ToolType.Junction);
		_templateField.Visible = false; // editing a signal, not placing a template
		_editingJunction = true;

		_updatingControls = true;
		_signalOption.Select(_signalOption.GetItemIndex((int)signal.Type));
		_updatingControls = false;

		_titleLabel.Text = "Selected: Intersection";
		_typeLabel.Text = $"Grid ({cell.X}, {cell.Y})";
		_subtitleLabel.Text = "Pick a signal | Esc to deselect";
	}

	// loads a selected building's settings into the inspector
	public void ShowStructure(Structure structure)
	{
		_selectedStructure = structure;
		ShowFieldsForTool(ToolType.Structure);
		LoadBuildingValues(structure);

		_titleLabel.Text = $"Selected: {structure.Type}";
		_typeLabel.Text = $"Grid ({structure.AnchorCell.X}, {structure.AnchorCell.Y})";
		_subtitleLabel.Text = $"Edit the building · {structure.RotationSteps * 90}° left";
		SetMessage("");

		DraftChanged?.Invoke(ChosenType, ChosenSize, ChosenParkingRows, ChosenDriverCount);
		RefreshDetails();
	}

	// shows only the fields used by the active tool
	private void ShowFieldsForTool(ToolType tool)
	{
		bool roadSelected = tool == ToolType.Road;
		bool junctionSelected = tool == ToolType.Junction;
		bool buildingSelected = tool == ToolType.Structure;
       
        _signalField.Visible = junctionSelected;
		_lanesField.Visible = roadSelected;
		_speedField.Visible = roadSelected;
		_templateField.Visible = junctionSelected;
		_buildingFields.Visible = buildingSelected;
		_typeIcon.Visible = !buildingSelected;
		Visible = roadSelected || junctionSelected || buildingSelected;
	}

	// fills the fields without sending extra draft changes
	private void LoadBuildingValues(Structure structure)
	{
		_buildingType.Select(_buildingType.GetItemIndex((int)structure.Type));
		LoadBuildingSettings((structure.SizeInCells, structure.ParkingRows, structure.DriverCount));
	}

	private void LoadBuildingSettings((Vector2I Size, int ParkingRows, int DriverCount) settings)
	{
		_updatingControls = true;
		bool endpoint = Structure.IsTrafficEndpoint(ChosenType);
		bool parking = Structure.HasParking(ChosenType);
		_heightInput.MinValue = parking ? 2 : 1;
		_parkingRowsInput.MinValue = parking ? 1 : 0;
		_widthInput.Value = settings.Size.X;
		_heightInput.Value = settings.Size.Y;
		_parkingRowsInput.MaxValue = parking ? settings.Size.Y - 1 : 0;
		_parkingRowsInput.Value = settings.ParkingRows;
		_widthInput.Visible = !endpoint;
		_heightInput.Visible = !endpoint;
		_parkingRowsInput.Visible = parking;
		_buildingFields.GetNode<Control>("WidthLabel").Visible = !endpoint;
		_buildingFields.GetNode<Control>("HeightLabel").Visible = !endpoint;
		_buildingFields.GetNode<Control>("ParkingRowsLabel").Visible = parking;
		int driverLimit = Structure.GetDriverLimit(ChosenType);
		_driverCountField.Visible = driverLimit > 0;
		_driverCountInput.MaxValue = driverLimit;
		_driverCountInput.Value = settings.DriverCount;
		_driverCountLabel.Text = ChosenType == StructureType.House ? "Resident drivers (0–4)" : "Total drivers per run";
		_updatingControls = false;
	}

	// updates the structure's details and edit buttons
    public void RefreshDetails()
    {
        bool hasSelection = _selectedStructure != null;
        _applyButton.Visible = hasSelection;
        _deleteButton.Visible = hasSelection;
        _newButton.Visible = hasSelection;

        _detailsLabel.Visible = ChosenType != StructureType.House;
        if (!_detailsLabel.Visible) return;

        if (Structure.IsTrafficEndpoint(ChosenType))
        {
            _detailsLabel.Text = _selectedStructure?.IsConnected == true
                ? $"Road access: {_selectedStructure.AccessCell}"
				: "Connect a road at the endpoint's facing edge.";
            return;
        }

        if (_selectedStructure == null)
        {
            var parking = new ParkingLayout(Vector2I.Zero, ChosenSize, ChosenParkingRows, _cellSize, _roadWidth);
            _detailsLabel.Text = $"Parking capacity: {parking.Capacity}\n";
            return;
        }

		string roadAccess = "No road access.";
		if (_selectedStructure.IsConnected)
		{
			roadAccess = $"Road access: {_selectedStructure.AccessCell}";
		}

		_detailsLabel.Text = $"Parking reserved: {_selectedStructure.ReservedCount} / " +
			$"{_selectedStructure.ParkingCapacity}\n{roadAccess}";
	}

    // keep lane count between range
    private void SetLanes(int value)
    {
        value = Mathf.Clamp(value, MinLanes, MaxLanes);
        if (value == LanesPerDirection)
        {
            return;
        }

        LanesPerDirection = value;
        OnRoadSettingsChanged();
    }

    // keep speed limits between range
    private void SetSpeed(int value)
    {
        value = Mathf.Clamp(value, MinSpeed, MaxSpeed);
        if (value == SpeedLimit)
        {
            return;
        }

        SpeedLimit = value;
        OnRoadSettingsChanged();
    }

    // updates inspector road labels
    private void OnRoadSettingsChanged()
    {
        RefreshRoadLabels();
        RoadSettingsChanged?.Invoke(LanesPerDirection, SpeedLimit);
    }

    private void RefreshRoadLabels()
    {
        _laneValue.Text = LanesPerDirection.ToString();
        _speedValue.Text = SpeedLimit.ToString();
    }

    public void SetMessage(string message) => _messageLabel.Text = message;
}
using Godot;
using System;

public partial class InspectorPanel : Control
{
    public event Action<StructureType, Vector2I, int> DraftChanged;
    public event Action ApplyRequested;
    public event Action DeleteRequested;
    public event Action NewRequested;
    public event Action CloseRequested;
    public StructureType ChosenType => (StructureType)_buildingType.GetSelectedId();
    public Vector2I ChosenSize => new Vector2I((int)_widthInput.Value, (int)_heightInput.Value);
    public int ChosenParkingRows => (int)_parkingRowsInput.Value;

    private Control _lanesField;
    private Control _speedField;
    private Control _signalField;
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
        ConnectEvents();
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

        _buildingFields = content.GetNode<VBoxContainer>("StructureFields");
        _buildingType = _buildingFields.GetNode<OptionButton>("BuildingType");
        _widthInput = _buildingFields.GetNode<SpinBox>("Width");
        _heightInput = _buildingFields.GetNode<SpinBox>("Height");
        _parkingRowsInput = _buildingFields.GetNode<SpinBox>("ParkingRows");
        _detailsLabel = _buildingFields.GetNode<Label>("Details");
        _messageLabel = _buildingFields.GetNode<Label>("Message");
        _applyButton = _buildingFields.GetNode<Button>("ApplyButton");
        _newButton = _buildingFields.GetNode<Button>("NewButton");
        _deleteButton = _buildingFields.GetNode<Button>("DeleteButton");
    }

    // keep dropdown choices matched to the building types
    private void PopulateBuildingTypes()
    {
        foreach (StructureType type in Enum.GetValues<StructureType>())
        {
            _buildingType.AddItem(type.ToString(), (int)type);
        }
    }

    // sends button actions and field changes to the rest of the ui
    private void ConnectEvents()
    {
        _closeButton.Pressed += () => CloseRequested?.Invoke();
        _applyButton.Pressed += () => ApplyRequested?.Invoke();
        _newButton.Pressed += () => NewRequested?.Invoke();
        _deleteButton.Pressed += () => DeleteRequested?.Invoke();

        _buildingType.ItemSelected += _ => OnDraftChanged();
        _widthInput.ValueChanged += _ => OnDraftChanged();
        _heightInput.ValueChanged += _ => OnDraftChanged();
        _parkingRowsInput.ValueChanged += _ => OnDraftChanged();
    }

    // keeps room for the building and sends an update for each user change
    private void OnDraftChanged()
    {
        if (_updatingControls)
        {
            return;
        }

        _updatingControls = true;
        _parkingRowsInput.MaxValue = _heightInput.Value - 1;
        _updatingControls = false;

        DraftChanged?.Invoke(ChosenType, ChosenSize, ChosenParkingRows);
        RefreshDetails();
        SetMessage("");
    }

    // clears the building selection and shows settings for the chosen tool
    public void ShowForTool(ToolType tool)
    {
        _selectedStructure = null;
        ShowFieldsForTool(tool);

        if (tool == ToolType.Structure)
        {
            _titleLabel.Text = "New building";
            _typeLabel.Text = "Click an empty cell to place";
            _subtitleLabel.Text = "Click an existing building to edit";
        }
        else
        {
            _titleLabel.Text = $"Selected: {tool}";
            _typeLabel.Text = tool.ToString();
            _subtitleLabel.Text = "";
        }

        SetMessage("");
        RefreshDetails();
    }

    // loads a selected building's settings into the inspector
    public void ShowStructure(Structure structure)
    {
        _selectedStructure = structure;
        ShowFieldsForTool(ToolType.Structure);
        LoadBuildingValues(structure);

        _titleLabel.Text = $"Selected: {structure.Type}";
        _typeLabel.Text = $"Grid ({structure.AnchorCell.X}, {structure.AnchorCell.Y})";
        _subtitleLabel.Text = "Edit the building";
        SetMessage("");

        DraftChanged?.Invoke(ChosenType, ChosenSize, ChosenParkingRows);
        RefreshDetails();
    }

    // shows only the fields used by the active tool
    private void ShowFieldsForTool(ToolType tool)
    {
        bool roadSelected = tool == ToolType.Road;
        bool junctionSelected = tool == ToolType.Junction;
        bool buildingSelected = tool == ToolType.Structure;

        _lanesField.Visible = roadSelected;
        _speedField.Visible = roadSelected;
        _signalField.Visible = junctionSelected;
        _buildingFields.Visible = buildingSelected;
        _typeIcon.Visible = !buildingSelected;
        Visible = roadSelected || junctionSelected || buildingSelected;
    }

    // fills the fields without sending extra draft changes
    private void LoadBuildingValues(Structure structure)
    {
        _updatingControls = true;
        _buildingType.Select(_buildingType.GetItemIndex((int)structure.Type));
        _widthInput.Value = structure.SizeInCells.X;
        _heightInput.Value = structure.SizeInCells.Y;
        _parkingRowsInput.MaxValue = structure.SizeInCells.Y - 1;
        _parkingRowsInput.Value = structure.ParkingRows;
        _updatingControls = false;
    }

    // updates parking details and shows edit buttons only for an existing building
    public void RefreshDetails()
    {
        bool hasSelection = _selectedStructure != null;
        _applyButton.Visible = hasSelection;
        _deleteButton.Visible = hasSelection;
        _newButton.Visible = hasSelection;

        if (_selectedStructure == null)
        {
            var parking = new ParkingLayout(Vector2I.Zero, ChosenSize, ChosenParkingRows, _cellSize, _roadWidth);
            _detailsLabel.Text = $"Parking capacity: {parking.Capacity}\n" +
                "Connect a road just below the lot.";
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

    public void SetMessage(string message) => _messageLabel.Text = message;
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class StructureGraph : Node2D
{
	[ExportGroup("Building Colors")]
	[Export] public Color HouseColor { get; set; } = new Color(0.84f, 0.91f, 1.0f);
	[Export] public Color ShopColor { get; set; } = new Color(1.0f, 0.89f, 0.91f);
	[Export] public Color SchoolColor { get; set; } = new Color(1.0f, 0.94f, 0.83f);
	[Export] public Color ParkingColor { get; set; } = new Color(0.36f, 0.39f, 0.45f);

	// TODO: building icons
	[ExportGroup("Building Icons")]
	[Export] public Texture2D HouseIcon { get; set; }
	[Export] public Texture2D ShopIcon { get; set; }
	[Export] public Texture2D SchoolIcon { get; set; }

	private RoadGraph _roads;
	private LaneGraph _lanes;
	private UIRoot _ui;
	private GridView _grid;
	private readonly List<Structure> _structures = new();
	public IReadOnlyList<Structure> Structures => _structures.AsReadOnly();
	public Structure Selected { get; private set; }
	public StructureType DraftType { get; private set; } = StructureType.House;
	public Vector2I DraftSize { get; private set; } = new Vector2I(3, 3);
	public int DraftParkingRows { get; private set; } = 1;
	public event Action<Structure> SelectionChanged;
	public event Action StructuresChanged;
	public event Action<string> PlacementRejected;
	private bool _showPreview;
	private Vector2I _hoverCell;

	private StyleBoxFlat _plotStyle;
	private StyleBoxFlat _houseStyle;
	private StyleBoxFlat _shopStyle;
	private StyleBoxFlat _schoolStyle;
	private StyleBoxFlat _parkingStyle;
	private StyleBoxFlat _shadowStyle;
	private StyleBoxFlat _iconSlotStyle;
	private StyleBoxFlat _reservedSpaceStyle;
	private StyleBoxFlat _selectionStyle;
	private StyleBoxFlat _validPreviewStyle;
	private StyleBoxFlat _invalidPreviewStyle;
	private Font _font;
	private readonly Color _textColor = new Color(0.08f, 0.13f, 0.28f);

	// connects buildings to road changes and various drawing settings
	public override void _Ready()
	{
		_roads = GetNode<RoadGraph>("../RoadGraph");
		_lanes = GetNode<LaneGraph>("../LaneGraph");
		_ui = GetNode<UIRoot>("../../UI");
		_grid = GetNode<GridView>("../GridView");
		CreateStyles();
		_roads.RoadsChanged += RefreshRoadConnections;
		_roads.DeleteCellsRequested += DeleteInBounds;
	}

	// stops listening for changes when this map is closed
	public override void _ExitTree()
	{
		if (_roads == null) return;
		_roads.RoadsChanged -= RefreshRoadConnections;
		_roads.DeleteCellsRequested -= DeleteInBounds;
		foreach (Structure structure in _structures) structure.ParkingChanged -= Changed;
	}

	// updates the placement preview
	public void SetDraft(StructureType type, Vector2I size, int parkingRows)
	{
		if (!Structure.IsValid(type, size, parkingRows)) return;
		DraftType = type;
		DraftSize = size;
		DraftParkingRows = parkingRows;
		QueueRedraw();
	}

	public bool IsCellOccupied(Vector2I cell) =>
		_structures.Any(structure => structure.Bounds.HasPoint(cell));

	public Structure GetAt(Vector2I cell) =>
		_structures.FirstOrDefault(structure => structure.Bounds.HasPoint(cell));

	// checks parking room and overlaps, ignoring the building being edited
	public bool CanPlace(Vector2I anchor, StructureType type, Vector2I size, int parkingRows, out string error, Structure ignore = null)
	{
		error = "";
		if (!Structure.IsValid(type, size, parkingRows))
		{
			error = "Width must be between 1 and 32, height between 2 and 32.";
			return false;
		}

		var parking = new ParkingLayout(anchor, size, parkingRows, _roads.CellSize, _roads.RoadWidth);
		if (parking.Capacity == 0)
		{
			error = "The parking area needs more room for spaces and a driving aisle.";
			return false;
		}
		if (parking.Capacity > ParkingLayout.MaximumCapacity)
		{
			error = "Reduce the parking area for this road scale.";
			return false;
		}

		Rect2I bounds = new Rect2I(anchor, size);
		if (_structures.Any(s => s != ignore && s.Bounds.Intersects(bounds)))
		{
			error = "That building overlaps another existing building.";
		}
		else if (_roads.Segments.Any(s => s.Path.Any(cell => bounds.HasPoint(cell))))
		{
			error = "That building overlaps an existing road segment.";
		}
		return error.Length == 0;
	}

	// adds a valid building, creates its parking lanes, and selects it
	public bool TryPlace(Vector2I anchor, StructureType type, Vector2I size,
		int parkingRows, out Structure structure, out string error)
	{
		structure = null;
		if (!CanPlace(anchor, type, size, parkingRows, out error)) return false;
		structure = new Structure(anchor, type, size, parkingRows, _roads.CellSize, _roads.RoadWidth);
		structure.CreateParkingLanes(_lanes);
		_structures.Add(structure);
		structure.ParkingChanged += Changed;
		ConnectToRoad(structure);
		Select(structure);
		Changed();
		return true;
	}

	// rebuilds parking only when the layout changes
	public bool TryEdit(Structure structure, StructureType type, Vector2I size,
		int parkingRows, out string error)
	{
		error = "Select a building first.";
		if (!_structures.Contains(structure)) return false;

		bool layoutChanged = structure.SizeInCells != size || structure.ParkingRows != parkingRows;
		if (!CanPlace(structure.AnchorCell, type, size, parkingRows, out error, structure))
		{
			return false;
		}

		if (layoutChanged)
		{
			structure.RemoveParkingLanes(_lanes);
		}

		structure.Configure(type, size, parkingRows);

		if (layoutChanged)
		{
			structure.CreateParkingLanes(_lanes);
			ConnectToRoad(structure);
		}

		Changed();
		return true;
	}

	// removes a building and cleans up its parking connections
	public bool TryRemove(Structure structure, out string error)
	{
		error = "Select a building first.";
		if (!_structures.Contains(structure)) return false;
		structure.RemoveParkingLanes(_lanes);
		structure.ParkingChanged -= Changed;
		structure.AccessRoad = null;
		structure.AccessCell = null;
		_structures.Remove(structure);
		if (Selected == structure) Select(null);
		error = "";
		Changed();
		return true;
	}

	public void Select(Structure structure)
	{
		Selected = structure;
		SelectionChanged?.Invoke(structure);
		QueueRedraw();
	}

	// removes whole buildings touched by the deletion box
	private void DeleteInBounds(Vector2I min, Vector2I max)
	{
		Rect2I bounds = new Rect2I(min, max - min + Vector2I.One);
		foreach (Structure structure in _structures.Where(s => s.Bounds.Intersects(bounds)).ToList())
			if (!TryRemove(structure, out string error)) PlacementRejected?.Invoke(error);
	}

	// checks each building's access after roads are added or removed
	private void RefreshRoadConnections()
	{
		foreach (Structure structure in _structures) ConnectToRoad(structure);
		Changed();
	}

	// replaces old road access with an endpoint just below the parking lot
	private void ConnectToRoad(Structure structure)
	{
		if (structure.AccessRoad != null)
		{
			RoadSegment previous = structure.AccessRoad;
			IEnumerable<Lane> incoming = previous.A == structure.AccessCell ? previous.LanesBtoA : previous.LanesAtoB;
			IEnumerable<Lane> outgoing = previous.A == structure.AccessCell ? previous.LanesAtoB : previous.LanesBtoA;
			foreach (Lane lane in incoming)
			{
				_lanes.Disconnect(lane.Id, structure.EntryLane.Id);
			}
			foreach (Lane lane in outgoing)
			{
				_lanes.Disconnect(structure.ExitLane.Id, lane.Id);
			}
		}

		structure.AccessRoad = null;
		structure.AccessCell = null;

		int y = structure.AnchorCell.Y + structure.SizeInCells.Y;
		for (int x = structure.AnchorCell.X; x < structure.Bounds.End.X; x++)
		{
			Vector2I cell = new Vector2I(x, y);
			RoadSegment road = _roads.Segments.FirstOrDefault(s => s.A == cell || s.B == cell);
			if (road == null) continue;
			structure.AccessRoad = road;
			structure.AccessCell = cell;
			IEnumerable<Lane> incoming = road.A == cell ? road.LanesBtoA : road.LanesAtoB;
			IEnumerable<Lane> outgoing = road.A == cell ? road.LanesAtoB : road.LanesBtoA;
			foreach (Lane lane in incoming)
			{
				_lanes.Connect(lane.Id, structure.EntryLane.Id);
			}
			foreach (Lane lane in outgoing)
			{
				_lanes.Connect(structure.ExitLane.Id, lane.Id);
			}
			return;
		}
	}

	private void Changed()
	{
		if (_batchDepth > 0)
		{
			_changedWhileBatching = true;
			return;
		}
		StructuresChanged?.Invoke();
		QueueRedraw();
	}

	// tracks the hovered cell and shows the preview while placing a new building
	public override void _Process(double delta)
	{
		Vector2I hoveredCell = _roads.GlobalToCell(GetGlobalMousePosition());
		bool showPreview = _ui.CurrentTool == ToolType.Structure && Selected == null &&
			!_roads.IsDeleteSelecting && GetViewport().GuiGetHoveredControl() == null;
		bool previewChanged = showPreview != _showPreview;
		bool hoveredCellChanged = hoveredCell != _hoverCell;

		_hoverCell = hoveredCell;
		_showPreview = showPreview;
		
		if (previewChanged || (_showPreview && hoveredCellChanged))
		{
			QueueRedraw();
		}
	}

	// handles building selection, placement, and clearing the selection
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel") && Selected != null)
		{
			Select(null);
			GetViewport().SetInputAsHandled();
			return;
		}
		if (@event is not InputEventMouseButton mouse || mouse.ButtonIndex != MouseButton.Left ||
			!mouse.Pressed || mouse.ShiftPressed || _roads.IsDeleteSelecting) return;
		if (_ui.CurrentTool != ToolType.Structure && _ui.CurrentTool != ToolType.None) return;

		Vector2I cell = _roads.GlobalToCell(GetGlobalMousePosition());
		Structure existing = GetAt(cell);
		if (existing != null) Select(existing);
		else if (_ui.CurrentTool == ToolType.Structure && Selected == null)
		{
			if (!TryPlace(cell, DraftType, DraftSize, DraftParkingRows, out _, out string error))
				PlacementRejected?.Invoke(error);
		}
		else Select(null);
		GetViewport().SetInputAsHandled();
	}

	// draws placed buildings and the current placement preview
	public override void _Draw()
	{
		if (_roads == null) return;
		foreach (Structure structure in _structures)
			DrawStructure(structure.AnchorCell, structure.Type, structure.SizeInCells,
				structure.ParkingRows, structure, false);
		if (_showPreview)
			DrawStructure(_hoverCell, DraftType, DraftSize, DraftParkingRows, null, true);
	}

	// creates reusable styles so drawing does not need new ones each time
	private void CreateStyles()
	{
		Color border = new Color(0.97f, 0.99f, 1.0f);
		int radius = Mathf.Max(1, Mathf.RoundToInt(_roads.CellSize * 0.09f));
		int roofBorder = Mathf.Max(1, Mathf.RoundToInt(_roads.CellSize * 0.055f));

		_plotStyle = CreateStyle(_grid.BackgroundColor, radius);
		_houseStyle = CreateStyle(HouseColor, 2, roofBorder, border);
		_shopStyle = CreateStyle(ShopColor, 2, roofBorder, border);
		_schoolStyle = CreateStyle(SchoolColor, 2, roofBorder, border);
		_parkingStyle = CreateStyle(ParkingColor, radius / 2, 2, border);
		_shadowStyle = CreateStyle(new Color(0.12f, 0.26f, 0.22f, 0.18f), 2);
		_iconSlotStyle = CreateStyle(new Color(1, 1, 1, 0.22f), radius / 2, 1, new Color(1, 1, 1, 0.45f));
		_reservedSpaceStyle = CreateStyle(new Color(0.98f, 0.76f, 0.35f), radius / 3);
		_selectionStyle = CreateStyle(Colors.Transparent, radius, 3, new Color(0.20f, 0.55f, 1));
		_validPreviewStyle = CreateStyle(new Color(0.1f, 0.75f, 0.35f, 0.12f), radius, 3, new Color(0.1f, 0.7f, 0.35f));
		_invalidPreviewStyle = CreateStyle(new Color(1, 0.2f, 0.25f, 0.18f), radius, 3, new Color(0.95f, 0.25f, 0.3f));
		_font = ThemeDB.FallbackFont;
	}

	private static StyleBoxFlat CreateStyle(Color color, int radius, int borderWidth = 0, Color borderColor = default)
	{
		var style = new StyleBoxFlat
		{
			BgColor = color,
			BorderColor = borderColor
		};
		style.SetCornerRadiusAll(radius);
		style.SetBorderWidthAll(borderWidth);
		return style;
	}

	// draws the building and lot together, with a selection or placement outline
	private void DrawStructure(Vector2I anchor, StructureType type, Vector2I size,
		int parkingRows, Structure structure, bool preview)
	{
		float cellSize = _roads.CellSize;
		Rect2 footprint = _roads.GetMultiCellLocalRect(anchor, size);
		Rect2 grounds = footprint.Grow(-cellSize * 0.06f);
		Rect2 buildingArea = _roads.GetMultiCellLocalRect(anchor, new Vector2I(size.X, size.Y - parkingRows));
		Rect2 roof = new Rect2(
			buildingArea.Position + new Vector2(cellSize * 0.30f, cellSize * 0.24f),
			buildingArea.Size - new Vector2(cellSize * 0.60f, cellSize * 0.38f));
		ParkingLayout parking = structure?.Parking;
		if (parking == null)
		{
			parking = new ParkingLayout(anchor, size, parkingRows, cellSize, _roads.RoadWidth);
		}

		DrawStyleBox(_plotStyle, grounds);
		DrawParkingSpaces(parking, structure);
		DrawWalkway(roof, parking.Lot);
		DrawBuilding(roof, type);
		DrawRoadAccess(parking.Lot, structure);

		if (preview)
		{
			if (CanPlace(anchor, type, size, parkingRows, out _))
			{
				DrawStyleBox(_validPreviewStyle, grounds);
			}
			else
			{
				DrawStyleBox(_invalidPreviewStyle, grounds);
			}
		}
		else if (structure == Selected)
		{
			DrawStyleBox(_selectionStyle, grounds);
		}
	}

	// fits the building's icon slot and label inside its roof
	private void DrawBuilding(Rect2 roof, StructureType type)
	{
		StyleBoxFlat roofStyle = _houseStyle;
		Texture2D icon = HouseIcon;
		switch (type)
		{
			case StructureType.Shop:
				roofStyle = _shopStyle;
				icon = ShopIcon;
				break;
			case StructureType.School:
				roofStyle = _schoolStyle;
				icon = SchoolIcon;
				break;
		}

		Vector2 shadowOffset = Vector2.One * _roads.CellSize * 0.055f;
		DrawStyleBox(_shadowStyle, new Rect2(roof.Position + shadowOffset, roof.Size));
		DrawStyleBox(roofStyle, roof);

		string label = type.ToString();
		float padding = _roads.CellSize * 0.08f;
		float labelWidth = roof.Size.X - padding * 2;
		float labelSize = Mathf.Min(_roads.CellSize * 0.20f, roof.Size.Y * 0.22f);
		int fontSize = Mathf.Max(6, Mathf.RoundToInt(labelSize));
		float textWidth = _font.GetStringSize(label, HorizontalAlignment.Left, -1, fontSize).X;
		if (textWidth > labelWidth)
		{
			fontSize = Mathf.Max(6, Mathf.FloorToInt(fontSize * labelWidth / textWidth));
		}

		float iconSize = Mathf.Min(_roads.CellSize * 0.46f, Mathf.Min(roof.Size.X * 0.45f, roof.Size.Y * 0.35f));
		float gap = _roads.CellSize * 0.07f;
		float contentHeight = iconSize + gap + _font.GetHeight(fontSize);
		float contentTop = roof.Position.Y + (roof.Size.Y - contentHeight) * 0.5f;
		Rect2 iconSlot = new Rect2(
			new Vector2(roof.GetCenter().X - iconSize * 0.5f, contentTop),
			Vector2.One * iconSize);

		DrawStyleBox(_iconSlotStyle, iconSlot);
		DrawBuildingIcon(icon, iconSlot);
		Vector2 labelPosition = new Vector2(roof.Position.X + padding,
			iconSlot.End.Y + gap + _font.GetAscent(fontSize));
		DrawString(_font, labelPosition, label, HorizontalAlignment.Center, labelWidth, fontSize, _textColor);
	}

	private void DrawBuildingIcon(Texture2D icon, Rect2 slot)
	{
		if (icon == null)
		{
			return;
		}

		Vector2 textureSize = icon.GetSize();
		if (textureSize.X <= 0 || textureSize.Y <= 0)
		{
			return;
		}

		// keep the icon's shape when fitting it inside the slot
		float scale = Mathf.Min(slot.Size.X / textureSize.X, slot.Size.Y / textureSize.Y) * 0.8f;
		Vector2 drawSize = textureSize * scale;
		Rect2 iconRect = new Rect2(slot.GetCenter() - drawSize * 0.5f, drawSize);
		DrawTextureRect(icon, iconRect, false);
	}

	// paints the lot and spaces, then marks any reserved spaces
	private void DrawParkingSpaces(ParkingLayout parking, Structure structure)
	{
		DrawStyleBox(_parkingStyle, parking.Lot);
		if (parking.Capacity == 0 || parking.Capacity > ParkingLayout.MaximumCapacity)
		{
			return;
		}

		Color stripeColor = new Color(0.96f, 0.98f, 1);
		float stripeWidth = Mathf.Max(1, _roads.RoadWidth * 0.04f);

		for (int row = 0; row < parking.Rows; row++)
		{
			Rect2 aisle = parking.GetAisleRect(row);
			Rect2 firstSpace = parking.GetSpaceRect(row * parking.Columns);
			float right = firstSpace.Position.X + parking.Columns * parking.SpaceSize.X;
			DrawLine(firstSpace.Position, new Vector2(right, firstSpace.Position.Y), stripeColor, stripeWidth, true);

			for (int column = 0; column <= parking.Columns; column++)
			{
				float x = firstSpace.Position.X + column * parking.SpaceSize.X;
				DrawLine(new Vector2(x, firstSpace.Position.Y), new Vector2(x, aisle.Position.Y),
					stripeColor, stripeWidth, true);
			}
		}

		if (structure == null || structure.ReservedCount == 0)
		{
			return;
		}

		for (int i = 0; i < structure.ParkingCapacity; i++)
		{
			if (!structure.IsSpaceReserved(i))
			{
				continue;
			}

			Vector2 center = structure.GetParkingPosition(i);
			Vector2 markerSize = new Vector2(parking.SpaceSize.X * 0.55f, parking.SpaceSize.Y * 0.70f);
			DrawStyleBox(_reservedSpaceStyle, new Rect2(center - markerSize * 0.5f, markerSize));
		}
	}

	// joins the building to the lot without covering parking spaces
	private void DrawWalkway(Rect2 roof, Rect2 lot)
	{
		float width = _roads.CellSize * 0.20f;
		Rect2 walkway = new Rect2(
			new Vector2(roof.GetCenter().X - width * 0.5f, roof.End.Y),
			new Vector2(width, lot.Position.Y - roof.End.Y));
		DrawRect(new Rect2(walkway.Position + Vector2.One * 3, walkway.Size), new Color(0.2f, 0.3f, 0.3f, 0.18f));
		DrawRect(walkway, new Color(0.87f, 0.9f, 0.92f));
	}

	// draws the driveway to the building's connected road endpoint
	// TODO: improve the look of this
	private void DrawRoadAccess(Rect2 lot, Structure structure)
	{
		if (structure?.AccessCell is not Vector2I access)
		{
			return;
		}

		Vector2 roadCenter = _roads.CellToLocalCenter(access);
		Vector2 entrance = new Vector2(roadCenter.X, lot.End.Y - 2);
		DrawLine(entrance, roadCenter, new Color(0.97f, 0.99f, 1), _roads.RoadWidth + 4, true);
		DrawLine(entrance, roadCenter, _roads.RoadColor, _roads.RoadWidth, true);
	}
	
	private int _batchDepth;
	private bool _changedWhileBatching;
	
	public void BeginBatch() => _batchDepth++;
	
	public void EndBatch() 
	{
		if (_batchDepth == 0) return;
		if (--_batchDepth > 0) return;
		
		if (_changedWhileBatching)
		{
			_changedWhileBatching = false;
			StructuresChanged?.Invoke();
		}
		QueueRedraw();
	}
	public void Clear()
	{
		foreach (Structure structure in _structures)
		{
			structure.RemoveParkingLanes(_lanes);
			structure.ParkingChanged -= Changed;
			structure.AccessRoad = null;
			structure.AccessCell = null;
		}
		
		_structures.Clear();
		if (Selected != null) Select(null);
		Changed();
	}

}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;



public partial class RoadGraph : Node2D
{
	[ExportGroup("References")]
	[Export] public NodePath CameraPath { get; set; } = new NodePath("../../Camera2D");
	[Export] public NodePath LaneGraphPath { get; set; } = new NodePath("../LaneGraph");

	[ExportGroup("Grid")]
	// width / height of each grid cell
	[Export] public float CellSize { get; set; } = 128.0f;

	[ExportGroup("Road Appearance")]
	[Export] public float RoadWidth { get; set; } = 32.0f;
	[Export] public Color RoadColor { get; set; } = new Color(0.36f, 0.39f, 0.45f);
	[Export] public Color RoadPreviewColor { get; set; } = new Color(0.25f, 0.62f, 1.0f, 0.75f);

	[ExportGroup("Delete Selection")]
	[Export] public float DeleteInset { get; set; } = 6.0f;
	[Export] public float DeleteOutlineWidth { get; set; } = 2.0f;
	[Export] public Color DeleteFillColor { get; set; } = new Color(0.95f, 0.22f, 0.27f, 0.16f);
	[Export] public Color DeleteOutlineColor { get; set; } = new Color(1.0f, 0.34f, 0.38f, 0.9f);

	[ExportGroup("Selection")]
	[Export] public Color SelectFillColor { get; set; } = new Color(0.25f, 0.62f, 1.0f, 0.16f);
	[Export] public Color SelectOutlineColor { get; set; } = new Color(0.35f, 0.70f, 1.0f, 0.9f);

	private readonly List<Vector2I> _draggedCells = new();

	private CameraController _cameraController;
	private LaneGraph _laneGraph;
	private RoadNetwork _roadNetwork;
	private UIRoot _ui;
	private StructureGraph _structures;

	//speed limit for new roads (default in inspector)
	private float _currentSpeedLimit = 35.0f;
	private bool _isDraggingRoad;
	private bool _isDraggingRect;
	private Vector2I _rectStartCell;
	private Vector2I _rectEndCell;

	private Vector2I _hoverCell;
	private bool _showHover;
	private bool _isErasing;
	private Vector2I _lastErasedCell;

	private bool _rectDeletes;
	private bool _hasSelection;
	private Vector2I _selectionMin, _selectionMax;
	private readonly HashSet<Vector2I> _selectedRoadCells = new();
	private readonly List<Structure> _selectedStructures = new();

	// junction tool: which template to place, its rotation, and its preview
	private JunctionTemplate _junctionTemplate = JunctionTemplate.Get(JunctionTemplateType.FourWay);
	private int _templateRotation;
	private bool _showTemplatePreview;
	private Vector2I _templateHoverCell;

	// junction tool: the existing intersection picked for signal editing, if any
	private Vector2I? _selectedJunction;

	public IReadOnlyList<RoadSegment> Segments => _roadNetwork?.Segments ?? Array.Empty<RoadSegment>();
	public bool IsRectDragging => _isDraggingRect;

	public bool HasSelection => _hasSelection;
	public event Action SelectionChanged;

	public Vector2I? SelectedJunction => _selectedJunction;
	public event Action<Vector2I?> JunctionSelectionChanged;
	public IReadOnlySet<Vector2I> SelectedRoadCells => _selectedRoadCells;
	public IReadOnlyList<Structure> SelectedStructures => _selectedStructures;


	public bool TryGetSelection(out Vector2I min, out Vector2I max)
	{
		min = _selectionMin;
		max = _selectionMax;
		return _hasSelection;
	}

	public void ClearSelection()
	{
		ClearJunctionSelection();
		if (!_hasSelection) return;
		_hasSelection = false;
		_selectedRoadCells.Clear();
		_selectedStructures.Clear();
		SelectionChanged?.Invoke();
		QueueRedraw();
	}

	// selects an existing intersection so its signal can be edited
	// always notifies, so clicking the same intersection again re-shows it in the inspector
	private void SelectJunction(Vector2I cell)
	{
		_selectedJunction = cell;
		JunctionSelectionChanged?.Invoke(_selectedJunction);
		QueueRedraw();
	}

	public void ClearJunctionSelection()
	{
		if (_selectedJunction == null) return;

		_selectedJunction = null;
		JunctionSelectionChanged?.Invoke(null);
		QueueRedraw();
	}

	// signal access for the ui; the road network does the validation
	public SignalSettings GetSignal(Vector2I cell) => _roadNetwork?.GetSignal(cell) ?? default;

	public bool SetSignal(Vector2I cell, SignalSettings signal)
	{
		if (_roadNetwork == null || !_roadNetwork.SetSignal(cell, signal)) return false;

		QueueRedraw();
		return true;
	}

	//update speed limit for new roads
	public void SetSpeedLimit(int speedLimit)
	{
		_currentSpeedLimit = speedLimit;
	}

	// picks which premade junction the junction tool places
	public void SetJunctionTemplate(JunctionTemplateType type)
	{
		_junctionTemplate = JunctionTemplate.Get(type);
		_templateRotation = 0;
		QueueRedraw();
	}

	public event Action<RoadSegment> RoadPlaced;
	public event Action RoadsChanged;
	public event Action<Vector2I, Vector2I> DeleteCellsRequested;

	// converts a cell coordinate to its top-left position in the grid
	public Godot.Vector2 CellToLocal(Vector2I cell)
	{
		return GridMath.CellToLocal(cell, CellSize);
	}

	// converts a cell coordinate to its center position in the grid
	// will be used for placing objects at a cell's center
	public Godot.Vector2 CellToLocalCenter(Vector2I cell)
	{
		return GridMath.CellToLocalCenter(cell, CellSize);
	}

	// finds the grid cell containing a position in local space
	public Vector2I LocalToCell(Godot.Vector2 localPosition)
	{
		return GridMath.LocalToCell(localPosition, CellSize);
	}

	// converts a cell coordinate to a world position
	// will be used for placing objects at a cell's position in the world
	public Godot.Vector2 CellToGlobal(Vector2I cell)
	{
		return ToGlobal(CellToLocal(cell));
	}

	// converts a cell coordinate to the world position of its center
	// will be used for placing objects at a cell's center in the world
	public Godot.Vector2 CellToGlobalCenter(Vector2I cell)
	{
		return ToGlobal(CellToLocalCenter(cell));
	}

	// finds the grid cell containing a world position
	// this is useful for mouse input
	public Vector2I GlobalToCell(Godot.Vector2 globalPosition)
	{
		return LocalToCell(ToLocal(globalPosition));
	}

	// returns the local rectangle of one grid cell
	public Rect2 GetCellLocalRect(Vector2I cell)
	{
		return GridMath.GetCellRect(cell, CellSize);
	}

	// returns the local rectangle occupied by a multi-cell structure
	// will be used for buildings, parking lots, etc.
	public Rect2 GetMultiCellLocalRect(Vector2I anchorCell, Vector2I sizeInCells)
	{
		return GridMath.GetMultiCellRect(anchorCell, sizeInCells, CellSize);
	}

	public override void _Ready()
	{
		_cameraController = GetNodeOrNull<CameraController>(CameraPath);

		// in theory should never happen but just in case
		if (_cameraController == null)
		{
			GD.PushWarning("CameraController not found, edge panning disabled.");
		}

		_laneGraph = GetNodeOrNull<LaneGraph>(LaneGraphPath);

		if (_laneGraph == null)
		{
			GD.PushError("Failed to get LaneGraph node.");

			SetProcess(false);
			return;
		}

		_roadNetwork = new RoadNetwork(_laneGraph);
		_ui = GetNode<UIRoot>("../../UI");
		_structures = GetNode<StructureGraph>("../StructureGraph");
		_structures.StructuresChanged += ClearSelection;
		_ui.ToolChanged += OnToolChanged;

		VerifyCellConversions();
		QueueRedraw();
	}

	public override void _ExitTree()
	{
		if (_ui != null) _ui.ToolChanged -= OnToolChanged;
		if (_structures != null) _structures.StructuresChanged -= ClearSelection;
	}

	private void OnToolChanged(int tool)
	{
		CancelCurrentDrag();
		ClearSelection();
	}


	// updates whichever drag is currently active
	public override void _Process(double delta)
	{
		UpdateHover();
		UpdateTemplatePreview();
		if (_isErasing)
		{
			if (!Input.IsMouseButtonPressed(MouseButton.Left))
			{
				FinishErase();
				return;
			}

			ContinueErase(GlobalToCell(GetGlobalMousePosition()));
			return;
		}

		if (!_isDraggingRoad && !_isDraggingRect)
		{
			return;
		}

		// handles mouse release
		if (!Input.IsMouseButtonPressed(MouseButton.Left))
		{
			if (_isDraggingRect)
			{
				FinishRectDrag();
			}
			else
			{
				FinishRoadDrag();
			}

			return;
		}

		Vector2I hoveredCell = GlobalToCell(GetGlobalMousePosition());

		if (_isDraggingRect)
		{
			UpdateRectDrag(hoveredCell);
		}
		else
		{
			ContinueRoadDrag(hoveredCell);
		}
	}

	// starts a road or deletion square using the left mouse button
	public override void _UnhandledInput(InputEvent @event)
	{
		if (_roadNetwork == null || _ui.IsSimulating) return;

		// no tool (select mode): clicking an intersection picks it for signal editing,
		// the same way clicking a building selects it
		if (_ui.CurrentTool == ToolType.None && !_isDraggingRect &&
			@event is InputEventMouseButton pick && pick.Pressed &&
			pick.ButtonIndex == MouseButton.Left && !pick.ShiftPressed)
		{
			Vector2I cell = GlobalToCell(GetGlobalMousePosition());
			if (_roadNetwork.IsIntersection(cell))
			{
				ClearSelection();
				SelectJunction(cell);
				GetViewport().SetInputAsHandled();
			}
			else
			{
				ClearJunctionSelection();
			}
			return;
		}

		// junction tool: R rotates the template (same key as buildings)
		if (_ui.CurrentTool == ToolType.Junction && @event is InputEventKey key &&
			key.Pressed && !key.Echo && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed &&
			(key.PhysicalKeycode == Key.R || key.Keycode == Key.R) &&
			GetViewport().GuiGetFocusOwner() is not LineEdit and not TextEdit)
		{
			_templateRotation = (_templateRotation + 1) % 4;
			QueueRedraw();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_ui.CurrentTool == ToolType.Junction && !_isDraggingRect &&
			@event is InputEventMouseButton click &&
			click.ButtonIndex == MouseButton.Left && !click.ShiftPressed)
		{
			if (click.Pressed)
			{
				Vector2I cell = GlobalToCell(GetGlobalMousePosition());

				// clicking an existing intersection selects it instead of placing on top of it
				if (_roadNetwork.IsIntersection(cell))
				{
					ClearSelection();
					SelectJunction(cell);
				}
				else
				{
					ClearJunctionSelection();
					var paths = _junctionTemplate.GetPaths(cell, _templateRotation);
					if (!TryPlaceTemplate(paths, out string error))
					{
						GD.Print($"Can't place intersection: {error}");
					}
				}
			}
			GetViewport().SetInputAsHandled();
			return;
		}
		if (!_isDraggingRoad && !_isDraggingRect &&
			_ui.CurrentTool != ToolType.Road && _ui.CurrentTool != ToolType.Erase &&
			!(@event is InputEventMouseButton deleteMouse && deleteMouse.ShiftPressed &&
				deleteMouse.ButtonIndex == MouseButton.Left && deleteMouse.Pressed))
			return;
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (mouseButton.Pressed)
			{
				if (!_isDraggingRoad && !_isDraggingRect)
				{
					if (mouseButton.ShiftPressed)
					{
						BeginRectDrag();
					}
					else if (_ui.CurrentTool == ToolType.Erase)
					{
						BeginErase();
					}
					else
					{
						BeginRoadDrag();
					}
				}
			}
			else if (_isErasing)
			{
				FinishErase();
			}
			else if (_isDraggingRect)
			{
				FinishRectDrag();
			}
			else
			{
				FinishRoadDrag();
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		if ((_isDraggingRoad || _isDraggingRect) && @event.IsActionPressed("ui_cancel"))
		{
			CancelCurrentDrag();
			GetViewport().SetInputAsHandled();
		}
	}

	// starts a road at the cell under the cursor
	private void BeginRoadDrag()
	{
		_draggedCells.Clear();

		Vector2I startingCell = GlobalToCell(GetGlobalMousePosition());
		if (_structures.IsCellOccupied(startingCell)) return;

		ClearSelection();
		_draggedCells.Add(startingCell);
		_isDraggingRoad = true;

		SetEdgePan(true);
		QueueRedraw();
	}

	// extends the road as the cursor or camera moves
	private void ContinueRoadDrag(Vector2I targetCell)
	{
		if (_draggedCells.Count == 0)
		{
			return;
		}

		Vector2I currentCell = _draggedCells[^1];

		if (currentCell == targetCell)
		{
			return;
		}

		// account for fast mouse movement
		while (currentCell != targetCell)
		{
			Vector2I difference = targetCell - currentCell;

			if (Math.Abs(difference.X) >= Math.Abs(difference.Y) && difference.X != 0)
			{
				currentCell += new Vector2I(Math.Sign(difference.X), 0);
			}
			else
			{
				currentCell += new Vector2I(0, Math.Sign(difference.Y));
			}

			if (_structures.IsCellOccupied(currentCell)) break;
			AddDraggedCell(currentCell);
		}

		QueueRedraw();
	}

	// adds a cell to the dragged road, avoids duplicates and overlaps
	private void AddDraggedCell(Vector2I cell)
	{
		int cellCount = _draggedCells.Count;

		if (cellCount >= 2 && _draggedCells[cellCount - 2] == cell)
		{
			_draggedCells.RemoveAt(cellCount - 1);
			return;
		}

		if (_draggedCells[cellCount - 1] != cell)
		{
			_draggedCells.Add(cell);
		}
	}

	// placement validation
	public bool CanAddRoad(IReadOnlyList<Vector2I> path, out string error)
	{
		error = "";

		if (_roadNetwork == null || path == null || path.Count < 2)
		{
			error = "A road needs at least two cells.";
			return false;
		}

		for (int i = 0; i < path.Count; ++i)
		{
			if (_structures.IsCellOccupied(path[i]))
			{
				error = "Road overlaps a building";
				return false;
			}

			if (i > 0)
			{
				Vector2I difference = path[i] - path[i - 1];
				if (Math.Abs(difference.X) + Math.Abs(difference.Y) != 1)
				{
					error = "Road cells must be adjacent";
					return false;
				}
			}
		}

		return true;
	}

	//adds roads with speed limit
	public bool TryAddRoad(IReadOnlyList<Vector2I> path, out RoadSegment segment)
	{
		return TryAddRoad(path, _currentSpeedLimit, out segment);
	}

	public bool TryAddRoad(IReadOnlyList<Vector2I> path, float speedLimit, out RoadSegment segment)
	{
		segment = null;

		if (_roadNetwork == null || _ui.IsSimulating || path == null || path.Count < 2 ||
			path.Any(cell => _structures.IsCellOccupied(cell)))
			return false;

		for (int i = 1; i < path.Count; i++)
		{
			Vector2I difference = path[i] - path[i - 1];

			if (Math.Abs(difference.X) + Math.Abs(difference.Y) != 1)
				return false;
		}

		List<RoadSegment> created = _roadNetwork.AddPath(path, speedLimit);

		foreach (RoadSegment s in created)
			RoadPlaced?.Invoke(s);

		segment = created.FirstOrDefault();

		NotifyRoadsChanged();
		QueueRedraw();

    	return true;
	}

	// places a premade junction from its road paths, all or nothing
	public bool TryPlaceTemplate(List<List<Vector2I>> paths, out string error)
	{
		error = "";
		if (_roadNetwork == null || _ui.IsSimulating) return false;

		foreach (List<Vector2I> path in paths)
		{
			if(!CanAddRoad(path, out error)) return false;
		}

		BeginBatch();
		foreach(List<Vector2I>path in paths)
		{
			TryAddRoad(path, out _);
		}
		EndBatch();

		return true;
	}
	// places the road that was shown in the preview
	private void FinishRoadDrag()
	{
		if (!_isDraggingRoad)
		{
			return;
		}

		ContinueRoadDrag(GlobalToCell(GetGlobalMousePosition()));

		TryAddRoad(_draggedCells, out _);

		_draggedCells.Clear();
		_isDraggingRoad = false;

		SetEdgePan(false);
		QueueRedraw();
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
			RoadsChanged?.Invoke();
		}
		QueueRedraw();
	}

	private void NotifyRoadsChanged()
	{
		ClearSelection();
		if (_batchDepth > 0) _changedWhileBatching = true;
		else RoadsChanged?.Invoke();
	}
	// starts a deletion box at the cell under the cursor
	private void BeginRectDrag()
	{
		_rectDeletes = _ui.CurrentTool == ToolType.Erase;

		_rectStartCell = GlobalToCell(GetGlobalMousePosition());
		_rectEndCell = _rectStartCell;
		_isDraggingRect = true;

		SetEdgePan(true);
		QueueRedraw();
	}

	// updates the end of the deletion box
	private void UpdateRectDrag(Vector2I cell)
	{
		if (_rectEndCell == cell)
		{
			return;
		}

		_rectEndCell = cell;
		QueueRedraw();
	}

	// removes roads within the box when the mouse is released
	// buildings touched by the box are removed as whole buildings
	private void FinishRectDrag()
	{
		if (!_isDraggingRect)
		{
			return;
		}

		UpdateRectDrag(GlobalToCell(GetGlobalMousePosition()));

		_isDraggingRect = false;
		SetEdgePan(false);

		GetRectBounds(out Vector2I minCell, out Vector2I maxCell);
		if (_rectDeletes)
		{
			ClearSelection();
			DeleteRoadCellsInSelection();
			DeleteCellsRequested?.Invoke(minCell, maxCell);
		}
		else
		{
			CommitSelection(minCell, maxCell);
		}

		QueueRedraw();
	}

	// escape discards the active drag
	private void CancelCurrentDrag()
	{
		FinishErase();
		_draggedCells.Clear();
		_isDraggingRoad = false;
		_isDraggingRect = false;

		SetEdgePan(false);
		QueueRedraw();
	}

	// converts selected rectangle into actual data
	private void CommitSelection(Vector2I min, Vector2I max)
	{
		_selectedRoadCells.Clear();
		_selectedStructures.Clear();

		// road cells inside the rectangle (clipped, same rule as rectangle delete)
		foreach (RoadSegment segment in _roadNetwork.Segments)
			foreach (Vector2I cell in segment.Path)
				if (cell.X >= min.X && cell.X <= max.X && cell.Y >= min.Y && cell.Y <= max.Y)
					_selectedRoadCells.Add(cell);

		// any structure touched by the rectangle is taken whole (same rule as erase)
		var seen = new HashSet<Structure>();
		for (int x = min.X; x <= max.X; x++)
			for (int y = min.Y; y <= max.Y; y++)
			{
				Structure s = _structures.GetAt(new Vector2I(x, y));
				if (s != null && seen.Add(s)) _selectedStructures.Add(s);
			}

		// nothing populated: no selection at all
		if (_selectedRoadCells.Count == 0 && _selectedStructures.Count == 0)
		{
			ClearSelection();
			return;
		}

		// snap the bounds to the contents (whole structures can extend past the drag)
		Vector2I lo = new(int.MaxValue, int.MaxValue), hi = new(int.MinValue, int.MinValue);
		foreach (Vector2I c in _selectedRoadCells)
		{
			lo = new Vector2I(Math.Min(lo.X, c.X), Math.Min(lo.Y, c.Y));
			hi = new Vector2I(Math.Max(hi.X, c.X), Math.Max(hi.Y, c.Y));
		}
		foreach (Structure s in _selectedStructures)
		{
			Vector2I a = s.Bounds.Position, b = s.Bounds.Position + s.Bounds.Size - Vector2I.One;
			lo = new Vector2I(Math.Min(lo.X, a.X), Math.Min(lo.Y, a.Y));
			hi = new Vector2I(Math.Max(hi.X, b.X), Math.Max(hi.Y, b.Y));
		}

		_selectionMin = lo;
		_selectionMax = hi;
		_hasSelection = true;
		SelectionChanged?.Invoke();
	}

	private void SetEdgePan(bool enabled)
	{
		if (_cameraController != null)
		{
			_cameraController.EdgePanEnabled = enabled;
		}
	}

	public override void _Draw()
	{
		if (CellSize <= 0.0f)
		{
			return;
		}

		if (RoadWidth > 0.0f)
		{
			float finalRoadWidth = Mathf.Clamp(RoadWidth, 1.0f, CellSize);

			DrawCommittedRoads(finalRoadWidth);
			DrawRoadPreview(finalRoadWidth);
			DrawTemplatePreview(finalRoadWidth);
		}

		DrawRectSelection();
		DrawJunctionSelection();
		DrawHover();
	}

	// outlines the intersection picked for signal editing
	private void DrawJunctionSelection()
	{
		if (_selectedJunction is not Vector2I cell) return;

		DrawDeleteRect(GetCellLocalRect(cell), SelectFillColor, SelectOutlineColor);
	}

	// tracks where the junction template would be placed
	private void UpdateTemplatePreview()
	{
		Vector2I cell = GlobalToCell(GetGlobalMousePosition());
		// over an existing intersection a click selects it, so no template ghost
		bool show = _ui.CurrentTool == ToolType.Junction && !_ui.IsSimulating &&
			!_isDraggingRect && GetViewport().GuiGetHoveredControl() == null &&
			_roadNetwork != null && !_roadNetwork.IsIntersection(cell);

		if (show == _showTemplatePreview && cell == _templateHoverCell)
		{
			return;
		}

		_showTemplatePreview = show;
		_templateHoverCell = cell;
		QueueRedraw();
	}

	// ghost of the template under the cursor, red where it can't be placed
	private void DrawTemplatePreview(float roadWidth)
	{
		if (!_showTemplatePreview)
		{
			return;
		}

		List<List<Vector2I>> paths = _junctionTemplate.GetPaths(_templateHoverCell, _templateRotation);
		bool valid = paths.All(path => CanAddRoad(path, out _));
		Color color = valid ? RoadPreviewColor : new Color(DeleteOutlineColor, 0.6f);

		foreach (List<Vector2I> path in paths)
		{
			DrawRoadPath(path, color, roadWidth);
		}
	}

	private void DrawCommittedRoads(float roadWidth)
	{
		if (_roadNetwork == null)
		{
			return;
		}

		foreach (RoadSegment segment in _roadNetwork.Segments)
		{
			DrawRoadPath(segment.Path, RoadColor, roadWidth);
		}
	}

	private void DrawRoadPreview(float roadWidth)
	{
		DrawRoadPath(_draggedCells, RoadPreviewColor, roadWidth);
	}

	private void DrawRoadPath(IReadOnlyList<Vector2I> cells, Color color, float roadWidth)
	{
		for (int i = 1; i < cells.Count; i++)
		{
			DrawLine(CellToLocalCenter(cells[i - 1]), CellToLocalCenter(cells[i]), color, roadWidth, true);
		}

		foreach (Vector2I cell in cells)
		{
			DrawRoadJoint(CellToLocalCenter(cell), color, roadWidth);
		}

		DrawRoadMarkings(cells, roadWidth, color.A);
	}

	// draws a dashed center line that follows the road's width
	private void DrawRoadMarkings(IReadOnlyList<Vector2I> cells, float roadWidth, float opacity)
	{
		Color stripeColor = new Color(0.96f, 0.98f, 1, opacity);
		float stripeWidth = Mathf.Max(1, roadWidth * 0.04f);
		float inset = roadWidth * 0.5f;

		for (int i = 1; i < cells.Count; i++)
		{
			Vector2I step = cells[i] - cells[i - 1];
			Godot.Vector2 direction = ((Godot.Vector2)step).Normalized();
			Godot.Vector2 from = CellToLocalCenter(cells[i - 1]);
			Godot.Vector2 to = CellToLocalCenter(cells[i]);

			// leave bends and road ends clear
			if (i == 1 || cells[i - 1] - cells[i - 2] != step)
			{
				from += direction * inset;
			}
			if (i == cells.Count - 1 || cells[i + 1] - cells[i] != step)
			{
				to -= direction * inset;
			}

			DrawDashedLine(from, to, stripeColor, stripeWidth, roadWidth * 0.3f, true, true);
		}
	}

	private void DrawRoadJoint(Godot.Vector2 center, Color color, float roadWidth)
	{
		DrawCircle(center, roadWidth * 0.5f, color, true, -1.0f, true);
	}

	// draws the selection box
	private void DrawRectSelection()
	{


		if (_isDraggingRect)
		{
			GetRectBounds(out Vector2I min, out Vector2I max);
			Color fill = _rectDeletes ? DeleteFillColor : SelectFillColor;
			Color outline = _rectDeletes ? DeleteOutlineColor : SelectOutlineColor;

			DrawDeleteRect(new Rect2(CellToLocal(min),
				new Godot.Vector2((max.X - min.X + 1) * CellSize, (max.Y - min.Y + 1) * CellSize)), fill, outline);
		}
		else if (_hasSelection)
		{
			foreach (Vector2I cell in _selectedRoadCells)
				DrawDeleteRect(GetCellLocalRect(cell), SelectFillColor, SelectOutlineColor);

			foreach (Structure s in _selectedStructures)
				DrawDeleteRect(GetMultiCellLocalRect(s.Bounds.Position, s.Bounds.Size),
					SelectFillColor, SelectOutlineColor);
		}
	}

	// finds the cells covered by the deletion box
	private void GetRectBounds(out Vector2I minCell, out Vector2I maxCell)
	{
		minCell = new Vector2I(Math.Min(_rectStartCell.X, _rectEndCell.X), Math.Min(_rectStartCell.Y, _rectEndCell.Y));
		maxCell = new Vector2I(Math.Max(_rectStartCell.X, _rectEndCell.X), Math.Max(_rectStartCell.Y, _rectEndCell.Y));
	}

	// takes any cell test
	public void DeleteRoadCellsWhere(Func<Vector2I, bool> inside)
	{
		if (_roadNetwork == null) return;

		var segmentsSnapshot = _roadNetwork.Segments.ToList();
		bool changed = false;

		foreach (RoadSegment segment in segmentsSnapshot)
 {
			var remainingRuns = new List<List<Vector2I>>();
			var currentRun = new List<Vector2I>();
			bool segmentWasAffected = false;

			foreach (Vector2I cell in segment.Path)
			{
				bool insideSelection = inside(cell);

				if (insideSelection)
				{
					segmentWasAffected = true;
					AddRemainingRun(remainingRuns, currentRun);
					currentRun = new List<Vector2I>();
				}
				else
				{
					currentRun.Add(cell);
				}
			}

			if (!segmentWasAffected)
			{
				continue;
			}

			AddRemainingRun(remainingRuns, currentRun);
			//remember speed limit
			float originalSpeedLimit = segment.SpeedLimit;
			_roadNetwork.RemoveSegment(segment);
			foreach (List<Vector2I> run in remainingRuns)
			{
				_roadNetwork.AddSegment(run, originalSpeedLimit);
			}

			changed = true;
		}

		if (changed) NotifyRoadsChanged();
	}

	// removes segments within the box
	private void DeleteRoadCellsInSelection()
	{
		GetRectBounds(out Vector2I min, out Vector2I max);
		DeleteRoadCellsWhere(c => c.X >= min.X && c.X <= max.X && c.Y >= min.Y && c.Y <= max.Y);



	}

	public void ClearRoads()
	{
		CancelCurrentDrag();
		ClearSelection();
		_roadNetwork?.Clear();
		NotifyRoadsChanged();
		QueueRedraw();
	}

	// a single cell without a connection is not a road section, therefore don't count it
	private void AddRemainingRun(List<List<Vector2I>> destination, List<Vector2I> cells)
	{
		if (cells.Count < 2)
		{
			return;
		}

		destination.Add(cells);
	}

	// verifies that converting to a position and back preserves the original coordinates
	private void VerifyCellConversions()
	{
		Vector2I originalCell = new Vector2I(-2, 3);
		Godot.Vector2 localPosition = CellToLocal(originalCell);
		Vector2I convertedBack = LocalToCell(localPosition);

		if (convertedBack != originalCell)
		{
			GD.PushError($"Cell conversion failed: " + $"{originalCell} became {convertedBack} after conversion.");
			return;
		}

		GD.Print("Cell conversion passed successfully.");
	}

	// tracks the cell under the cursor so it can be highlighted
	private void UpdateHover()
	{
		bool show = !_ui.IsSimulating && !_isDraggingRect && GetViewport().GuiGetHoveredControl() == null;
		Vector2I cell = GlobalToCell(GetGlobalMousePosition());

		if (show == _showHover && cell == _hoverCell)
		{
			return;
		}

		_showHover = show;
		_hoverCell = cell;
		QueueRedraw();
	}

	private void DrawDeleteRect(Rect2 area, Color? fill = null, Color? outline = null)
	{
		float canvasScale = GetGlobalTransformWithCanvas().X.Length();
		float inset = canvasScale > 0.0f ? DeleteInset / canvasScale : DeleteInset;
		float outlineWidth = canvasScale > 0.0f ? DeleteOutlineWidth / canvasScale : DeleteOutlineWidth;

		inset = Mathf.Min(inset, CellSize * 0.25f);
		area = area.Grow(-inset);

		DrawRect(area, fill ?? DeleteFillColor);
		DrawRect(area, outline ?? DeleteOutlineColor, false, outlineWidth, true);
	}

	// highlights what a click with the erase tool will remove
	private void DrawHover()
	{
		if (!_showHover)
		{
			return;
		}

		bool erasing = _ui.CurrentTool == ToolType.Erase;

		// building deletion
		Structure building = erasing ? _structures.GetAt(_hoverCell) : null;
		Rect2 area = building != null
			? GetMultiCellLocalRect(building.Bounds.Position, building.Bounds.Size)
			: GetCellLocalRect(_hoverCell);


		DrawDeleteRect(area,
			erasing ? DeleteFillColor : SelectFillColor,
			erasing ? DeleteOutlineColor : SelectOutlineColor
		);
	}

	// starts erasing and removes cell under the cursor
	private void BeginErase()
	{
		ClearSelection();
		_isErasing = true;
		BeginBatch();
		SetEdgePan(true);

		_lastErasedCell = GlobalToCell(GetGlobalMousePosition());
		EraseCell(_lastErasedCell);
	}

	// erases every cell between the last one and the cursor
	private void ContinueErase(Vector2I targetCell)
	{
		while (_lastErasedCell != targetCell)
		{
			Vector2I difference = targetCell - _lastErasedCell;

			if (Math.Abs(difference.X) >= Math.Abs(difference.Y) && difference.X != 0)
			{
				_lastErasedCell += new Vector2I(Math.Sign(difference.X), 0);
			}
			else
			{
				_lastErasedCell += new Vector2I(0, Math.Sign(difference.Y));
			}

			EraseCell(_lastErasedCell);
		}
	}

	// remove a cell and any building on it using the delete path
	private void EraseCell(Vector2I cell)
	{
		_rectStartCell = cell;
		_rectEndCell = cell;
		DeleteRoadCellsInSelection();
		DeleteCellsRequested?.Invoke(cell, cell);
		QueueRedraw();
	}

	private void FinishErase()
	{
		if (!_isErasing)
		{
			return;
		}

		_isErasing = false;
		SetEdgePan(false);
		EndBatch();
	}


}

using Godot;
using System;
using System.Collections.Generic;

public partial class RoadGraph : Node2D
{
    [ExportGroup("References")]
    [Export] public NodePath CameraPath { get; set; } = new NodePath("../../Camera2D");

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

    // one placed road and its ordered cells
    public sealed record PlacedRoad(int Id, IReadOnlyList<Vector2I> Cells);

    private readonly List<PlacedRoad> _roads = new();
    private readonly List<Vector2I> _draggedCells = new();

    private CameraController _cameraController;
    private int _nextRoadId = 1;
    private bool _isDraggingRoad;
    private bool _isSelectingDelete;
    private Vector2I _deleteStartCell;
    private Vector2I _deleteEndCell;

    public IReadOnlyList<PlacedRoad> Roads => _roads.AsReadOnly();
    public bool IsDeleteSelecting => _isSelectingDelete;

    public event Action<PlacedRoad> RoadPlaced;
    public event Action RoadsChanged;

    // converts a cell coordinate to its top-left position in the grid
    public Vector2 CellToLocal(Vector2I cell)
    {
        return GridMath.CellToLocal(cell, CellSize);
    }

    // converts a cell coordinate to its center position in the grid
    // will be used for placing objects at a cell's center
    public Vector2 CellToLocalCenter(Vector2I cell)
    {
        return GridMath.CellToLocalCenter(cell, CellSize);
    }

    // finds the grid cell containing a position in local space
    public Vector2I LocalToCell(Vector2 localPosition)
    {
        return GridMath.LocalToCell(localPosition, CellSize);
    }

    // converts a cell coordinate to a world position
    // will be used for placing objects at a cell's position in the world
    public Vector2 CellToGlobal(Vector2I cell)
    {
        return ToGlobal(CellToLocal(cell));
    }

    // converts a cell coordinate to the world position of its center
    // will be used for placing objects at a cell's center in the world
    public Vector2 CellToGlobalCenter(Vector2I cell)
    {
        return ToGlobal(CellToLocalCenter(cell));
    }

    // finds the grid cell containing a world position
    // this is useful for mouse input
    public Vector2I GlobalToCell(Vector2 globalPosition)
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
            GD.PushWarning("CameraController not found; edge panning is disabled.");
        }

        VerifyCellConversions();
        QueueRedraw();
    }

    // updates whichever drag is currently active
    public override void _Process(double delta)
    {
        if (!_isDraggingRoad && !_isSelectingDelete)
        {
            return;
        }

        // handles mouse release 
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (_isSelectingDelete)
            {
                FinishDeleteSelection();
            }
            else
            {
                FinishRoadDrag();
            }

            return;
        }

        Vector2I hoveredCell = GlobalToCell(GetGlobalMousePosition());

        if (_isSelectingDelete)
        {
            UpdateDeleteSelection(hoveredCell);
        }
        else
        {
            ContinueRoadDrag(hoveredCell);
        }
    }

    // starts a road or deletion square using the left mouse button
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                if (!_isDraggingRoad && !_isSelectingDelete)
                {
                    if (mouseButton.ShiftPressed)
                    {
                        BeginDeleteSelection();
                    }
                    else
                    {
                        BeginRoadDrag();
                    }
                }
            }
            else if (_isSelectingDelete)
            {
                FinishDeleteSelection();
            }
            else
            {
                FinishRoadDrag();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if ((_isDraggingRoad || _isSelectingDelete) && @event.IsActionPressed("ui_cancel"))
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

    // places the road that was shown in the preview
    private void FinishRoadDrag()
    {
        if (!_isDraggingRoad)
        {
            return;
        }

        ContinueRoadDrag(GlobalToCell(GetGlobalMousePosition()));

        if (_draggedCells.Count >= 2)
        {
            var cells = Array.AsReadOnly(_draggedCells.ToArray());
            var road = new PlacedRoad(_nextRoadId++, cells);

            _roads.Add(road);
            RoadPlaced?.Invoke(road);
            RoadsChanged?.Invoke();
        }

        _draggedCells.Clear();
        _isDraggingRoad = false;

        SetEdgePan(false);
        QueueRedraw();
    }

    // starts a deletion box at the cell under the cursor
    private void BeginDeleteSelection()
    {
        _deleteStartCell = GlobalToCell(GetGlobalMousePosition());
        _deleteEndCell = _deleteStartCell;
        _isSelectingDelete = true;

        SetEdgePan(true);
        QueueRedraw();
    }

    // updates the end of the deletion box
    private void UpdateDeleteSelection(Vector2I cell)
    {
        if (_deleteEndCell == cell)
        {
            return;
        }

        _deleteEndCell = cell;
        QueueRedraw();
    }

    // removes roads within the box when the mouse is released
    // TODO: update this later to handle buildings and what not as well
    private void FinishDeleteSelection()
    {
        if (!_isSelectingDelete)
        {
            return;
        }

        UpdateDeleteSelection(GlobalToCell(GetGlobalMousePosition()));

        _isSelectingDelete = false;
        SetEdgePan(false);

        DeleteRoadCellsInSelection();
        QueueRedraw();
    }

    // escape discards the active drag 
    private void CancelCurrentDrag()
    {
        _draggedCells.Clear();
        _isDraggingRoad = false;
        _isSelectingDelete = false;

        SetEdgePan(false);
        QueueRedraw();
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
        }

        DrawDeleteSelection();
    }

    private void DrawCommittedRoads(float roadWidth)
    {
        foreach (PlacedRoad road in _roads)
        {
            DrawRoadPath(road.Cells, RoadColor, roadWidth);
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
    }

    private void DrawRoadJoint(Vector2 center, Color color, float roadWidth)
    {
        DrawCircle(center, roadWidth * 0.5f, color, true, -1.0f, true);
    }

    // draws the deletion box 
    private void DrawDeleteSelection()
    {
        if (!_isSelectingDelete)
        {
            return;
        }

        GetDeleteBounds(out Vector2I minCell, out Vector2I maxCell);

        Rect2 selectionRect = new Rect2(CellToLocal(minCell), new Vector2((maxCell.X - minCell.X + 1) * CellSize, (maxCell.Y - minCell.Y + 1) * CellSize));
        float canvasScale = GetGlobalTransformWithCanvas().X.Length();
        float inset = canvasScale > 0.0f ? DeleteInset / canvasScale : DeleteInset;
        float outlineWidth = canvasScale > 0.0f ? DeleteOutlineWidth / canvasScale : DeleteOutlineWidth;

        inset = Mathf.Min(inset, CellSize * 0.25f);
        selectionRect = selectionRect.Grow(-inset);

        DrawRect(selectionRect, DeleteFillColor);
        DrawRect(selectionRect, DeleteOutlineColor, false, outlineWidth, true);
    }

    // finds the cells covered by the deletion box
    private void GetDeleteBounds(out Vector2I minCell, out Vector2I maxCell)
    {
        minCell = new Vector2I(Math.Min(_deleteStartCell.X, _deleteEndCell.X), Math.Min(_deleteStartCell.Y, _deleteEndCell.Y));
        maxCell = new Vector2I(Math.Max(_deleteStartCell.X, _deleteEndCell.X), Math.Max(_deleteStartCell.Y, _deleteEndCell.Y));
    }

    // removes selected cells and keeps any remaining road sections
    private void DeleteRoadCellsInSelection()
    {
        GetDeleteBounds(out Vector2I minCell, out Vector2I maxCell);

        var remainingRoads = new List<PlacedRoad>();
        bool changed = false;

        foreach (PlacedRoad road in _roads)
        {
            var currentRun = new List<Vector2I>();
            bool roadWasAffected = false;

            foreach (Vector2I cell in road.Cells)
            {
                bool insideSelection = cell.X >= minCell.X && cell.X <= maxCell.X && cell.Y >= minCell.Y && cell.Y <= maxCell.Y;

                if (insideSelection)
                {
                    roadWasAffected = true;
                    AddRemainingRoadRun(remainingRoads, currentRun);
                    currentRun.Clear();
                }
                else
                {
                    currentRun.Add(cell);
                }
            }

            if (roadWasAffected)
            {
                AddRemainingRoadRun(remainingRoads, currentRun);
                changed = true;
            }
            else
            {
                remainingRoads.Add(road);
            }
        }

        if (!changed)
        {
            return;
        }

        _roads.Clear();
        _roads.AddRange(remainingRoads);

        RoadsChanged?.Invoke();
    }

    // a single cell without a connection is not a road section, therefore don't count it
    private void AddRemainingRoadRun(List<PlacedRoad> destination, List<Vector2I> cells)
    {
        if (cells.Count < 2)
        {
            return;
        }

        destination.Add(new PlacedRoad(_nextRoadId++, Array.AsReadOnly(cells.ToArray())));
    }

    // verifies that converting to a position and back preserves the original coordinates
    private void VerifyCellConversions()
    {
        Vector2I originalCell = new Vector2I(-2, 3);
        Vector2 localPosition = CellToLocal(originalCell);
        Vector2I convertedBack = LocalToCell(localPosition);

        if (convertedBack != originalCell)
        {
            GD.PushError($"Cell conversion failed: " + $"{originalCell} became {convertedBack} after conversion.");
            return;
        }

        GD.Print("Cell conversion passed successfully.");
    }
}
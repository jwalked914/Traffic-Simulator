using Godot;

public partial class GridView : Node2D
{
    [ExportGroup("References")]
    [Export] public NodePath RoadGraphPath { get; set; } = new NodePath("../RoadGraph");
    [ExportGroup("Grid")]
    [Export] public float GridLineWidth { get; set; } = 1.0f;
    [Export] public float HoverInset { get; set; } = 6.0f;
    [Export] public float HoverOutlineWidth { get; set; } = 2.0f;
    
    // temporary colors
    // TODO: potentially replace this with a grass theme in the future
    [ExportGroup("Dark Mode Colors")]
    [Export] public Color BackgroundColor { get; set; } = new Color(0.105f, 0.105f, 0.115f);
    [Export] public Color GridLineColor { get; set; } = new Color(0.220f, 0.225f, 0.245f);
    [Export] public Color HoverFillColor { get; set; } = new Color(0.120f, 0.470f, 0.950f, 0.180f);
    [Export] public Color HoverOutlineColor { get; set; } = new Color(0.250f, 0.620f, 1.000f, 0.900f);

    private RoadGraph _roadGraph;
    private Vector2I _hoveredCell;

    public override void _Ready()
    {
        _roadGraph = GetNodeOrNull<RoadGraph>(RoadGraphPath);

        if (_roadGraph == null)
        {
            GD.PushError("Failed to get RoadGraph node.");

            SetProcess(false);
            return;
        }

        // ensure that the roads and buildings display above the cosmetic grid
        ZIndex = -100;
        QueueRedraw();
    }

    // recalculate hover state and redraw the grid every frame to ensure responsiveness
    // TODO: potentially something to optimize later, but for now runs fine
    public override void _Process(double delta)
    {
        UpdateHoveredCell();
        QueueRedraw();
    }

    // draw only the visible portion of the infinite grid, plus a small buffer
    public override void _Draw()
    {
        if (_roadGraph == null)
        {
            return;
        }

        float cellSize = _roadGraph.CellSize;

        if (cellSize <= 0.0f)
        {
            GD.PushError("Cell size must be greater than zero.");
            return;
        }

        Rect2 visibleBounds = GetVisibleLocalBounds();

        // calculate the range of grid cells to draw
        // +1 / -1 is the buffer
        int minX = Mathf.FloorToInt(visibleBounds.Position.X / cellSize) - 1;
        int maxX = Mathf.CeilToInt(visibleBounds.End.X / cellSize) + 1;
        int minY = Mathf.FloorToInt(visibleBounds.Position.Y / cellSize) - 1;
        int maxY = Mathf.CeilToInt(visibleBounds.End.Y / cellSize) + 1;

        DrawRect(visibleBounds.Grow(cellSize * 2.0f), BackgroundColor);
        DrawGridLines(minX, maxX, minY, maxY, visibleBounds, cellSize);
        DrawHoveredCell();
    }

    // draw the boundaries for the visible grid area
    private void DrawGridLines(int minX, int maxX, int minY, int maxY, Rect2 bounds, float cellSize)
    {
        float lineWidth = ScreenPixelsToLocal(GridLineWidth);

        for (int x = minX; x <= maxX; x++)
        {
            float positionX = x * cellSize;

            DrawLine(
                new Vector2(positionX, bounds.Position.Y - cellSize),
                new Vector2(positionX, bounds.End.Y + cellSize),
                GridLineColor,
                lineWidth,
                true
            );
        }

        for (int y = minY; y <= maxY; y++)
        {
            float positionY = y * cellSize;

            DrawLine(
                new Vector2(bounds.Position.X - cellSize, positionY),
                new Vector2(bounds.End.X + cellSize, positionY),
                GridLineColor,
                lineWidth,
                true
            );
        }
    }

    // converts mouse's world position to the cell currently being hovered over
    private void UpdateHoveredCell()
    {
        _hoveredCell = _roadGraph.GlobalToCell(GetGlobalMousePosition());
    }

    // draws a hover indicator on the hovered cell
    private void DrawHoveredCell()
    {
        Rect2 cellRect = _roadGraph.GetCellLocalRect(_hoveredCell);

        float inset = ScreenPixelsToLocal(HoverInset);
        float maxInset = _roadGraph.CellSize * 0.25f;
        inset = Mathf.Min(inset, maxInset);

        float outlineWidth = ScreenPixelsToLocal(HoverOutlineWidth);
        Rect2 hoverRect = cellRect.Grow(-inset);

        DrawRect(hoverRect, HoverFillColor);

        DrawRect(hoverRect, HoverOutlineColor, false, outlineWidth, true);
    }

    // converts pixel measurements to local space
    // this ensures that the drawn elements remain consistent as the camera zooms in and out
    private float ScreenPixelsToLocal(float pixels)
    {
        float canvasScale = GetGlobalTransformWithCanvas().X.Length();

        if (canvasScale <= 0.0f)
        {
            return pixels;
        }

        return pixels / canvasScale;
    }

    // converts all viewport corners into gridview local space
    // returns the smallest rectangle that contains all corners
    private Rect2 GetVisibleLocalBounds()
    {
        Rect2 viewportRect = GetViewport().GetVisibleRect();

        Transform2D viewportToLocal = GetGlobalTransformWithCanvas().AffineInverse();

        Vector2[] corners =
        {
            viewportToLocal * viewportRect.Position,
            viewportToLocal * (viewportRect.Position + new Vector2(viewportRect.Size.X, 0.0f)),
            viewportToLocal * viewportRect.End,
            viewportToLocal * (viewportRect.Position + new Vector2(0.0f, viewportRect.Size.Y))
        };

        float minX = corners[0].X;
        float maxX = corners[0].X;
        float minY = corners[0].Y;
        float maxY = corners[0].Y;

        foreach (Vector2 corner in corners)
        {
            minX = Mathf.Min(minX, corner.X);
            maxX = Mathf.Max(maxX, corner.X);
            minY = Mathf.Min(minY, corner.Y);
            maxY = Mathf.Max(maxY, corner.Y);
        }

        return new Rect2(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
    }
}

using Godot;

public partial class CarVisual : Node2D
{
    [Export]
    public Color CarColor { get; set; } = new Color("#D95C4F");

    private static readonly Color WheelColor =
        new Color("#111417");

    private static readonly Color WindowColor =
        new Color("#AFC3CC");

    public override void _Draw()
    {
        // Draw the wheels first so they sit underneath the car.
        DrawCircle(new Vector2(-8, -6), 3.0f, WheelColor);
        DrawCircle(new Vector2(8, -6), 3.0f, WheelColor);
        DrawCircle(new Vector2(-8, 6), 3.0f, WheelColor);
        DrawCircle(new Vector2(8, 6), 3.0f, WheelColor);

        // Draw the main car body.
        DrawRect(
            new Rect2(-15, -7, 30, 14),
            CarColor
        );

        // Draw the windows.
        DrawCircle(new Vector2(-5, 0), 3.5f, WindowColor);
        DrawCircle(new Vector2(5, 0), 3.5f, WindowColor);
    }

    public void SetCarColor(Color color)
    {
        CarColor = color;
        QueueRedraw();
    }
}

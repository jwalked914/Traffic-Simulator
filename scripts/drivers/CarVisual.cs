
using Godot;

public partial class CarVisual : Node2D
{
    public const float HalfLength = 15.0f;
    private static readonly Shader TunnelShader = GD.Load<Shader>("res://assets/shaders/traffic_endpoint.gdshader");
    private ShaderMaterial _tunnelMaterial;
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
            new Rect2(-HalfLength, -7, HalfLength * 2, 14),
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

    // cut the car at each opening, simulates the car entering and leaving a tunnel (used by producer/consumer)
    public void SetTunnelOpenings(Vector2 source, Vector2 sourceDirection, Vector2 consumer, Vector2 consumerDirection)
    {
        _tunnelMaterial = new ShaderMaterial { Shader = TunnelShader };
        _tunnelMaterial.SetShaderParameter("source_opening", source);
        _tunnelMaterial.SetShaderParameter("source_direction", sourceDirection);
        _tunnelMaterial.SetShaderParameter("consumer_opening", consumer);
        _tunnelMaterial.SetShaderParameter("consumer_direction", consumerDirection);
        Material = _tunnelMaterial;
    }

    public void FinishLeavingTunnel() => _tunnelMaterial.SetShaderParameter("clip_source", false);

    public void BeginEnteringTunnel() => _tunnelMaterial.SetShaderParameter("clip_consumer", true);
}

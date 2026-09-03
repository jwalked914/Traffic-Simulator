using Godot;

public partial class CameraController : Camera2D
{
	[Export] 
	public float MoveSpeed = 600.0f;
	private bool _dragging = false;

	public override void _Process(double delta)
	{
		// get movement input from the player
		Vector2 movement = Input.GetVector(
			"move_left",
			"move_right",
			"move_up",
			"move_down"
		);

		// move the camera based on input type and delta time
		Position += movement * MoveSpeed * (float)delta;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// check if the right mouse button is held
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Right)
			{
				_dragging = mouseButton.Pressed;
			}
		}
		
		// move the camera based on mouse movement
		if (@event is InputEventMouseMotion mouseMotion && _dragging)
		{
			Position -= mouseMotion.Relative / Zoom;
		}
	}
}

using Godot;

public partial class CameraController : Camera2D
{
	[Export] public float MoveSpeed = 1200.0f;
	[Export] public float SmoothSpeed = 16f;
	[Export] public float SprintMultiplier = 2f;
	[Export] public float ZoomFactor = 1.15f;
	[Export] public float MinZoom = 0.25f;
	[Export] public float MaxZoom = 3.0f;
	private bool _dragging = false;
	
	private Vector2 _targetPosition;

	public override void _Ready()
	{
		_targetPosition = Position;
	}

	public override void _Process(double delta)
	{
		// get movement input from the player
		Vector2 movement = Input.GetVector(
			"move_left",
			"move_right",
			"move_up",
			"move_down"
		);
		
		// check fast camera key and apply multiplier
		float speedMultiplier = Input.IsActionPressed("fast_camera") ? SprintMultiplier : 1f;
		
		// move the camera based on input type and delta time
		_targetPosition += movement * MoveSpeed * speedMultiplier * (float)delta;
		
		// Smoothen movement with linear interpolation
		float t = 1f - Mathf.Exp(-SmoothSpeed * (float)delta);
		Position = Position.Lerp(_targetPosition, t);
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
			
			// zoom toward the mouse cursor with scroll wheel
			if (mouseButton.Pressed && (mouseButton.ButtonIndex == MouseButton.WheelUp ||
			    mouseButton.ButtonIndex == MouseButton.WheelDown))
			{
				float oldZoom = Zoom.X;

				float zoomMultiplier = mouseButton.ButtonIndex == MouseButton.WheelUp ? ZoomFactor : 1.0f / ZoomFactor;
			
				float newZoom = Mathf.Clamp(oldZoom * zoomMultiplier, MinZoom, MaxZoom);

				Vector2 mouseOffset = mouseButton.Position - GetViewportRect().Size * 0.5f;

				Vector2 positionChange = mouseOffset / oldZoom - mouseOffset / newZoom;

				Position += positionChange;
				_targetPosition += positionChange;
				Zoom = Vector2.One * newZoom;
			}
		}
		
		// move the camera based on mouse movement
		if (@event is InputEventMouseMotion mouseMotion && _dragging)
		{
			Vector2 draggingMovement = mouseMotion.Relative / Zoom;
			
			Position -= draggingMovement;
			_targetPosition -= draggingMovement;
		}
	}
}

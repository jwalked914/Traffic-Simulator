using Godot;

public partial class CameraController : Camera2D
{
	[Export] public float MoveSpeed = 1200.0f;
	[Export] public float SmoothSpeed = 16f;
	[Export] public float SprintMultiplier = 2f;
	[Export] public float ZoomFactor = 1.15f;
	[Export] public float MinZoom = 0.25f;
	[Export] public float MaxZoom = 3.0f;

	[ExportGroup("Edge Pan")]
	[Export] public float EdgePanMargin = 48.0f;
	[Export] public float EdgePanSpeed = 600.0f;

	public bool EdgePanEnabled { get; set; }

	private bool _dragging = false;
	private Vector2 _targetPosition;

	public override void _Ready()
	{
		_targetPosition = Position;
	}

	public override void _Process(double delta)
	{
		// get movement input from the player
		Vector2 movement = Input.GetVector("move_left", "move_right", "move_up", "move_down");

		// check fast camera key and apply multiplier
		float speedMultiplier = Input.IsActionPressed("fast_camera") ? SprintMultiplier : 1f;

		// move the camera based on input type and delta time
		_targetPosition += movement * MoveSpeed * speedMultiplier * (float)delta;

		// pan when dragging near the edge of the viewport
		if (EdgePanEnabled)
		{
			_targetPosition += GetEdgePanDirection() * EdgePanSpeed * (float)delta / Mathf.Max(Zoom.X, 0.001f);
		}

		// Smoothen movement with linear interpolation
		float t = 1f - Mathf.Exp(-SmoothSpeed * (float)delta);
		Position = Position.Lerp(_targetPosition, t);
	}

	// finds which viewport edge the mouse is near
	private Vector2 GetEdgePanDirection()
	{
		Rect2 viewportRect = GetViewport().GetVisibleRect();
		Vector2 mousePosition = GetViewport().GetMousePosition();

		if (!viewportRect.HasPoint(mousePosition) || EdgePanMargin <= 0.0f)
		{
			return Vector2.Zero;
		}

		float margin = Mathf.Min(EdgePanMargin, Mathf.Min(viewportRect.Size.X, viewportRect.Size.Y) * 0.25f);
		float horizontal = 0.0f;
		float vertical = 0.0f;

		if (mousePosition.X <= viewportRect.Position.X + margin)
		{
			horizontal = -1.0f;
		}
		else if (mousePosition.X >= viewportRect.End.X - margin)
		{
			horizontal = 1.0f;
		}

		if (mousePosition.Y <= viewportRect.Position.Y + margin)
		{
			vertical = -1.0f;
		}
		else if (mousePosition.Y >= viewportRect.End.Y - margin)
		{
			vertical = 1.0f;
		}

		return new Vector2(horizontal, vertical).Normalized();
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
using Godot;

public partial class CheckpointTimeline : Control
{
	[Export] public Color TrackColor { get; set; } = new Color(1, 1, 1, 0.15f);
	[Export] public Color FillColor { get; set; } = new Color("4C9AFF");
	[Export] public Color DotColor { get; set; } = new Color("7CE36B");
	[Export] public Color NowColor { get; set; } = new Color("FFFFFF");
	[Export] public double WindowSeconds { get; set; } = 12 * 3600;

	public SimClock Clock { get; set; }

	private const float Padding = 12f;
	private const float TrackWidth = 6f;
	private const float DotRadius = 5f;
	private const float NowRadius = 7f;

	private int _hovered = -1;

	public override void _Ready()
	{
		TooltipText = " ";
		CustomMinimumSize = new Vector2(0, 24);
		MouseExited += () => { _hovered = -1; QueueRedraw(); };
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion)
		{
			int hit = HitCheckpoint(motion.Position.X);
			if (hit != _hovered) { _hovered = hit; QueueRedraw(); }
		}
	}

	// draws the track, progress fill, checkpoints, and current time
	public override void _Draw()
	{
		if (Clock == null) return;

		float y = Size.Y / 2f;
		float left = Padding, right = Size.X - Padding;
		float now = TimeToX(Clock.SimTime);

		// track with rounded ends
		DrawRoundedLine(left, right, y, TrackColor);
		// part of the day already elapsed
		if (now > left) DrawRoundedLine(left, now, y, FillColor);

		// checkpoints, outlined, bigger when hovered
		for (int i = 0; i < Clock.Checkpoints.Count; i++)
		{
			var p = new Vector2(TimeToX(Clock.Checkpoints[i]), y);
			float r = i == _hovered ? DotRadius + 2 : DotRadius;
			DrawCircle(p, r + 1.5f, new Color(0, 0, 0, 0.6f));
			DrawCircle(p, r, DotColor);
		}

		// current time knob
		DrawCircle(new Vector2(now, y), NowRadius + 1.5f, new Color(0, 0, 0, 0.5f));
		DrawCircle(new Vector2(now, y), NowRadius, NowColor);
	}

	// shows the checkpoint time under the mouse
	public override string _GetTooltip(Vector2 atPosition)
	{
		int i = HitCheckpoint(atPosition.X);
		return i < 0 ? "" : $"Checkpoint {i + 1} · {SimClock.Format(Clock.Checkpoints[i])}";
	}

	private int HitCheckpoint(float x)
	{
		if (Clock == null) return -1;
		for (int i = Clock.Checkpoints.Count - 1; i >= 0; i--)
			if (Mathf.Abs(TimeToX(Clock.Checkpoints[i]) - x) <= DotRadius + 3) return i;
		return -1;
	}

	private void DrawRoundedLine(float x0, float x1, float y, Color color)
	{
		DrawLine(new Vector2(x0, y), new Vector2(x1, y), color, TrackWidth, true);
		DrawCircle(new Vector2(x0, y), TrackWidth / 2, color);
		DrawCircle(new Vector2(x1, y), TrackWidth / 2, color);
	}


	// maps sim time across the full day
	private float TimeToX(double time)
	{
		double span = Mathf.Max(WindowSeconds, Clock.SimTime - Clock.DayStart);
		float t = (float)((time - Clock.DayStart) / span);
		return Mathf.Lerp(Padding, Size.X - Padding, Mathf.Clamp(t, 0f, 1f));
	}
}
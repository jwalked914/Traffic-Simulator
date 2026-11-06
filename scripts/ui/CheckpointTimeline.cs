using Godot;

public partial class CheckpointTimeline : Control
{
	
	[Export] public double WindowSeconds { get; set; } = 7200;
	[Export] public Color TrackColor { get; set; } = new Color("FFFFFF");
	[Export] public Color DotColor { get; set; } = new Color("7CE36B");
	[Export] public Color NowColor {get; set; } = new Color("DDE3E8");

	public SimClock Clock { get; set; }
	private const float Padding = 10f;
	private const float DotRadius = 6f;

	public override void _Ready() => TooltipText = " ";

	// draw the track, checkpoint, and current time marker
	public override void _Draw()
	{
		if (Clock == null) return;

		float y = Size.Y / 2f;
		DrawLine(new Vector2(Padding, y), new Vector2(Size.X - Padding, y), TrackColor, 3f, true);


		foreach (double time in Clock.Checkpoints)
			DrawCircle(new Vector2(TimeToX(time), y), DotRadius, DotColor);

		DrawLine(new Vector2(TimeToX(Clock.SimTime), y - 8), new Vector2(TimeToX(Clock.SimTime), y + 8), NowColor, 2f);
	}

	// show time of checkpoint under mouse
	public override string _GetTooltip(Vector2 atPosition)
	{
		if (Clock == null) return "";
		foreach (double time in Clock.Checkpoints)
			if (Mathf.Abs(TimeToX(time) - atPosition.X) <= DotRadius + 2)
				return $"Checkpoint {SimClock.Format(time)}";
		return "";
	}

	// maps sim time to position of bar, squeezing once run passes window
	private float TimeToX(double time)
	{
		double start = Clock.StartHour * 3600;
		double span = Mathf.Max(WindowSeconds, Clock.SimTime - start);
		float t = (float)((time - start) / span);
		return Mathf.Lerp(Padding, Size.X - Padding, Mathf.Clamp(t, 0f, 1f));
	}

}

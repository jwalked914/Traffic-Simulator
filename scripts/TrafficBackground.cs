
using Godot;

public partial class TrafficBackground : Control
{

	//grid
	private const float GridSpacing = 40.0f;
	private const float GridLineWidth = 1.0f;

	// roads
	private const float RoadWidth = 70.0f;
	private const float RoadEdgeWidth = 78.0f;

	private const float TopHorizontalRoad = 0.20f;
	private const float BottomHorizontalRoad = 0.80f;

	private const float LeftVerticalRoad = 0.20f;
	private const float RightVerticalRoad = 0.80f;

	private const float LaneOffset = 18.0f;

	//cars
	private const int NumberOfCars = 5;

	private readonly float[] carProgress =
	{
		0.05f,
		0.27f,
		0.48f,
		0.71f,
		0.88f
	};

	private readonly float[] carSpeed =
	{
		0.075f,
		0.095f,
		0.065f,
		0.085f,
		0.070f
	};

	//0 = top horizontal
	//1 = bottom horizontal
	//2 = left vertical
	//3 = right vertical
	private readonly int[] carRoad =
	{
		0,
		1,
		2,
		3,
		0
	};

	// true  = right/down
	// false = left/up
	private readonly bool[] carForward =
	{
		true,
		false,
		true,
		false,
		false
	};

	public override void _Ready()
	{
		SetProcess(true);
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		float deltaTime = (float)delta;

		for (int i = 0; i < NumberOfCars; i++)
		{
			if (carForward[i])
			{
				carProgress[i] += carSpeed[i] * deltaTime;
			}
			else
			{
				carProgress[i] -= carSpeed[i] * deltaTime;
			}

			if (carProgress[i] > 1.0f)
			{
				carProgress[i] -= 1.0f;
			}

			if (carProgress[i] < 0.0f)
			{
				carProgress[i] += 1.0f;
			}
		}

		QueueRedraw();
	}

	public override void _Draw()
	{
		// green background

		DrawRect(
			new Rect2(
				Vector2.Zero,
				Size
			),
			new Color("#26382D")
		);

		//grid
		DrawDesignGrid();

		//color of roads

		Color roadColor = new Color("#292929");
		Color roadEdgeColor = new Color("#171717");

		//lane lines
		Color laneColor = new Color("#E1B93F");

		//road positions
		float topY = Size.Y * TopHorizontalRoad;
		float bottomY = Size.Y * BottomHorizontalRoad;

		float leftX = Size.X * LeftVerticalRoad;
		float rightX = Size.X * RightVerticalRoad;

		// edges of horizontal roads

		DrawLine(
			new Vector2(-100.0f, topY),
			new Vector2(Size.X + 100.0f, topY),
			roadEdgeColor,
			RoadEdgeWidth
		);

		DrawLine(
			new Vector2(-100.0f, bottomY),
			new Vector2(Size.X + 100.0f, bottomY),
			roadEdgeColor,
			RoadEdgeWidth
		);

		// edges of vertical roads
		DrawLine(
			new Vector2(leftX, -100.0f),
			new Vector2(leftX, Size.Y + 100.0f),
			roadEdgeColor,
			RoadEdgeWidth
		);

		DrawLine(
			new Vector2(rightX, -100.0f),
			new Vector2(rightX, Size.Y + 100.0f),
			roadEdgeColor,
			RoadEdgeWidth
		);

		//horizontal roads

		DrawLine(
			new Vector2(-100.0f, topY),
			new Vector2(Size.X + 100.0f, topY),
			roadColor,
			RoadWidth
		);

		DrawLine(
			new Vector2(-100.0f, bottomY),
			new Vector2(Size.X + 100.0f, bottomY),
			roadColor,
			RoadWidth
		);

		//vertical roads

		DrawLine(
			new Vector2(leftX, -100.0f),
			new Vector2(leftX, Size.Y + 100.0f),
			roadColor,
			RoadWidth
		);

		DrawLine(
			new Vector2(rightX, -100.0f),
			new Vector2(rightX, Size.Y + 100.0f),
			roadColor,
			RoadWidth
		);

		//horizontal lane lines
		DrawDashedLine(
			new Vector2(-100.0f, topY),
			new Vector2(Size.X + 100.0f, topY),
			laneColor,
			2.5f,
			18.0f,
			14.0f
		);

		DrawDashedLine(
			new Vector2(-100.0f, bottomY),
			new Vector2(Size.X + 100.0f, bottomY),
			laneColor,
			2.5f,
			18.0f,
			14.0f
		);

		//vertical lane lines
		DrawDashedLine(
			new Vector2(leftX, -100.0f),
			new Vector2(leftX, Size.Y + 100.0f),
			laneColor,
			2.5f,
			18.0f,
			14.0f
		);

		DrawDashedLine(
			new Vector2(rightX, -100.0f),
			new Vector2(rightX, Size.Y + 100.0f),
			laneColor,
			2.5f,
			18.0f,
			14.0f
		);

		//nodes at intersections

		DrawIntersectionNode(
			new Vector2(leftX, topY)
		);

		DrawIntersectionNode(
			new Vector2(rightX, topY)
		);

		DrawIntersectionNode(
			new Vector2(leftX, bottomY)
		);

		DrawIntersectionNode(
			new Vector2(rightX, bottomY)
		);

		//cars
		for (int i = 0; i < NumberOfCars; i++)
		{
			Vector2 carPosition = GetCarPosition(
				i,
				leftX,
				rightX,
				topY,
				bottomY
			);

			DrawCar(
				carPosition,
				GetCarDirection(i),
				i
			);
		}
	}

	//design grid
	private void DrawDesignGrid()
	{
		Color gridColor = new Color(
			0.88f,
			0.88f,
			0.84f,
			0.13f
		);

		//vertical grid lines

		float x = 0.0f;

		while (x <= Size.X)
		{
			DrawLine(
				new Vector2(x, 0.0f),
				new Vector2(x, Size.Y),
				gridColor,
				GridLineWidth
			);

			x += GridSpacing;
		}

		//horizontal grid lines
		float y = 0.0f;

		while (y <= Size.Y)
		{
			DrawLine(
				new Vector2(0.0f, y),
				new Vector2(Size.X, y),
				gridColor,
				GridLineWidth
			);

			y += GridSpacing;
		}

		Color guideColor = new Color(
			0.92f,
			0.92f,
			0.88f,
			0.17f
		);

		float majorSpacing = GridSpacing * 5.0f;

		x = 0.0f;

		while (x <= Size.X)
		{
			DrawLine(
				new Vector2(x, 0.0f),
				new Vector2(x, Size.Y),
				guideColor,
				1.0f
			);

			x += majorSpacing;
		}

		y = 0.0f;

		while (y <= Size.Y)
		{
			DrawLine(
				new Vector2(0.0f, y),
				new Vector2(Size.X, y),
				guideColor,
				1.0f
			);

			y += majorSpacing;
		}
	}

	//intersection node
	private void DrawIntersectionNode(Vector2 position)
	{
		Color outerColor = new Color("#202020");
		Color nodeColor = new Color("#D2D2D0");
		Color centerColor = new Color("#666666");

		DrawCircle(
			position,
			18.0f,
			outerColor
		);

		DrawCircle(
			position,
			13.0f,
			nodeColor
		);

		DrawCircle(
			position,
			4.0f,
			centerColor
		);
	}

	//car position
	private Vector2 GetCarPosition(
		int carIndex,
		float leftX,
		float rightX,
		float topY,
		float bottomY
	)
	{
		float progress = carProgress[carIndex];

		Vector2 direction = GetCarDirection(carIndex);

		switch (carRoad[carIndex])
		{
			//upper horizontal road

			case 0:
			{
				float x = Mathf.Lerp(
					-80.0f,
					Size.X + 80.0f,
					progress
				);

				float laneY = topY;

				if (direction == Vector2.Right)
				{
					laneY -= LaneOffset;
				}
				else
				{
					laneY += LaneOffset;
				}

				return new Vector2(
					x,
					laneY
				);
			}

			// bottom horizontal road

			case 1:
			{
				float x = Mathf.Lerp(
					-80.0f,
					Size.X + 80.0f,
					progress
				);

				float laneY = bottomY;

				if (direction == Vector2.Right)
				{
					laneY -= LaneOffset;
				}
				else
				{
					laneY += LaneOffset;
				}

				return new Vector2(
					x,
					laneY
				);
			}

			//left vertical road
			case 2:
			{
				float y = Mathf.Lerp(
					-80.0f,
					Size.Y + 80.0f,
					progress
				);

				float laneX = leftX;

				if (direction == Vector2.Down)
				{
					laneX -= LaneOffset;
				}
				else
				{
					laneX += LaneOffset;
				}

				return new Vector2(
					laneX,
					y
				);
			}

			//right vertical road
			default:
			{
				float y = Mathf.Lerp(
					-80.0f,
					Size.Y + 80.0f,
					progress
				);

				float laneX = rightX;

				if (direction == Vector2.Down)
				{
					laneX -= LaneOffset;
				}
				else
				{
					laneX += LaneOffset;
				}

				return new Vector2(
					laneX,
					y
				);
			}
		}
	}

	//car direction
	private Vector2 GetCarDirection(int carIndex)
	{
		switch (carRoad[carIndex])
		{
			case 0:
				return carForward[carIndex]
					? Vector2.Right
					: Vector2.Left;

			case 1:
				return carForward[carIndex]
					? Vector2.Right
					: Vector2.Left;

			case 2:
				return carForward[carIndex]
					? Vector2.Down
					: Vector2.Up;

			default:
				return carForward[carIndex]
					? Vector2.Down
					: Vector2.Up;
		}
	}

	//draw car
	private void DrawCar(
		Vector2 position,
		Vector2 direction,
		int carIndex
	)
	{
		Color[] carColors =
		{
			new Color("#B83F35"),
			new Color("#3F6F9E"),
			new Color("#B58A32"),
			new Color("#4D8055"),
			new Color("#79569A")
		};

		Color carColor = carColors[
			carIndex % carColors.Length
		];

		Color windowColor = new Color("#D9D9D7");
		Color wheelColor = new Color("#111111");

		float angle = direction.Angle();

		Transform2D transform = new Transform2D(
			angle,
			position
		);

		//car body

		Vector2 bodySize = new Vector2(
			30.0f,
			14.0f
		);

		Vector2[] body =
		{
			transform * new Vector2(
				-bodySize.X / 2,
				-bodySize.Y / 2
			),

			transform * new Vector2(
				bodySize.X / 2,
				-bodySize.Y / 2
			),

			transform * new Vector2(
				bodySize.X / 2,
				bodySize.Y / 2
			),

			transform * new Vector2(
				-bodySize.X / 2,
				bodySize.Y / 2
			)
		};

		DrawColoredPolygon(
			body,
			carColor
		);

		//car windows
		Vector2 frontWindow =
			position + direction * 5.0f;

		Vector2 rearWindow =
			position - direction * 5.0f;

		DrawCircle(
			frontWindow,
			3.5f,
			windowColor
		);

		DrawCircle(
			rearWindow,
			3.5f,
			windowColor
		);

		//car wheels
		Vector2 perpendicular = new Vector2(
			-direction.Y,
			direction.X
		);

		DrawCircle(
			position
				+ direction * 8.0f
				+ perpendicular * 7.0f,
			3.0f,
			wheelColor
		);

		DrawCircle(
			position
				+ direction * 8.0f
				- perpendicular * 7.0f,
			3.0f,
			wheelColor
		);

		DrawCircle(
			position
				- direction * 8.0f
				+ perpendicular * 7.0f,
			3.0f,
			wheelColor
		);

		DrawCircle(
			position
				- direction * 8.0f
				- perpendicular * 7.0f,
			3.0f,
			wheelColor
		);
	}

	private void DrawDashedLine(
		Vector2 start,
		Vector2 end,
		Color color,
		float width,
		float dashLength,
		float gapLength
	)
	{
		Vector2 direction = end - start;

		float length = direction.Length();

		if (length <= 0.0f)
		{
			return;
		}

		direction = direction.Normalized();

		float current = 0.0f;

		while (current < length)
		{
			float dashEnd = Mathf.Min(
				current + dashLength,
				length
			);

			DrawLine(
				start + direction * current,
				start + direction * dashEnd,
				color,
				width
			);

			current += dashLength + gapLength;
		}
	}
}

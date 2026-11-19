using Godot;
using System;
using System.Collections.Generic;

public partial class PasteController : Node2D
{
	[Export] public Color ValidColor { get; set; } = new Color(0.25f, 0.62f, 1.0f, 0.45f);
	[Export] public Color OverwriteColor { get; set; } = new Color(1.0f, 0.65f, 0.2f, 0.45f);
	[Export] public Color InvalidColor { get; set; } = new Color(0.95f, 0.22f, 0.27f, 0.45f);

	public bool IsActive { get; private set; }
	public string Reason { get; private set; } = "";

	private ScenarioManager _scenario;
	private RoadGraph _roads;
	private UIRoot _ui;

	private ScenarioData _data;
	private readonly List<List<Vector2I>> _paths = new();
	private readonly List<Rect2I> _rects = new();

	private Vector2I _offset;
	private bool _overwrite;
	private bool _valid;
	private bool _stale;

	public override void _Ready()
	{
		_scenario = GetNode<ScenarioManager>("../ScenarioManager");
		_roads = GetNode<RoadGraph>("../RoadGraph");
		_ui = GetNode<UIRoot>("../../UI");
		_ui.ToolChanged += OnToolChanged;
		ZIndex = 10;
		SetProcess(false);
	}

	public override void _ExitTree()
	{
		if (_ui != null) _ui.ToolChanged -= OnToolChanged;
	}

	private void OnToolChanged(int tool) => Cancel();

	// reads the clipboard and enters paste mode, or reports why it can't
	public ImportResult Begin()
	{
		var result = new ImportResult();
		try
		{
			string text = DisplayServer.ClipboardGet();
			if (string.IsNullOrWhiteSpace(text) || text.Length > 2_000_000 ||
				!ScenarioSerializer.TryFromJson(text, out ScenarioData data, out string error))
			{
				result.Errors.Add("The clipboard doesn't contain a copied selection.");
				return result;
			}

			ImportResult check = _scenario.Validate(data, Vector2I.Zero, checkLiveWorld: false);
			if (!check.Success) return check;
			if (data.Roads.Count + data.Structures.Count == 0)
			{
				result.Errors.Add("The copied selection is empty.");
				return result;
			}

			_paths.Clear();
			_rects.Clear();
			foreach (RoadData r in data.Roads)
			{
				var path = new List<Vector2I>();
				foreach (int[] a in r.Path) path.Add(new Vector2I(a[0], a[1]));
				_paths.Add(path);
			}
			foreach (StructureData s in data.Structures)
				_rects.Add(new Structure(s.Anchor, s.Type, s.Props, _roads.CellSize, _roads.RoadWidth).Bounds);

			_data = data;
		}
		catch (Exception e) // hostile or malformed clipboard text
		{
			result.Errors.Add($"Could not read clipboard: {e.Message}");
			return result;
		}

		IsActive = true;
		_stale = true;
		SetProcess(true);
		return result;
	}

	public void Cancel()
	{
		if (!IsActive) return;
		IsActive = false;
		_data = null;
		SetProcess(false);
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		// recomputed from the cursor each frame, so camera movement is handled too
		Vector2I cell = _roads.GlobalToCell(GetGlobalMousePosition());
		bool overwrite = Input.IsKeyPressed(Key.Ctrl);

		if (cell != _offset || overwrite != _overwrite || _stale)
		{
			_offset = cell;
			_overwrite = overwrite;
			_stale = false;
			Revalidate();
			QueueRedraw();
		}
	}

	private void Revalidate()
	{
		ImportResult r = _scenario.Validate(_data, _offset, checkLiveWorld: !_overwrite);
		_valid = r.Success;
		Reason = r.Success ? "" : r.Errors[0];
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsActive || _ui.IsSimulating) return;
		if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;

		if (click.Pressed)
		{
			_overwrite = click.CtrlPressed;
			Revalidate();
			if (_valid)
			{
				// stays in paste mode so the user can stamp repeatedly; Escape exits
				ImportResult result = _scenario.Paste(_data, _offset, _overwrite);
				if (!result.Success) GD.PushWarning(string.Join("; ", result.Errors));
				_stale = true;
			}
		}

		GetViewport().SetInputAsHandled(); // swallow press and release
	}

	public override void _Draw()
	{
		if (!IsActive || GetViewport().GuiGetHoveredControl() != null) return;

		Color color = !_valid ? InvalidColor : _overwrite ? OverwriteColor : ValidColor;
		float width = Mathf.Clamp(_roads.RoadWidth, 1.0f, _roads.CellSize);

		foreach (List<Vector2I> path in _paths)
		{
			for (int i = 1; i < path.Count; i++)
				DrawLine(CellCenter(path[i - 1]), CellCenter(path[i]), color, width, true);
			foreach (Vector2I c in path)
				DrawCircle(CellCenter(c), width * 0.5f, color);
		}

		foreach (Rect2I r in _rects)
		{
			var rect = new Rect2(ToLocal(_roads.CellToGlobal(r.Position + _offset)),
				(Vector2)r.Size * _roads.CellSize);
			DrawRect(rect, color);
		}
	}

	private Vector2 CellCenter(Vector2I cell) => ToLocal(_roads.CellToGlobalCenter(cell + _offset));
}

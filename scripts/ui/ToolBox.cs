using Godot;
using System.Collections.Generic;

public enum ToolType { None, Road, Junction, Structure, Home, Roundabout, Erase}

public partial class ToolBox : Control
{
	[Signal] public delegate void ToolSelectedEventHandler(int tool);
	[Export] public int PageSize { get; set; } = 4;

	private readonly Dictionary<ToolType, Button> _buttons = new();
	private readonly List<Button> _tiles = new();
	private readonly HashSet<Button> _unavailable = new();
	private Button _prev;
	private Button _next;
	private int _page;

	private int PageCount => Mathf.Max(1, Mathf.CeilToInt(_tiles.Count / (float)PageSize));

	// registers the tool buttons and shows the first page
	public override void _Ready()
	{
		_buttons[ToolType.Erase] = GetNode<Button>("PanelContainer/ToolBoxButtons/EditButtons/EraseButton");

		_buttons[ToolType.Road] = GetNode<Button>("PanelContainer/ToolBoxButtons/RoadButton");
		_buttons[ToolType.Junction] = GetNode<Button>("PanelContainer/ToolBoxButtons/JunctionButton");
		_buttons[ToolType.Structure] = GetNode<Button>("PanelContainer/ToolBoxButtons/BuildingsButton");
		_buttons[ToolType.Home] = GetNode<Button>("PanelContainer/ToolBoxButtons/HomeButton");
		_buttons[ToolType.Roundabout] = GetNode<Button>("PanelContainer/ToolBoxButtons/RoundaboutButton");
		foreach (Button button in _buttons.Values)
		{
			button.Pressed += ReportSelected;
			if (button.Disabled) _unavailable.Add(button); // tools not built yet stay disabled
		}

		// order of page
		_tiles.AddRange(new[]
		{
			_buttons[ToolType.Road],
			_buttons[ToolType.Junction],
			_buttons[ToolType.Structure],
			_buttons[ToolType.Home],
			_buttons[ToolType.Roundabout],
		});

		_prev = GetNode<Button>("PanelContainer/ToolBoxButtons/PrevButton");
		_next = GetNode<Button>("PanelContainer/ToolBoxButtons/NextButton");
		_prev.Pressed += () => ShowPage(_page - 1);
		_next.Pressed += () => ShowPage(_page + 1);

		ShowPage(0);
	}

	// shows only tiles of current page and hides navigation arrows at the ends
	private void ShowPage(int page)
	{
		_page = Mathf.Clamp(page, 0, PageCount - 1);
		for (int i = 0; i < _tiles.Count; i++)
			_tiles[i].Visible = i / PageSize == _page;

		_prev.Visible = _page > 0;
		_next.Visible = _page < PageCount - 1;
	}

	// reports the pressed tool, or none if the user turns it off
	private void ReportSelected()
	{
		foreach (var entry in _buttons)
			if (entry.Value.ButtonPressed)
			{
				EmitSignal(SignalName.ToolSelected, (int)entry.Key);
				return;
			}
		EmitSignal(SignalName.ToolSelected, (int)ToolType.None);
	}

	// changes the pressed button, jumps to its page, and reports the selection
	public void SelectTool(ToolType tool)
	{
		foreach (var entry in _buttons)
			entry.Value.SetPressedNoSignal(entry.Key == tool);

		if (_buttons.TryGetValue(tool, out Button selected))
			ShowPage(_tiles.IndexOf(selected) / PageSize);

		EmitSignal(SignalName.ToolSelected, (int)tool);
	}

	// locks the tools while the simulation runs
	public void SetDisabled(bool disabled)
	{
		foreach (Button button in _buttons.Values)
			button.Disabled = disabled || _unavailable.Contains(button);

		_prev.Disabled = disabled;
		_next.Disabled = disabled;
	}
}

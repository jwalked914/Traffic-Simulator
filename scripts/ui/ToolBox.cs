using Godot;
using System.Collections.Generic;

public enum ToolType { None, Road, Junction, Structure }

public partial class ToolBox : Control
{
    [Signal] public delegate void ToolSelectedEventHandler(int tool);
    private readonly Dictionary<ToolType, Button> _buttons = new();

    public override void _Ready()
    {
        _buttons[ToolType.Road] = GetNode<Button>("PanelContainer/ToolBoxButtons/RoadButton");
        _buttons[ToolType.Junction] = GetNode<Button>("PanelContainer/ToolBoxButtons/JunctionButton");
        _buttons[ToolType.Structure] = GetNode<Button>("PanelContainer/ToolBoxButtons/BuildingsButton");
        foreach (Button button in _buttons.Values) button.Pressed += ReportSelected;
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

    // changes the pressed button and reports the selection 
    public void SelectTool(ToolType tool)
    {
        foreach (var entry in _buttons)
            entry.Value.SetPressedNoSignal(entry.Key == tool);
        EmitSignal(SignalName.ToolSelected, (int)tool);
    }
}

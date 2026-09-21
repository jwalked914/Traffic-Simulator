using Godot;
using System;
using System.ComponentModel;

public partial class InspectorPanel : Control
{
	private Button closeButton;
	private ToolBox toolBox;
	private Control lanesField;
	private Control speedField;
	private Control signalField;

	public override void _Ready()
	{
		closeButton = GetNode<Button>("PanelContainer/InspectorContent/HeaderRow/CloseButton");
		closeButton.Pressed += OnCloseButtonPressed;

		lanesField = GetNode<Control>("PanelContainer/InspectorContent/LanesField");
		GD.Print("LanesField found: ", lanesField != null);

		speedField = GetNode<Control>("PanelContainer/InspectorContent/SpeedField");
		GD.Print("SpeedField found: ", speedField != null);
		
		signalField = GetNode<Control>("PanelContainer/InspectorContent/SignalField");
		GD.Print("SignalField found: ", signalField != null);

		toolBox = GetNode<ToolBox>("../ToolBox");
		toolBox.ToolSelected += OnToolSelected;

		Visible = false;
	}

	private void OnToolSelected(int tool)
	{
		var selectedTool = (ToolType)tool;

		switch (selectedTool)
		{
			case ToolType.Road:
				Visible = true;
				lanesField.Visible = true;
				speedField.Visible = true;
				signalField.Visible = true;
				break;

			case ToolType.Junction:
				Visible = true;
				lanesField.Visible = false;
				speedField.Visible = false;
				signalField.Visible = true;
				break;

			case ToolType.None:
			case ToolType.Location:
				Visible = false;
				break;

		}
	}

	private void OnCloseButtonPressed()
	{
		Visible = false;
	}
}

using Godot;

public partial class InspectorPanel : Control
{
	private Button closeButton;
	private Control lanesField;
	private Control speedField;
	private Control signalField;

	public override void _Ready()
	{
		closeButton = GetNode<Button>("PanelContainer/InspectorContent/HeaderRow/CloseButton");
		closeButton.Pressed += OnCloseButtonPressed;

		lanesField = GetNode<Control>("PanelContainer/InspectorContent/LanesField");

		speedField = GetNode<Control>("PanelContainer/InspectorContent/SpeedField");
		
		signalField = GetNode<Control>("PanelContainer/InspectorContent/SignalField");

		Visible = false;
	}

	// display designated fields for tools selected
	public void ShowForTool(ToolType selectedTool)
	{
		switch (selectedTool)
		{
			case ToolType.Road:
				Visible = true;
				lanesField.Visible = true;
				speedField.Visible = true;
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

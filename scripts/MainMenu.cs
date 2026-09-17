using Godot;

public partial class MainMenu : Control
{
	private Button newDesignButton;
	private Button loadDesignButton;
	private Button settingsButton;
	private Button exitButton;

	public override void _Ready()
	{
		newDesignButton = GetNode<Button>("CenterContainer/MenuContainer/NewDesignButton");
		loadDesignButton = GetNode<Button>("CenterContainer/MenuContainer/LoadDesignButton");
		settingsButton = GetNode<Button>("CenterContainer/MenuContainer/SettingsButton");
		exitButton = GetNode<Button>("CenterContainer/MenuContainer/ExitButton");

		newDesignButton.Pressed += OnNewDesignPressed;
		loadDesignButton.Pressed += OnLoadDesignPressed;
		settingsButton.Pressed += OnSettingsPressed;
		exitButton.Pressed += OnExitPressed;
	}

	private void OnNewDesignPressed()
	{
		GetTree().ChangeSceneToFile("res://scenes/traffic_simulator.tscn");
	}

	private void OnLoadDesignPressed()
	{
		GD.Print("Load Design pressed");
	}

	private void OnSettingsPressed()
	{
		GD.Print("Settings pressed");
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}
}

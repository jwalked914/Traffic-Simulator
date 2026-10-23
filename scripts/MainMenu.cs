using Godot;

public partial class MainMenu : Control
{
	private Button newDesignButton;
	private Button loadDesignButton;
	private Button settingsButton;
	private Button exitButton;

	private FileDialog loadFileDialog;
	private const string DesignScene = "res://scenes/traffic_simulator.tscn";
	private const string SaveDirectory = "user://scenarios";
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

		//make sure scenario folder exists
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SaveDirectory));

		// Choose file to load
		loadFileDialog = new FileDialog
		{
			Access = FileDialog.AccessEnum.Userdata,
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Title = "Load Design",
			CurrentDir = SaveDirectory,
			Filters = new[] { "*.json ; Traffic scenario" }
		};

		loadFileDialog.FileSelected += OnLoadFileSelected;
		AddChild(loadFileDialog);
	}

	private void OnNewDesignPressed()
	{
		ScenarioLoadRequest.PendingPath = null;
		GetTree().ChangeSceneToFile(DesignScene);
	}

	private void OnLoadDesignPressed()
	{
		loadFileDialog.PopupCentered(new Vector2I(700,450));
	}

	private void OnLoadFileSelected(string path)
	{
		ScenarioLoadRequest.PendingPath = path;
		Error error = GetTree().ChangeSceneToFile(DesignScene);

		if (error != Error.Ok)
		{
			GD.PrintErr("Could not open the design screen: " + error);
			ScenarioLoadRequest.PendingPath = null;
		}
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

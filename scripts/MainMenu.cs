using Godot;

public partial class MainMenu : Control
{
	private Button newDesignButton;
	private Button loadDesignButton;
	private Button settingsButton;
	private Button exitButton;

	private FileDialog loadFileDialog;
	private Window settingsWindow;
	private CheckButton darkModeToggle;
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

		//make settings window
		CreateSettingsWindow();
	}

	private void CreateSettingsWindow()
	{
		settingsWindow = new Window
		{
			Title = "Settings",
			Size = new Vector2I(420, 200),
			Unresizable = true,
			Visible = false
		};

		settingsWindow.CloseRequested += () => settingsWindow.Hide();

		AddChild(settingsWindow);

		//layout
		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);

		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		settingsWindow.AddChild(margin);

		VBoxContainer layout = new VBoxContainer();
		layout.AddThemeConstantOverride("separation",15);
		margin.AddChild(layout);

		Label description = new Label
		{
			Text = "Choose light/dark mode for the design screen."
		};

		layout.AddChild(description);

		//light dark toggle
		darkModeToggle = new CheckButton
		{
			Text = "Dark Mode",
			ButtonPressed = DesignSettings.DarkMode
		};

		darkModeToggle.Toggled += OnDarkModeToggled;
		layout.AddChild(darkModeToggle);

		//close button
		Button closeButton = new Button
		{
			Text = "Close",
			CustomMinimumSize = new Vector2(100, 35)
		};

		closeButton.Pressed += () => settingsWindow.Hide();
		layout.AddChild(closeButton);
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
		darkModeToggle.ButtonPressed = DesignSettings.DarkMode;
		settingsWindow.PopupCentered();
	}

	private void OnDarkModeToggled(bool enabled)
	{
		DesignSettings.DarkMode = enabled;
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}
}

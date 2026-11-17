
using Godot;

public partial class SettingsPopup : Window
{
    private CheckButton darkModeToggle;

    public override void _Ready()
    {
        Title = "Settings";
        Size = new Vector2I(420, 200);
        Unresizable = true;
        Visible = false;

        CloseRequested += Hide;

        //layout
        MarginContainer margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);

        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(margin);

        VBoxContainer layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 15);
        margin.AddChild(layout);

        Label description = new Label
        {
            Text = "Choose light/dark mode for the design screen."
        };

        layout.AddChild(description);

        //light dark mode toggle
        darkModeToggle = new CheckButton
        {
            Text = "Dark Mode",
            ButtonPressed = DesignSettings.DarkMode
        };

        darkModeToggle.Toggled += OnDarkModeToggled;
        layout.AddChild(darkModeToggle);

    }

    public void ShowSettings()
    {
        darkModeToggle.ButtonPressed = DesignSettings.DarkMode;

        PopupCentered();
    }

    private void OnDarkModeToggled(bool enabled)
    {
        DesignSettings.DarkMode = enabled;
    }
}

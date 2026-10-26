using Godot;

namespace Dcyberpunkgam;

public partial class MainMenu : Control
{
    private Button _newGameButton = null!;
    private Button _continueButton = null!;
    private Button _quitButton = null!;
    private Label _statusLabel = null!;

    public override void _Ready()
    {
        _newGameButton = GetNode<Button>("Center/Panel/NewGame");
        _continueButton = GetNode<Button>("Center/Panel/Continue");
        _quitButton = GetNode<Button>("Center/Panel/Quit");
        _statusLabel = GetNode<Label>("Center/Panel/Status");

        _newGameButton.Pressed += OnNewGamePressed;
        _continueButton.Pressed += OnContinuePressed;
        _quitButton.Pressed += OnQuitPressed;
    }

    private void OnNewGamePressed()
    {
        GameManager.Instance.StartGame();
    }

    private void OnContinuePressed()
    {
        // Placeholder: no save system yet.
        _statusLabel.Text = "No saved game yet.";
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}

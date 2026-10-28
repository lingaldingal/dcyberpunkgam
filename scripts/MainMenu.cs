using Godot;

namespace Dcyberpunkgam;

public partial class MainMenu : Control
{
    // Supported window resolutions, cycled by the RESOLUTION button.
    private static readonly Vector2I[] Resolutions =
    {
        new(1280, 720),
        new(1600, 900),
        new(1920, 1080),
    };

    private Button _newGameButton = null!;
    private Button _continueButton = null!;
    private Button _quitButton = null!;
    private Button _sceneTestButton = null!;
    private Button _resolutionButton = null!;
    private Label _statusLabel = null!;

    // Index into <see cref="Resolutions"/> for the active window size.
    private int _resolutionIndex;

    public override void _Ready()
    {
        _newGameButton = GetNode<Button>("Center/Panel/NewGame");
        _continueButton = GetNode<Button>("Center/Panel/Continue");
        _sceneTestButton = GetNode<Button>("Center/Panel/SceneTest");
        _resolutionButton = GetNode<Button>("Center/Panel/Resolution");
        _quitButton = GetNode<Button>("Center/Panel/Quit");
        _statusLabel = GetNode<Label>("Center/Panel/Status");

        _newGameButton.Pressed += OnNewGamePressed;
        _continueButton.Pressed += OnContinuePressed;
        _sceneTestButton.Pressed += OnSceneTestPressed;
        _resolutionButton.Pressed += OnResolutionPressed;
        _quitButton.Pressed += OnQuitPressed;

        // Adopt whatever size the window currently is (default 1280x720).
        var current = DisplayServer.WindowGetSize();
        _resolutionIndex = 0;
        for (int i = 0; i < Resolutions.Length; i++)
        {
            if (Resolutions[i] == current)
            {
                _resolutionIndex = i;
                break;
            }
        }
        RefreshResolutionLabel();
    }

    /// <summary>The window resolution currently selected (for tests).</summary>
    public Vector2I GetResolution() => Resolutions[_resolutionIndex];

    private void OnResolutionPressed()
    {
        _resolutionIndex = (_resolutionIndex + 1) % Resolutions.Length;
        DisplayServer.WindowSetSize(Resolutions[_resolutionIndex]);
        RefreshResolutionLabel();
    }

    private void RefreshResolutionLabel()
    {
        var r = Resolutions[_resolutionIndex];
        _resolutionButton.Text = $"RESOLUTION: {r.X}x{r.Y}";
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

    private void OnSceneTestPressed()
    {
        GameManager.Instance.StartSceneTest();
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}

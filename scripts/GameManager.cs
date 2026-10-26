using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// Autoload: scene flow between the menu and levels.
/// </summary>
public partial class GameManager : Node
{
    public const string Level01Scene = "res://scenes/level_01.tscn";
    public const string MainMenuScene = "res://scenes/main_menu.tscn";

    public static GameManager Instance { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
    }

    public void StartGame()
    {
        GetTree().ChangeSceneToFile(Level01Scene);
    }

    public void QuitToMenu()
    {
        GetTree().ChangeSceneToFile(MainMenuScene);
    }
}

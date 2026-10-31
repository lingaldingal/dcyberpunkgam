using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// Scene test: a basic 2D cutscene. Two placeholder characters face off
/// (left and right) while a dialogue box at the bottom shows their
/// conversation about cheese. Text reveals character by character and
/// the box auto-scrolls; the speaker is highlighted in the text (bold,
/// in their color) and their portrait glows while the other dims.
/// Click, Space, or Enter advances the conversation; when the last line
/// is answered the scene returns to the main menu.
/// </summary>
public partial class SceneTest : Control
{
    private const float RevealCps = 60f;   // characters per second (sweet spot: 35-60)

    private struct Line
    {
        public string Speaker;
        public string Text;
    }

    // Vex (left) and Kaito (right) discuss cheese.
    private static readonly Line[] Dialogue =
    {
        new() { Speaker = "Vex",   Text = "You're telling me cheese is legal currency in this megacity?" },
        new() { Speaker = "Kaito", Text = "Say what you will. A wheel of Gouda buys more in Neo-Kowloon than a stack of credits." },
        new() { Speaker = "Vex",   Text = "And the rats? They still want the good stuff." },
        new() { Speaker = "Kaito", Text = "The rats get cheddar. Cheddar keeps them docile. Brie, and you're dead." },
        new() { Speaker = "Vex",   Text = "So we're running cheese through a black market. Just like everything else here." },
        new() { Speaker = "Kaito", Text = "Smell that? That's not exhaust. That's a fresh delivery of gruyere. Follow me." },
    };

    private int _lineIndex;
    // Float on purpose: the per-frame reveal increment (delta * RevealCps) is
    // fractional, and storing it in an int truncated the fraction every frame —
    // on a 120 Hz display (delta*60 = 0.5) the typewriter then barely moved.
    private double _charsShown;
    private bool _ended;

    private Control _left = null!;
    private Control _right = null!;
    private ColorRect _leftGlow = null!;
    private ColorRect _rightGlow = null!;
    private RichTextLabel _text = null!;
    private PanelContainer _dialogBox = null!;
    private Button _backButton = null!;
    private Label _hint = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _left = GetNode<Control>("LeftCharacter");
        _right = GetNode<Control>("RightCharacter");
        _leftGlow = GetNode<ColorRect>("LeftCharacter/Glow");
        _rightGlow = GetNode<ColorRect>("RightCharacter/Glow");
        _text = GetNode<RichTextLabel>("DialogueBox/Text");
        _backButton = GetNode<Button>("Back");
        _hint = GetNode<Label>("DialogueBox/Hint");
        _dialogBox = GetNode<PanelContainer>("DialogueBox");
        _text.BbcodeEnabled = true;
        // Let clicks anywhere (including on the background, dialogue box and
        // text) reach this node's _GuiInput instead of being swallowed.
        GetNode<ColorRect>("Background").MouseFilter = MouseFilterEnum.Ignore;
        _dialogBox.MouseFilter = MouseFilterEnum.Ignore;
        _text.MouseFilter = MouseFilterEnum.Ignore;
        _backButton.Pressed += OnBackPressed;
        _backButton.ReleaseFocus();
        ShowLine();
    }

    public override void _Process(double delta)
    {
        if (!_ended)
        {
            var line = Dialogue[_lineIndex];
            if (_charsShown < line.Text.Length)
            {
                _charsShown = Mathf.Min(line.Text.Length, _charsShown + delta * RevealCps);
                UpdateText();
            }

            // Active speaker: glow pulses, portrait full; inactive dims out.
            // Even lines are Vex (left), odd lines are Kaito (right).
            var leftSpeaks = _lineIndex % 2 == 0;
            SetSpeakerHighlight(_left, _leftGlow, leftSpeaks);
            SetSpeakerHighlight(_right, _rightGlow, !leftSpeaks);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            Advance();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_accept") || e.IsActionPressed("ui_select")) // Enter or Space
            Advance();
    }

    /// <summary>
    /// Advance the conversation: a click/key instantly finishes the
    /// typewriter reveal and moves to the next line (or ends the scene
    /// after the last line).
    /// </summary>
    public void Advance()
    {
        if (_ended)
            return;
        if (_lineIndex < Dialogue.Length - 1)
        {
            _lineIndex++;
            ShowLine();
        }
        else
            EndScene();
    }

    private void EndScene()
    {
        _ended = true;
        _hint.Visible = false;
        GameManager.Instance.QuitToMenu();
    }

    private static void SetSpeakerHighlight(Control portrait, ColorRect glow, bool active)
    {
        portrait.Modulate = active ? Colors.White : new Color(1, 1, 1, 0.35f);
        glow.Visible = active;
        if (active)
        {
            float pulse = 0.25f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() * 0.006f));
            glow.Modulate = new Color(1, 1, 1, pulse);
        }
    }

    private void ShowLine()
    {
        var line = Dialogue[_lineIndex];
        _charsShown = 0;
        // The speaker name is shown in full immediately; only the line
        // body types itself out.
        UpdateText();
        _text.ScrollToLine(0);
    }

    private void UpdateText()
    {
        var line = Dialogue[_lineIndex];
        int shown = (int)Math.Min(_charsShown, line.Text.Length);
        _text.Text = SpeakerBbCode(line) + line.Text[..shown];
        _text.ScrollToLine(_text.GetLineCount() - 1);
    }

    private string SpeakerBbCode(Line line)
    {
        string color = _lineIndex % 2 == 0 ? "#ff5c5c" : "#40c0e6";
        return $"[b][color={color}]{line.Speaker}[/color][/b]: ";
    }

    private void OnBackPressed()
    {
        GetNode<GameManager>("/root/GameManager").QuitToMenu();
    }

    // --- Test hooks ---
    public int GetLineIndex() => _lineIndex;
    public string GetSpeakerName() => Dialogue[_lineIndex].Speaker;
    public string GetLineText() => Dialogue[_lineIndex].Text;
    public int GetLineCount() => Dialogue.Length;

    /// <summary>
    /// The current line as revealed so far, including the speaker prefix
    /// ("Speaker: "). Built from state because in headless mode
    /// RichTextLabel has no render pass to parse its BBCode.
    /// </summary>
    public string GetTextPlain()
    {
        var line = Dialogue[_lineIndex];
        int shown = (int)Math.Min(_charsShown, line.Text.Length);
        return $"{line.Speaker}: {line.Text[..shown]}";
    }

    /// <summary>Instantly finish revealing the current line.</summary>
    public void SkipReveal()
    {
        _charsShown = Dialogue[_lineIndex].Text.Length;
    }

    /// <summary>Jump to a specific line index (test hook).</summary>
    public void SetLine(int index)
    {
        _lineIndex = Mathf.Clamp(index, 0, Dialogue.Length - 1);
        ShowLine();
    }
}

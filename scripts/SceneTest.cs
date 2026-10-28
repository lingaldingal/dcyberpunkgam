using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// Scene test: a basic 2D cutscene. Two placeholder characters face off
/// (left and right) while a dialogue box at the bottom shows their
/// conversation about cheese. Text reveals character by character and
/// the box auto-scrolls; the speaker is highlighted in the text (bold,
/// in their color) and their portrait glows while the other dims.
/// </summary>
public partial class SceneTest : Control
{
    private const float RevealCps = 45f;   // characters per second
    private const float LineHold = 1.4f;   // pause after a line is done

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
    private int _charsShown;
    private float _holdTimer;

    private Control _left = null!;
    private Control _right = null!;
    private ColorRect _leftGlow = null!;
    private ColorRect _rightGlow = null!;
    private RichTextLabel _text = null!;
    private Button _backButton = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _left = GetNode<Control>("LeftCharacter");
        _right = GetNode<Control>("RightCharacter");
        _leftGlow = GetNode<ColorRect>("LeftCharacter/Glow");
        _rightGlow = GetNode<ColorRect>("RightCharacter/Glow");
        _text = GetNode<RichTextLabel>("DialogueBox/Text");
        _backButton = GetNode<Button>("Back");
        _backButton.Pressed += OnBackPressed;
        ShowLine();
    }

    public override void _Process(double delta)
    {
        var line = Dialogue[_lineIndex];
        if (_charsShown < line.Text.Length)
        {
            _charsShown = (int)Mathf.Min(line.Text.Length, _charsShown + (float)delta * RevealCps);
            _text.Text = SpeakerBbCode(line) + line.Text[.._charsShown];
            _text.ScrollToLine(_text.GetLineCount() - 1);
        }
        else
        {
            if (++_holdTimer >= LineHold)
            {
                _lineIndex = (_lineIndex + 1) % Dialogue.Length;
                _holdTimer = 0;
                ShowLine();
            }
        }

        // Active speaker: glow pulses, portrait full; inactive dims out.
        // Even lines are Vex (left), odd lines are Kaito (right).
        var leftSpeaks = _lineIndex % 2 == 0;
        SetSpeakerHighlight(_left, _leftGlow, leftSpeaks);
        SetSpeakerHighlight(_right, _rightGlow, !leftSpeaks);
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
        _holdTimer = 0;
        _text.Text = SpeakerBbCode(line);
        _text.ScrollToLine(0);
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
        int shown = Math.Min(_charsShown, line.Text.Length);
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

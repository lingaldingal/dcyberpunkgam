using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// Level 01: placeholder top-down arena. A party of four fights in
/// rounds; at the start of the encounter every character rolls a d5
/// for initiative and the roll is added to their Agility to set turn
/// order (descending), which persists for the rest of the encounter.
/// The active character can be moved (WASD/arrows or clicking a
/// highlighted tile), then END TURN passes control to the next
/// character. When everyone has acted, a new round begins with the
/// same order.
/// </summary>
public partial class Level01 : Control
{
    private const int GridWidth = 12;
    private const int GridHeight = 7;
    private const int TileSize = 64;

    // 1 = walkable, 0 = obstacle (placeholder layout).
    private static readonly int[,] Walkable =
    {
        { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1, 1 },
        { 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
    };

    // Placeholder party. Agility: 10 = normal, 20 = max.
    private static readonly Character[] Party =
    {
        new("Vex", new Color(0.85f, 0.25f, 0.35f), 12),
        new("Kaito", new Color(0.25f, 0.75f, 0.9f), 10),
        new("Rook", new Color(0.4f, 0.85f, 0.45f), 15),
        new("Mira", new Color(0.95f, 0.75f, 0.25f), 10),
    };

    private static readonly (int X, int Y)[] StartCells =
    {
        (1, 1), (2, 1), (1, 2), (2, 2),
    };

    private readonly List<Control> _tokens = new();
    private readonly List<ColorRect> _tokenBodies = new();
    private readonly List<ColorRect> _highlights = new();

    private TurnSystem _turns = null!;
    private bool _turnActive;

    private Control _board = null!;
    private Label _turnLabel = null!;
    private Label _infoLabel = null!;
    private Button _endTurnButton = null!;
    private Button _backButton = null!;

    public override void _Ready()
    {
        // The board handles its own clicks (GuiInput). The UI panel on top
        // of it is click-transparent except for the buttons, so board
        // clicks reach the board while the buttons keep their handling.
        MouseFilter = MouseFilterEnum.Ignore;
        _board = GetNode<Control>("Board");
        _board.MouseFilter = MouseFilterEnum.Stop;
        _board.GuiInput += OnBoardGuiInput;
        foreach (var name in new[]
        {
            "Center", "Center/Panel", "Center/Panel/Title",
            "Center/Panel/HUD", "Center/Panel/HUD/Turn", "Center/Panel/HUD/Info",
        })
            GetNode<Control>(name).MouseFilter = MouseFilterEnum.Ignore;
        BuildBoard();

        _turnLabel = GetNode<Label>("Center/Panel/HUD/Turn");
        _infoLabel = GetNode<Label>("Center/Panel/HUD/Info");
        _endTurnButton = GetNode<Button>("Center/Panel/Actions/EndTurn");
        _backButton = GetNode<Button>("Center/Panel/Actions/Back");
        _endTurnButton.Pressed += OnEndTurnPressed;
        _backButton.Pressed += OnBackPressed;

        _turns = new TurnSystem(Party.ToList());
        _turns.StartEncounter();
        SetActive();
    }

    // Movement keys are handled in _Input (runs before focus handling)
    // so they work even while a button holds keyboard focus.
    public override void _Input(InputEvent @event)
    {
        if (!_turnActive)
            return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        int dx = 0;
        int dy = 0;
        if (key.Keycode == Key.W || key.Keycode == Key.Up)
            dy = -1;
        else if (key.Keycode == Key.S || key.Keycode == Key.Down)
            dy = 1;
        else if (key.Keycode == Key.A || key.Keycode == Key.Left)
            dx = -1;
        else if (key.Keycode == Key.D || key.Keycode == Key.Right)
            dx = 1;
        else
            return;

        TryMove(dx, dy);
        GetViewport().SetInputAsHandled();
    }

    private void OnBoardGuiInput(InputEvent @event)
    {
        if (!_turnActive)
            return;
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            TryMoveToward(mouse.Position);
    }

    private void OnEndTurnPressed()
    {
        if (!_turnActive)
            return;
        _turnActive = false;
        _turns.EndTurn();
        SetActive();
    }

    private void OnBackPressed()
    {
        GetNode<GameManager>("/root/GameManager").QuitToMenu();
    }

    // Test/debug hooks: read turn state and force deterministic d5 rolls.
    public int GetRound() => _turns.Round;
    public string GetCurrentName() => _turns.Current.Name;
    public void SetInitiativeRolls(int[] rolls) => _turns.SetRolls(rolls);
    public int[] GetInitiativeTotals() => (int[])_turns.Total.Clone();

    /// <summary>Character names in this round's turn order (first = next to act).</summary>
    public string[] GetInitiativeOrderNames()
    {
        var order = _turns.GetOrder();
        var names = new string[order.Length];
        for (int i = 0; i < order.Length; i++)
            names[i] = _turns.Combatants[order[i]].Name;
        return names;
    }

    private void SetActive()
    {
        int index = _turns.CurrentIndex;
        _turnActive = true;
        _turnLabel.Text = $"ROUND {_turns.Round} — TURN: {_turns.Current.Name}";
        _infoLabel.Text = BuildInitiativeInfo();
        for (int i = 0; i < _tokenBodies.Count; i++)
            _tokenBodies[i].Modulate = i == index ? Colors.White : new Color(1, 1, 1, 0.55f);
        RefreshHighlights();
    }

    private string BuildInitiativeInfo()
    {
        var parts = new List<string>(_turns.Combatants.Count);
        for (int i = 0; i < _turns.Combatants.Count; i++)
            parts.Add($"{_turns.Combatants[i].Name} {_turns.Combatants[i].Agility}+{_turns.Roll[i]}");
        return string.Join(" · ", parts)
            + "  —  d5 initiative + Agility sets order (high first) · WASD / click to move · END TURN";
    }

    private void TryMove(int dx, int dy)
    {
        MoveTo(CurrentCell() + new Vector2I(dx, dy));
    }

    private void TryMoveToward(Vector2 localPos)
    {
        var from = CurrentCell();
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                var center = new Vector2(x * TileSize + TileSize / 2f, y * TileSize + TileSize / 2f);
                if (Math.Abs(center.X - localPos.X) >= TileSize / 2f)
                    continue;
                if (Math.Abs(center.Y - localPos.Y) >= TileSize / 2f)
                    continue;
                if (Math.Abs(x - from.X) + Math.Abs(y - from.Y) != 1)
                    continue;
                MoveTo(new Vector2I(x, y));
            }
        }
    }

    private void MoveTo(Vector2I next)
    {
        if (next.X < 0 || next.X >= GridWidth || next.Y < 0 || next.Y >= GridHeight)
            return;
        if (Walkable[next.Y, next.X] != 1)
            return;
        if (IsOccupied(next))
            return;
        _tokens[_turns.CurrentIndex].Position = new Vector2(next.X * TileSize, next.Y * TileSize);
        RefreshHighlights();
    }

    private Vector2I CurrentCell()
    {
        return (Vector2I)(_tokens[_turns.CurrentIndex].Position / TileSize);
    }

    private bool IsOccupied(Vector2I cell)
    {
        int current = _turns.CurrentIndex;
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (i == current)
                continue;
            if ((Vector2I)(_tokens[i].Position / TileSize) == cell)
                return true;
        }
        return false;
    }

    private void RefreshHighlights()
    {
        foreach (var rect in _highlights)
            rect.QueueFree();
        _highlights.Clear();

        var from = CurrentCell();
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var next = from + new Vector2I(dx, dy);
            if (next.X < 0 || next.X >= GridWidth || next.Y < 0 || next.Y >= GridHeight)
                continue;
            if (Walkable[next.Y, next.X] != 1 || IsOccupied(next))
                continue;
            var highlight = new ColorRect
            {
                Position = new Vector2(next.X * TileSize, next.Y * TileSize),
                Size = new Vector2(TileSize, TileSize),
                Color = new Color(1, 1, 1, 0.2f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _board.AddChild(highlight);
            _highlights.Add(highlight);
        }
    }

    private void BuildBoard()
    {
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                bool walkable = Walkable[y, x] == 1;
                var tile = new ColorRect
                {
                    Position = new Vector2(x * TileSize, y * TileSize),
                    Size = new Vector2(TileSize, TileSize),
                    Color = walkable ? new Color(0.1f, 0.12f, 0.16f) : new Color(0.04f, 0.045f, 0.06f),
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                _board.AddChild(tile);
            }
        }

        for (int i = 0; i < Party.Length; i++)
        {
            var (name, color) = (Party[i].Name, Party[i].Color);
            var (sx, sy) = StartCells[i];
            var token = new Control
            {
                Name = $"P{i}",
                Position = new Vector2(sx * TileSize, sy * TileSize),
                Size = new Vector2(TileSize, TileSize),
                ZIndex = 1,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            var body = new ColorRect
            {
                Position = new Vector2(8, 8),
                Size = new Vector2(TileSize - 16, TileSize - 16),
                Color = color,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            token.AddChild(body);
            var label = new Label
            {
                Text = name,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = new Vector2(TileSize, TileSize),
            };
            token.AddChild(label);
            _board.AddChild(token);
            _tokens.Add(token);
            _tokenBodies.Add(body);
        }
    }
}

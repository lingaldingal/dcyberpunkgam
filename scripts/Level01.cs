using System.Collections.Generic;
using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// Level 01: top-down turn-based arena. A party of four fights four enemies in
/// rounds. At the start of the encounter every combatant rolls a d5 for
/// initiative; the roll is added to their Agility and everyone acts in
/// descending order of that total (ties: higher Agility, then roster order).
/// The order is rolled once and persists for the rest of the encounter.
///
/// Movement: the active combatant may move up to <see cref="Character.MovementRange"/>
/// tiles (5 + Agility/2, floored) once per turn. Clicking any reachable tile
/// moves the combatant there along the shortest path that avoids obstacles
/// and other tokens (BFS). Character movement is mouse-only; WASD instead
/// pans the camera.
///
/// Weapons + items: every combatant starts equipped with basic head/body gear
/// and a randomly rolled pistol (range 4, 2 dmg) or rifle (range 6, 3 dmg).
/// Shooting hits any target within the weapon's range along one of the 8
/// straight lines (the closest cell in the line is always in range) with a
/// clear line of sight (no obstacle on the line; diagonals cannot cut a wall
/// corner), for the weapon's damage. A shoot may be used before or after the
/// move; a shot always fires from the shooter's CURRENT position. Party
/// members also start with BioStims (heal items).
///
/// Actions: per turn each combatant has ONE action (SHOOT / WAIT / item) and
/// ONE move. Mode switching (SHOOT <-> MOVE) is free; using either budget
/// latches it until END TURN (UNDO restores the turn-start cell and re-arms
/// everything).
///
/// Enemy AI: enemies follow the same rules as the player — one action
/// (shoot / wait / item) and one move per turn. On its turn an enemy spends
/// its full movement range along the BFS path toward the nearest living
/// party member, then (from the new position) shoots it if it is at weapon
/// range with a clear line of sight. The move and the shot each latch the
/// same per-turn budgets a player uses, so the actions menu reflects what
/// the enemy has spent.
///
/// Camera: the board is a plain Control that can be far larger than the window
/// (levels are up to 2000x2000 grids). <see cref="_cameraPos"/> is the top-left
/// pixel of the viewport, clamped to the board; WASD pans it by one tile per
/// frame while held.
/// </summary>
public partial class Level01 : Control
{
    private const int GridWidth = 28;
    private const int GridHeight = 16;
    private const int TileSize = 48;
    // One board tile per frame of WASD panning.
    private const int CameraPanSpeed = TileSize;

    // Frames an enemy waits before acting, so its action is visible.
    private const int EnemyThinkFrames = 12;

    // Basic gear everyone starts with; the weapon is rolled per combatant
    // (pistol or rifle) — see AssignStartingGear.
    private const string HeadGear = "Ballistic Cap";
    private const string BodyGear = "Weave Vest";
    private const int PartyBioStims = 2;

    public enum ActionMode { Move, Shoot }

    private static readonly Vector2I[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    // The 8 straight-line shooting directions (horizontal, vertical, diagonal).
    private static readonly Vector2I[] LineDirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(-1, 1), new(1, -1), new(-1, -1),
    };

    // 1 = walkable, 0 = obstacle (16 rows x 28 cols).
    //
    // Layout notes (also relied on by the tests):
    // - Rows y=0..2 are completely clear, so a token can shoot straight
    //   east/west (and on the diagonals from those rows) without any wall in
    //   the way.
    // - The x=0 column is a clear, obstacle-free vertical lane (y=0..15) — a
    //   long open detour along the west edge.
    // - The x=6 column is clear from y=4..11 (walls at y=0..3 and y=12..15),
    //   and row y=10 is clear from x=3..11 (walls at x=2 and x=12). These two
    //   form a cross whose arms sit exactly at pistol/rifle range from the
    //   open ground around them, so line-of-sight (no shooting through
    //   obstacles) is easy to see and to test.
    private static readonly int[,] Walkable =
    {
        { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 1, 1, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 0, 1, 1, 0, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 0, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1 },
        { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
    };

    // Roster: party (0-3) then enemies (4-7). Token index == roster index.
    private static readonly Character[] Roster =
    {
        new("Vex", new Color(0.85f, 0.25f, 0.35f), 12, 11, false),
        new("Kaito", new Color(0.25f, 0.75f, 0.9f), 10, 9, false),
        new("Rook", new Color(0.4f, 0.85f, 0.45f), 15, 12, false),
        new("Mira", new Color(0.95f, 0.75f, 0.25f), 10, 8, false),
        new("Gutter", new Color(0.6f, 0.3f, 0.8f), 11, 10, true),
        new("Slick", new Color(0.9f, 0.4f, 0.7f), 13, 13, true),
        new("Brute", new Color(0.5f, 0.2f, 0.2f), 16, 12, true),
        new("Wisp", new Color(0.7f, 0.7f, 0.9f), 9, 7, true),
    };

    // Starting cells, one per roster entry.
    private static readonly (int X, int Y)[] StartCells =
    {
        (1, 1), (2, 1), (1, 2), (2, 2),          // party (left)
        (26, 1), (25, 1), (26, 2), (25, 2),      // enemies (right)
    };

    private readonly List<Control> _tokens = new();
    private readonly List<ColorRect> _tokenBodies = new();
    private readonly List<ColorRect> _highlights = new();
    private readonly List<Label> _tokenHpLabels = new();
    private readonly System.Random _rng = new();

    private TurnSystem _turns = null!;
    private bool _turnActive;
    private Dictionary<Vector2I, int> _reachable = new();
    private HashSet<Vector2I> _shootTargets = new();

    // Per-turn budget: ONE action (shoot / wait / item) and ONE move. Mode
    // switching is free; using either budget latches it until END TURN.
    // UNDO (before END TURN) restores the turn-start cell and re-arms both.
    private bool _movedThisTurn;
    private Vector2I _turnStartCell;
    private ActionMode _actionMode = ActionMode.Move;
    private bool _actionUsed;

    // The actions menu: a rectangular panel pinned to the bottom-left with the
    // active character's name + equipment on top and the Shoot / Move / Wait /
    // BioStim buttons below.
    private PanelContainer _actionMenu = null!;
    private Label _actionMenuName = null!;
    private Label _actionMenuEquip = null!;
    private Button _shootButton = null!;
    private Button _moveButton = null!;
    private Button _waitButton = null!;
    private Button _healButton = null!;

    // Enemy auto-move: armed when an enemy is active; fires after a short delay.
    private bool _enemyMoveArmed;
    private int _enemyMoveFrames;
    // Set once the active enemy has moved, so the turn ends exactly once.
    private bool _enemyActed;

    // Top-left pixel of the viewport within the board (see camera section).
    private Vector2 _cameraPos;
    // Test hook: when non-None, UpdateCamera pans as if this key were held.
    private Key _forcedPanKey;

    private Control _board = null!;
    private Label _turnLabel = null!;
    private Label _infoLabel = null!;
    private Button _endTurnButton = null!;
    private Button _undoButton = null!;
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
        AssignStartingGear();
        BuildBoard();
        BuildActionMenu();
        InitializeCamera();

        _turnLabel = GetNode<Label>("Center/Panel/HUD/Turn");
        _infoLabel = GetNode<Label>("Center/Panel/HUD/Info");
        _endTurnButton = GetNode<Button>("Center/Panel/Actions/EndTurn");
        _undoButton = GetNode<Button>("Center/Panel/Actions/Undo");
        _backButton = GetNode<Button>("Center/Panel/Actions/Back");
        _endTurnButton.Pressed += OnEndTurnPressed;
        _undoButton.Pressed += OnUndoPressed;
        _backButton.Pressed += OnBackPressed;

        _turns = new TurnSystem(Roster.ToList());
        _turns.StartEncounter();
        SetActive();
    }

    public override void _Process(double delta)
    {
        // Delayed enemy action: count down frames, then move — but only while
        // the armed combatant is still current (a turn change re-arms).
        if (_enemyMoveArmed && _turnActive && _turns.Current.IsEnemy)
        {
            _enemyMoveFrames--;
            if (_enemyMoveFrames <= 0)
            {
                _enemyMoveArmed = false;
                EnemyAct();
            }
        }

        UpdateCamera((float)delta);
        ClampActionMenu();
    }

    // WASD pans the camera (held keys, so it works while a button has focus).
    // Character movement is mouse-only, so WASD no longer moves any token.
    private void UpdateCamera(float delta)
    {
        if (_board == null)
            return;

        float step = CameraPanSpeed * delta;
        int dirX = 0;
        int dirY = 0;
        // A forced pan key (test hook) takes precedence over real key state.
        bool forced = false;
        if (_forcedPanKey != Key.None)
        {
            forced = true;
            if (_forcedPanKey == Key.W || _forcedPanKey == Key.Up)
                dirY -= 1;
            else if (_forcedPanKey == Key.S || _forcedPanKey == Key.Down)
                dirY += 1;
            else if (_forcedPanKey == Key.A || _forcedPanKey == Key.Left)
                dirX -= 1;
            else if (_forcedPanKey == Key.D || _forcedPanKey == Key.Right)
                dirX += 1;
        }
        if (!forced)
        {
            if (Godot.Input.IsKeyPressed(Key.W) || Godot.Input.IsKeyPressed(Key.Up))
                dirY -= 1;
            else if (Godot.Input.IsKeyPressed(Key.S) || Godot.Input.IsKeyPressed(Key.Down))
                dirY += 1;
            else if (Godot.Input.IsKeyPressed(Key.A) || Godot.Input.IsKeyPressed(Key.Left))
                dirX -= 1;
            else if (Godot.Input.IsKeyPressed(Key.D) || Godot.Input.IsKeyPressed(Key.Right))
                dirX += 1;
        }

        if (dirX == 0 && dirY == 0)
        {
            ApplyCamera();
            return;
        }

        // Panning "right"/"down" reveals the board's right/bottom edge, i.e.
        // the viewport's top-left pixel moves in that direction.
        _cameraPos = new Vector2(
            _cameraPos.X + dirX * step,
            _cameraPos.Y + dirY * step);
        ApplyCamera();
    }

    // The board's pixel extent. Equals Grid*TileSize for the level's own grid,
    // but is configurable (see SetBoardPixelSize) so the panning/clamping logic
    // can be exercised against a large board (e.g. a 2000x2000 level).
    private float _boardPixelW;
    private float _boardPixelH;

    // Clamp the camera to the board and apply it. The board is a plain
    // Control, so it is shown by offsetting it by -camera (the board's own
    // children keep their board-local coordinates; only the board moves).
    // The camera is the board pixel at the viewport's top-left, so it is
    // always >= 0 and, when the board is larger than the viewport in a
    // dimension, at most (board - view) in that dimension.
    private void ApplyCamera()
    {
        if (_board == null)
            return;

        float viewW = Size.X > 0 ? Size.X : 1;
        float viewH = Size.Y > 0 ? Size.Y : 1;

        float maxX = Mathf.Max(0f, _boardPixelW - viewW);
        float maxY = Mathf.Max(0f, _boardPixelH - viewH);
        _cameraPos = new Vector2(
            Mathf.Clamp(_cameraPos.X, 0f, maxX),
            Mathf.Clamp(_cameraPos.Y, 0f, maxY));

        _board.Position = -_cameraPos;
    }

    private void InitializeCamera()
    {
        _boardPixelW = GridWidth * TileSize;
        _boardPixelH = GridHeight * TileSize;
        // Start pinned to the board's top-left corner.
        _cameraPos = Vector2.Zero;
        ApplyCamera();
    }

    /// <summary>Test hook: treat the board as <paramref name="pixels"/> wide/tall.</summary>
    public void SetBoardPixelSize(float w, float h)
    {
        _boardPixelW = w;
        _boardPixelH = h;
        ApplyCamera();
    }

    private void OnBoardGuiInput(InputEvent @event)
    {
        // Each party turn allows ONE action (shoot) and ONE move; each path
        // latches itself (see MoveTo / TryShootAt).
        if (!_turnActive || _turns.Current.IsEnemy)
            return;
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            TryBoardClick(mouse.Position);
    }

    private void OnEndTurnPressed()
    {
        if (!_turnActive)
            return;
        _turnActive = false;
        _turns.EndTurn();
        SetActive();
    }

    // Restore the active party member to the cell they started their turn in.
    // Allowed once, before END TURN, so a move can be reconsidered. Undoing
    // also re-arms the turn's action (in the current mode).
    private void OnUndoPressed()
    {
        if (!_turnActive || _turns.Current.IsEnemy || !_movedThisTurn)
            return;
        MoveTokenTo(_turns.CurrentIndex, _turnStartCell);
        _movedThisTurn = false;
        _actionUsed = false;
        UpdateActionMenu();
    }

    private void OnBackPressed()
    {
        GetNode<GameManager>("/root/GameManager").QuitToMenu();
    }

    // --- Test/debug hooks -------------------------------------------------

    public int GetRound() => _turns.Round;
    public string GetCurrentName() => _turns.Current.Name;
    public void SetInitiativeRolls(int[] rolls)
    {
        _turns.SetRolls(rolls);
        SetActive();
    }
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

    /// <summary>How many tiles the active combatant may move (5 + Agility/2).</summary>
    public int GetMovementRange() => _turns.Current.MovementRange;

    /// <summary>Whether the active combatant can move to cell (x, y).</summary>
    public bool IsReachable(int x, int y) => _reachable.ContainsKey(new Vector2I(x, y));

    /// <summary>Whether the active party member has already moved this turn.</summary>
    public bool HasMovedThisTurn() => _movedThisTurn;

    /// <summary>Test hook: restore the active party member to their turn-start cell.</summary>
    public void UndoMove() => OnUndoPressed();

    /// <summary>Test hook: make the named combatant active without re-rolling.</summary>
    public void ForceCurrent(string name)
    {
        int index = -1;
        for (int i = 0; i < Roster.Length; i++)
        {
            if (Roster[i].Name == name)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return;
        _turns.ForceCurrent(index);
        SetActive();
    }

    /// <summary>Test hook: the board cell a token currently occupies.</summary>
    public Vector2I GetTokenCell(string name)
    {
        int index = -1;
        for (int i = 0; i < Roster.Length; i++)
        {
            if (Roster[i].Name == name)
            {
                index = i;
                break;
            }
        }
        return index < 0 ? default : (Vector2I)(_tokens[index].Position / TileSize);
    }

    /// <summary>Test hook: the board pixel the viewport's top-left is at.</summary>
    public Vector2 GetCameraPos() => _cameraPos;

    /// <summary>Test hook: pan the camera by a pixel offset (clamped).</summary>
    public void PanCameraBy(Vector2 offset)
    {
        _cameraPos += offset;
        ApplyCamera();
    }

    /// <summary>
    /// Test hook: make <paramref name="key"/> behave as held for the next
    /// <see cref="_Process"/> (Key.None clears it), so the WASD pan path can be
    /// exercised without a real key event.
    /// </summary>
    public void SetPanKey(Key key) => _forcedPanKey = key;

    /// <summary>Current HP of the named combatant.</summary>
    public int GetHitPoints(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? -1 : _turns.Combatants[index].HitPoints;
    }

    /// <summary>Whether the named combatant is still on the map.</summary>
    public bool IsAlive(string name)
    {
        int index = IndexOf(name);
        return index >= 0 && !_turns.Combatants[index].IsDead;
    }

    /// <summary>Whether cell (x, y) is a valid shoot target for the active combatant.</summary>
    public bool IsShootTarget(int x, int y) => _shootTargets.Contains(new Vector2I(x, y));

    /// <summary>Test hook: teleport a token to a cell (used to set up shoot distances).</summary>
    public void SetTokenCell(string name, int x, int y)
    {
        int index = IndexOf(name);
        if (index < 0)
            return;
        _tokens[index].Position = new Vector2(x * TileSize, y * TileSize);
    }

    /// <summary>Test hook: the actions menu's position and size.</summary>
    public Vector2 GetActionMenuPos() => _actionMenu.Position;

    private int IndexOf(string name)
    {
        for (int i = 0; i < Roster.Length; i++)
        {
            if (Roster[i].Name == name)
                return i;
        }
        return -1;
    }

    // --- Equipment + items -------------------------------------------------

    // Give every combatant basic head/body gear and a randomly rolled weapon
    // (50/50 pistol or rifle). Party members also start with BioStims.
    private void AssignStartingGear()
    {
        for (int i = 0; i < Roster.Length; i++)
        {
            var c = Roster[i];
            var weapon = _rng.Next(2) == 0 ? Weapon.Pistol : Weapon.Rifle;
            c.Equipment = new Equipment(HeadGear, BodyGear, weapon);
            if (!c.IsEnemy)
                c.Items[Item.BioStim] = PartyBioStims;
        }
    }

    /// <summary>The named combatant's equipped weapon name ("" if unknown).</summary>
    public string GetWeaponName(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? "" : Roster[index].Equipment.Weapon.Name;
    }

    /// <summary>The named combatant's weapon range in squares.</summary>
    public int GetWeaponRange(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? 0 : Roster[index].Equipment.Weapon.Range;
    }

    /// <summary>The named combatant's weapon damage per hit.</summary>
    public int GetWeaponDamage(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? 0 : Roster[index].Equipment.Weapon.Damage;
    }

    /// <summary>Test hook: swap a combatant's weapon (recomputes if they are active).</summary>
    public void SetWeapon(string name, bool rifle)
    {
        int index = IndexOf(name);
        if (index < 0)
            return;
        Roster[index].Equipment = new Equipment(HeadGear, BodyGear, rifle ? Weapon.Rifle : Weapon.Pistol);
        if (index == _turns.CurrentIndex)
        {
            ComputeShootTargets();
            RefreshHighlights();
        }
        UpdateActionMenu();
    }

    /// <summary>Test hook: a combatant's equipped head/body gear text.</summary>
    public string GetEquipment(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? "" : Roster[index].Equipment.ToText();
    }

    /// <summary>How many BioStims the named combatant is carrying.</summary>
    public int GetItemCount(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? 0 : Roster[index].GetItemCount(Item.BioStim);
    }

    /// <summary>
    /// Test hook: use one BioStim for the named combatant (heal + decrement),
    /// regardless of whose turn it is. Returns false if none were available.
    /// </summary>
    public bool UseItem(string name)
    {
        int index = IndexOf(name);
        if (index < 0)
            return false;
        var c = Roster[index];
        if (c.UseItem(Item.BioStim))
        {
            RefreshTokenHp(index);
            UpdateActionMenu();
            return true;
        }
        return false;
    }

    // The active party member spends its action using a BioStim (if it has one
    // and is below full HP).
    private void OnHealPressed()
    {
        if (!_turnActive || _turns.Current.IsEnemy || _actionUsed)
            return;
        var c = _turns.Current;
        if (c.UseItem(Item.BioStim))
        {
            _infoLabel.Text = $"{c.Name} uses a BioStim and recovers {Item.BioStim.HealValue} HP ({c.HitPoints} HP)";
            RefreshTokenHp(_turns.CurrentIndex);
            MarkActionUsed();
        }
    }

    // --- Turn / movement --------------------------------------------------

    private void SetActive()
    {
        int index = _turns.CurrentIndex;
        _turnActive = true;
        _movedThisTurn = false;
        _enemyActed = false;
        _actionUsed = false;
        _turnStartCell = CurrentCell();
        _turnLabel.Text = $"ROUND {_turns.Round} — TURN: {_turns.Current.Name}";
        _infoLabel.Text = BuildInitiativeInfo();
        for (int i = 0; i < _tokenBodies.Count; i++)
            _tokenBodies[i].Modulate = i == index ? Colors.White : new Color(1, 1, 1, 0.55f);
        _reachable = ComputeDistances(CurrentCell(), _turns.Current.MovementRange, index);
        ComputeShootTargets();
        RefreshHighlights();
        UpdateActionMenu();

        _enemyMoveArmed = _turns.Current.IsEnemy;
        _enemyMoveFrames = EnemyThinkFrames;
    }

    private string BuildInitiativeInfo()
    {
        var parts = new List<string>(_turns.Combatants.Count);
        for (int i = 0; i < _turns.Combatants.Count; i++)
        {
            var c = _turns.Combatants[i];
            parts.Add($"{c.Name} {c.Agility}+{_turns.Roll[i]}{(c.IsEnemy ? " (E)" : "")}");
        }
        return string.Join(" · ", parts)
            + "  —  initiative d5 + Agility sets order (high first) · move = 5 + Agi/2 · per turn: ONE action (SHOOT / WAIT / BioStim) + ONE move · shoot = weapon range (pistol 4 / rifle 6) straight line, clear line of sight, weapon dmg (2 / 3) · HP = 5 + Con · WASD pans the map · END TURN";
    }

    // BFS: shortest-path distance from <start> to every reachable tile, treating
    // obstacles and every token except the moving one as walls.
    private Dictionary<Vector2I, int> ComputeDistances(Vector2I start, int range, int movingIndex)
    {
        var dist = new Dictionary<Vector2I, int> { [start] = 0 };
        var queue = new Queue<Vector2I>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int d = dist[cur];
            if (d >= range)
                continue;
            foreach (var dir in Dirs)
            {
                var next = cur + dir;
                if (next.X < 0 || next.X >= GridWidth || next.Y < 0 || next.Y >= GridHeight)
                    continue;
                if (Walkable[next.Y, next.X] != 1)
                    continue;
                if (IsOccupiedBy(next, movingIndex))
                    continue;
                if (dist.ContainsKey(next))
                    continue;
                dist[next] = d + 1;
                queue.Enqueue(next);
            }
        }
        return dist;
    }

    // Distance from <start> to <cell> in a BFS map where <cell> may itself be
    // a "wall" (a token's own cell, e.g. a party member the enemy targets).
    // Falls back to a neighbour's distance + 1, or int.MaxValue if unreachable.
    private static int DistanceToCell(Dictionary<Vector2I, int> dist, Vector2I cell)
    {
        if (dist.TryGetValue(cell, out int d))
            return d;
        int best = int.MaxValue;
        foreach (var dir in Dirs)
        {
            var n = cell + dir;
            if (dist.TryGetValue(n, out int dn) && dn + 1 < best)
                best = dn + 1;
        }
        return best;
    }

    private void MoveTo(Vector2I next)
    {
        if (_movedThisTurn || !_reachable.ContainsKey(next))
            return;
        MovePartyTo(next);
    }

    private void TryBoardClick(Vector2 localPos)
    {
        var cell = new Vector2I((int)(localPos.X / TileSize), (int)(localPos.Y / TileSize));
        if (_actionMode == ActionMode.Shoot)
            TryShootAt(cell);
        else
            MoveTo(cell);
    }

    // Shoot: the target must sit within the active combatant's weapon range
    // along one of the 8 straight lines (horizontal, vertical, diagonal) with
    // a clear line of sight — from the combatant's CURRENT position, whether
    // or not it has moved this turn. Deals the weapon's damage.
    private void TryShootAt(Vector2I cell)
    {
        if (_actionUsed || !_shootTargets.Contains(cell))
            return;
        int targetIndex = -1;
        for (int i = 0; i < _tokens.Count; i++)
        {
            if ((Vector2I)(_tokens[i].Position / TileSize) == cell)
            {
                targetIndex = i;
                break;
            }
        }
        if (targetIndex < 0)
            return;
        var target = _turns.Combatants[targetIndex];
        if (target.IsDead)
            return;
        ApplyShot(_turns.CurrentIndex, targetIndex);
        MarkActionUsed();
    }

    // Common shot application: weapon damage on the target, HUD line, token
    // HP update, death removal. Shared by the party's shoot action and the
    // enemy AI.
    private void ApplyShot(int shooterIndex, int targetIndex)
    {
        var shooter = _turns.Combatants[shooterIndex];
        var target = _turns.Combatants[targetIndex];
        int damage = shooter.Equipment.Weapon.Damage;
        target.HitPoints -= damage;
        _infoLabel.Text = $"{shooter.Name} shoots {target.Name} with the {shooter.Equipment.Weapon.Name} for {damage} ({target.HitPoints} HP left)";
        RefreshTokenHp(targetIndex);
        if (target.IsDead)
            KillToken(targetIndex);
    }

    // One action per turn: once the active combatant has moved, shot or waited,
    // board clicks are ignored and the action buttons disable (END TURN /
    // WAIT / UNDO stay available).
    private void MarkActionUsed()
    {
        _actionUsed = true;
        UpdateActionMenu();
    }

    private void OnWaitPressed()
    {
        if (!_turnActive || _actionUsed)
            return;
        _infoLabel.Text = $"{_turns.Current.Name} waits.";
        MarkActionUsed();
    }

    private void OnShootModePressed() => SetActionMode(ActionMode.Shoot);
    private void OnMoveModePressed() => SetActionMode(ActionMode.Move);

    // Switch the board-click mode. Mode switching is free: it does not
    // consume or re-arm either of the turn's budgets (one action + one move).
    public void SetActionMode(ActionMode mode)
    {
        _actionMode = mode;
        UpdateActionMenu();
        RefreshHighlights();
    }

    private void MoveTokenTo(int index, Vector2I cell)
    {
        _tokens[index].Position = new Vector2(cell.X * TileSize, cell.Y * TileSize);
        _reachable = ComputeDistances(cell, _turns.Combatants[index].MovementRange, index);
        // A move changes where shots are fired from: refresh the shoot-target
        // set from the new position so a shoot-after-move aims from here.
        if (index == _turns.CurrentIndex)
            ComputeShootTargets();
        RefreshHighlights();
    }

    // Player move: the turn-start cell is remembered so UNDO can restore it
    // before END TURN. The reachable set is NOT recomputed here — it stays
    // frozen to the turn-start position so the full range (and highlights)
    // remain valid for the rest of the turn.
    private void MovePartyTo(Vector2I next)
    {
        _tokens[_turns.CurrentIndex].Position = new Vector2(next.X * TileSize, next.Y * TileSize);
        _movedThisTurn = true;
        // A move changes where shots are fired from: refresh the shoot-target
        // set from the new position so a shoot-after-move aims from here.
        ComputeShootTargets();
        RefreshHighlights();
    }

    /// <summary>Test hook: act for the active enemy now (AI move + shoot + end turn).</summary>
    public void ForceEnemyMove() => EnemyAct();

    // Enemy AI (same rules as the player: one action + one move per turn):
    // the enemy spends its full movement range walking the BFS path toward
    // the nearest living party member, then shoots it — from the new
    // position — if it ended up at weapon range with a clear line of sight.
    // The move and the shot each latch the same per-turn budgets a player's
    // actions use (_movedThisTurn / _actionUsed), so the actions menu shows
    // exactly what the enemy has spent. Then its turn ends automatically.
    // Guarded so it acts exactly once per turn.
    private void EnemyAct()
    {
        if (_enemyActed)
            return;
        _enemyActed = true;
        int index = _turns.CurrentIndex;
        int targetIndex = FindNearestPartyMember(index);
        if (targetIndex < 0)
        {
            // No living party member reachable (or all dead): just end the turn.
            _turns.EndTurn();
            SetActive();
            return;
        }

        EnemyMoveToward(index, targetIndex);
        EnemyShoot(index, targetIndex);
        // Enemy has acted: advance to the next combatant.
        _turns.EndTurn();
        SetActive();
    }

    // Nearest living party member by BFS path distance from <index> (-1 if
    // none is reachable or all are dead). A party member's own token cell is
    // a BFS "wall", so DistanceToCell also accepts a cell whose neighbour is
    // in the map (distance = 1 more).
    private int FindNearestPartyMember(int index)
    {
        var dist = ComputeDistances(CurrentCell(), int.MaxValue, index);
        int targetIndex = -1;
        int best = int.MaxValue;
        for (int i = 0; i < _turns.Combatants.Count; i++)
        {
            var c = _turns.Combatants[i];
            if (c.IsEnemy || c.IsDead)
                continue;
            int d = DistanceToCell(dist, (Vector2I)(_tokens[i].Position / TileSize));
            if (d < best)
            {
                best = d;
                targetIndex = i;
            }
        }
        return targetIndex;
    }

    // The enemy's one move for the turn: walk the BFS path toward
    // <targetIndex> up to its full movement range (or until it is adjacent,
    // since the target's token is a BFS "wall", or boxed in by obstacles).
    // Latches the move budget exactly like a player's move.
    private void EnemyMoveToward(int index, int targetIndex)
    {
        var enemy = _turns.Combatants[index];
        int steps = enemy.MovementRange;
        var pos = CurrentCell();
        while (steps-- > 0)
        {
            // BFS from the target treating the ENEMY as the moving token, so
            // the enemy's own cell is in the map (its distance = how far it
            // still is). The target's token is a wall, so once adjacent no
            // neighbour can get any closer.
            var toward = ComputeDistances(
                (Vector2I)(_tokens[targetIndex].Position / TileSize), int.MaxValue, index);
            if (!toward.TryGetValue(pos, out int curD))
                break; // boxed in by obstacles
            if (curD <= 1)
                break; // adjacent to the target: its cell is the wall
            Vector2I? step = null;
            int bestStep = int.MaxValue;
            foreach (var dir in Dirs)
            {
                var next = pos + dir;
                if (next.X < 0 || next.X >= GridWidth || next.Y < 0 || next.Y >= GridHeight)
                    continue;
                if (Walkable[next.Y, next.X] != 1 || IsOccupiedBy(next, index))
                    continue;
                if (!toward.TryGetValue(next, out int d))
                    continue;
                if (d < bestStep)
                {
                    bestStep = d;
                    step = next;
                }
            }
            if (step == null || bestStep >= curD)
                break; // no neighbour is any closer: stop (adjacent or boxed)
            pos = step.Value;
        }
        if (pos != CurrentCell())
        {
            MoveTokenTo(index, pos);
            _movedThisTurn = true;
            UpdateActionMenu();
        }
    }

    // The enemy's action: if <targetIndex> sits at weapon range from the
    // enemy's CURRENT position with a clear line of sight, shoot it (weapon
    // damage). Latches the action budget exactly like a player's shoot.
    private void EnemyShoot(int index, int targetIndex)
    {
        var enemy = _turns.Combatants[index];
        int weaponRange = enemy.Equipment.Weapon.Range;
        var from = CurrentCell();
        var cell = (Vector2I)(_tokens[targetIndex].Position / TileSize);
        if (IsShootCell(from, cell, weaponRange) && HasLineOfSight(from, cell, weaponRange))
        {
            ApplyShot(index, targetIndex);
            _actionUsed = true;
            UpdateActionMenu();
        }
    }

    private Vector2I CurrentCell()
    {
        return (Vector2I)(_tokens[_turns.CurrentIndex].Position / TileSize);
    }

    // A cell is occupied by a LIVE token. Dead tokens are removed from the map
    // and no longer block movement.
    private bool IsOccupiedBy(Vector2I cell, int ignoreIndex)
    {
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (i == ignoreIndex)
                continue;
            if (_tokens[i].Visible == false)
                continue;
            if ((Vector2I)(_tokens[i].Position / TileSize) == cell)
                return true;
        }
        return false;
    }

    // <to> is a shoot position for a weapon of <range> iff it sits no more
    // than <range> squares away from <from> along one of the 8 straight lines
    // (horizontal, vertical, diagonal) — the closest cell in the line is
    // always in range.
    private static bool IsShootCell(Vector2I from, Vector2I to, int range)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        return (Math.Abs(dx) <= range && dy == 0 && dx != 0)
            || (Math.Abs(dy) <= range && dx == 0 && dy != 0)
            || (Math.Abs(dx) <= range && Math.Abs(dy) <= range && dx != 0 && dy != 0);
    }

    // Clear line of sight from <from> to <to> (within a weapon's range on one
    // of the 8 lines): every intermediate cell between them must be walkable.
    // On diagonals a wall at either corner (the two cells adjacent to a
    // diagonal step) also blocks the shot.
    private static bool HasLineOfSight(Vector2I from, Vector2I to, int range)
    {
        int dist = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)); // 1..range
        if (dist > range)
            return false;
        int dx = Math.Sign(to.X - from.X); // -1, 0 or 1 per step
        int dy = Math.Sign(to.Y - from.Y);
        var cur = from;
        for (int i = 0; i < dist - 1; i++)
        {
            cur = new Vector2I(cur.X + dx, cur.Y + dy);
            if (Walkable[cur.Y, cur.X] != 1)
                return false;
            if (dx != 0 && dy != 0 && CornerBlocked(cur, new Vector2I(dx, dy)))
                return false;
        }
        return true;
    }

    // A diagonal step through <cell> is blocked if a wall sits at either
    // corner (the two cells adjacent to the step's line); a corner past the
    // grid edge counts as a wall too (bounds are checked before reading).
    private static bool CornerBlocked(Vector2I cell, Vector2I dir)
    {
        var a = new Vector2I(cell.X - dir.X, cell.Y);
        if (a.X < 0 || a.X >= GridWidth || a.Y < 0 || a.Y >= GridHeight)
            return true;
        if (Walkable[a.Y, a.X] != 1)
            return true;
        var b = new Vector2I(cell.X, cell.Y + dir.Y);
        if (b.X < 0 || b.X >= GridWidth || b.Y < 0 || b.Y >= GridHeight)
            return true;
        return Walkable[b.Y, b.X] != 1;
    }

    // Rebuild the shoot-target set for the active combatant from its CURRENT
    // position: every cell no more than its weapon range away on one of the 8
    // straight lines with a clear line of sight.
    private void ComputeShootTargets()
    {
        _shootTargets.Clear();
        if (_turns.Combatants[_turns.CurrentIndex].IsEnemy)
            return;
        int range = _turns.Combatants[_turns.CurrentIndex].Equipment.Weapon.Range;
        var from = CurrentCell();
        foreach (var dir in LineDirs)
        {
            // Walk the line one cell at a time; the first obstacle ends it.
            // EVERY reachable cell up to <range> away is a target — a weapon
            // can hit at any distance within its range, so the whole line
            // (and, on diagonals, both corners of every step) must be walkable.
            for (int step = 1; step <= range; step++)
            {
                var cell = from + dir * step;
                if (cell.X < 0 || cell.X >= GridWidth || cell.Y < 0 || cell.Y >= GridHeight)
                    break;
                if (Walkable[cell.Y, cell.X] != 1)
                    break;
                if (dir.X != 0 && dir.Y != 0 && CornerBlocked(cell, dir))
                    break;
                _shootTargets.Add(cell);
            }
        }
    }

    private void RefreshHighlights()
    {
        foreach (var rect in _highlights)
            rect.QueueFree();
        _highlights.Clear();

        // Move mode: green highlights for reachable tiles. Shoot mode: red
        // highlights on every shootable tile (in range with line of sight,
        // from the CURRENT position; only when the active combatant has not
        // used its action yet).
        if (_actionMode == ActionMode.Shoot && !_turns.Combatants[_turns.CurrentIndex].IsEnemy)
        {
            foreach (var cell in _shootTargets)
            {
                var highlight = new ColorRect
                {
                    Position = new Vector2(cell.X * TileSize, cell.Y * TileSize),
                    Size = new Vector2(TileSize, TileSize),
                    Color = new Color(0.95f, 0.35f, 0.3f, 0.25f),
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                _board.AddChild(highlight);
                _highlights.Add(highlight);
            }
        }
        else
        {
            foreach (var cell in _reachable.Keys)
            {
                var highlight = new ColorRect
                {
                    Position = new Vector2(cell.X * TileSize, cell.Y * TileSize),
                    Size = new Vector2(TileSize, TileSize),
                    Color = new Color(0.3f, 0.9f, 0.6f, 0.18f),
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                _board.AddChild(highlight);
                _highlights.Add(highlight);
            }
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

        for (int i = 0; i < Roster.Length; i++)
        {
            var (name, color) = (Roster[i].Name, Roster[i].Color);
            var (sx, sy) = StartCells[i];
            // Party tokens are P0.., enemy tokens E0.. (test hooks key off this).
            var tokenName = Roster[i].IsEnemy ? $"E{i - 4}" : $"P{i}";
            var token = new Control
            {
                Name = tokenName,
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
            // Small HP readout above the token.
            var hpLabel = new Label
            {
                Text = $"{Roster[i].HitPoints}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(0, -14),
                Size = new Vector2(TileSize, 14),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            hpLabel.Modulate = new Color(0.9f, 0.95f, 1f);
            hpLabel.AddThemeFontSizeOverride("font_size", 12);
            token.AddChild(hpLabel);
            _board.AddChild(token);
            _tokens.Add(token);
            _tokenBodies.Add(body);
            _tokenHpLabels.Add(hpLabel);
        }
    }

    private void RefreshTokenHp(int index)
    {
        if (index >= 0 && index < _tokenHpLabels.Count)
            _tokenHpLabels[index].Text = $"{_turns.Combatants[index].HitPoints}";
    }

    // HP hit 0: remove the token from the map. It no longer blocks movement
    // (see IsOccupiedBy) and its turn is skipped (see TurnSystem.EndTurn).
    private void KillToken(int index)
    {
        _tokens[index].Visible = false;
        RefreshTokenHp(index);
    }

    // --- Actions menu -------------------------------------------------------

    // The rectangular actions menu: pinned to the bottom-left, the active
    // character's name on top, then the SHOOT / MOVE / WAIT buttons.
    private void BuildActionMenu()
    {
        _actionMenu = new PanelContainer
        {
            Name = "ActionMenu",
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 10,
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.08f, 0.11f, 0.92f),
            BorderColor = new Color(0.35f, 0.6f, 0.9f, 0.8f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12,
            ContentMarginTop = 10,
            ContentMarginRight = 12,
            ContentMarginBottom = 10,
        };
        _actionMenu.AddThemeStyleboxOverride("panel", panelStyle);
        var vbox = new VBoxContainer { Name = "VBox" };
        vbox.AddThemeConstantOverride("separation", 6);
        _actionMenuName = new Label
        {
            Name = "Name",
            Text = "…",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _actionMenuName.Modulate = new Color(0.85f, 0.92f, 1f);
        _actionMenuName.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(_actionMenuName);
        // Equipped gear readout (weapon · head · body), under the name.
        _actionMenuEquip = new Label
        {
            Name = "Equipment",
            Text = "…",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _actionMenuEquip.Modulate = new Color(0.65f, 0.75f, 0.9f);
        _actionMenuEquip.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_actionMenuEquip);
        var actions = new HBoxContainer { Name = "Actions" };
        actions.AddThemeConstantOverride("separation", 8);
        _shootButton = new Button { Name = "Shoot", Text = "SHOOT", CustomMinimumSize = new Vector2(84, 34) };
        _moveButton = new Button { Name = "Move", Text = "MOVE", CustomMinimumSize = new Vector2(84, 34) };
        _waitButton = new Button { Name = "Wait", Text = "WAIT", CustomMinimumSize = new Vector2(84, 34) };
        _healButton = new Button { Name = "Heal", CustomMinimumSize = new Vector2(96, 34) };
        _shootButton.Pressed += OnShootModePressed;
        _moveButton.Pressed += OnMoveModePressed;
        _waitButton.Pressed += OnWaitPressed;
        _healButton.Pressed += OnHealPressed;
        actions.AddChild(_shootButton);
        actions.AddChild(_moveButton);
        actions.AddChild(_waitButton);
        actions.AddChild(_healButton);
        vbox.AddChild(actions);
        _actionMenu.AddChild(vbox);
        AddChild(_actionMenu);
    }

    private void UpdateActionMenu()
    {
        if (_actionMenuName == null)
            return;
        var current = _turns.Current;
        _actionMenuName.Text = current.Name;
        _actionMenuEquip.Text = current.Equipment.ToText();
        int stims = current.GetItemCount(Item.BioStim);
        _healButton.Text = $"BIOSTIM x{stims}";
        // One action (shoot/wait/item) and one move per turn; each budget
        // disables its own buttons independently.
        bool enemyTurn = current.IsEnemy;
        _shootButton.Disabled = enemyTurn || _actionUsed;
        _moveButton.Disabled = enemyTurn || _movedThisTurn;
        _waitButton.Disabled = enemyTurn || _actionUsed;
        _healButton.Disabled = enemyTurn || _actionUsed || stims <= 0 || current.HitPoints >= current.MaxHitPoints;
        _shootButton.Modulate = _actionMode == ActionMode.Shoot ? Colors.White : new Color(1, 1, 1, 0.55f);
        _moveButton.Modulate = _actionMode == ActionMode.Move ? Colors.White : new Color(1, 1, 1, 0.55f);
    }

    // Keep the menu on screen at all times: it hugs the bottom-left corner
    // with a margin, clamped for very small viewports (menu is ~300x120, so
    // it always fits the supported 1280x720+ windows).
    private void ClampActionMenu()
    {
        if (_actionMenu == null)
            return;
        const int Margin = 12;
        float x = Margin;
        float y = Margin;
        if (Size.X > 0 && _actionMenu.Size.X > 0 && Margin + _actionMenu.Size.X > Size.X)
            x = Mathf.Max(0, Size.X - _actionMenu.Size.X - Margin);
        if (Size.Y > 0 && _actionMenu.Size.Y > 0)
        {
            // Bottom-left: pin the menu's bottom edge above the screen's
            // bottom edge; clamp to the top if the viewport is too short.
            y = Size.Y - _actionMenu.Size.Y - Margin;
            if (y < Margin)
                y = Margin;
        }
        _actionMenu.Position = new Vector2(x, y);
    }
}

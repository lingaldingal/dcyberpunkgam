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
/// Shooting hits any target inside the weapon's shooting CIRCLE centered on
/// the shooter (dx^2 + dy^2 <= range^2 — the "radius of the weapon's range"
/// rule used by most tactics games, in ANY direction) with a clear line of
/// sight (no obstacle or other character on the segment between them), for
/// the weapon's damage. A shoot may be used before or after the move; a shot
/// always fires from the shooter's CURRENT position. In shoot mode a soft
/// circle of radius = weapon range is drawn around the active combatant so
/// the player can see who is in range. Party members also start with
/// BioStims (heal items).
///
/// Melee: every combatant also carries a melee weapon (knife 3 dmg or katana
/// 5 dmg, rolled like the ranged one). The melee action hits a live token on
/// one of the FOUR directly adjacent squares (left, right, up, down — no
/// diagonals) for the weapon's damage. Melee and shoot are the SAME one
/// action: using one bars the other until END TURN. In melee mode the
/// adjacent cells holding tokens are highlighted orange and a click on one
/// strikes.
///
/// Actions: per turn each combatant has ONE action (SHOOT / MELEE / WAIT /
/// item) and ONE move. Mode switching (MOVE <-> SHOOT <-> MELEE) is free;
/// using either budget latches it until END TURN (UNDO restores the
/// turn-start cell and re-arms everything).
///
/// Enemy AI: enemies follow the same rules as the player — one action
/// (shoot / melee / wait / item) and one move per turn. On its turn an
/// enemy approaches the nearest living party member along the BFS path,
/// STOPPING as soon as it stands inside its weapon's shooting circle (target
/// within range with a clear line of sight) — it moves into shooting range
/// rather than right up to the player. Then it PREFERS the ranged attack: it
/// shoots (from the new position) when that is possible, and falls back to
/// the melee weapon when the target is on a directly adjacent square (or it
/// already stood in shooting range but the line of sight is blocked by a wall
/// or another character). The move and the action each latch the same
/// per-turn budgets a player uses, so the actions menu reflects what the
/// enemy has spent.
///
/// Camera: the board is a plain Control that can be far larger than the window
/// (levels are up to 2000x2000 grids). <see cref="_cameraPos"/> is the top-left
/// pixel of the viewport, clamped so the board stays fully on screen (a board
/// smaller than the viewport is centered, not pinned to an edge); WASD /
/// arrow keys pan it at <see cref="CameraPanSpeed"/> px/s while held, so the
/// player can move the map around on screen.
/// </summary>
public partial class Level01 : Control
{
    private const int GridWidth = 28;
    private const int GridHeight = 16;
    // Tile size in logical board pixels. 64 leaves generous room on each
    // diamond for standing/crouching/shooting character sprites and later
    // environment art, while the 28x16 map still fits a 1280x720 window at
    // the default zoom (diamond 1856x928; the edges are reachable via pan).
    private const int TileSize = 64;
    // WASD pan speed in pixels per SECOND (frame-rate independent; the actual
    // per-frame step is CameraPanSpeed * delta in UpdateCamera). 480 px/s is
    // fast enough to feel responsive on a small map yet controllable — the map
    // is up to 2000x2000 grids, so a much higher rate is needed to traverse it.
    private const float CameraPanSpeed = 480f;

    // Mouse-wheel zoom: the board is scaled uniformly around the viewport
    // center (see ZoomBoard). 1.0 is the default (no zoom). The limits are
    // intentionally small for now — the exact range is to be tuned later — so
    // the player can zoom in a little (1.75x) or out a little (0.9x). The min
    // is 0.9 (not lower) because the map is already large on screen: at 64px
    // tiles the diamond (1856x928) is bigger than the window, so zooming out
    // below ~0.95 would shrink it back to the old small-tile proportions.
    private const float ZoomMin = 0.9f;
    private const float ZoomMax = 1.75f;
    // Per wheel-notch multiplier: zoom in multiplies the scale by this, zoom
    // out divides by it. 1.1 per notch is a gentle, controllable step.
    private const float ZoomStep = 1.1f;

    // --- Isometric projection --------------------------------------------
    // The board is drawn as a 2:1 isometric grid: a logical board pixel
    // (x, y) is shown at the "iso" screen point
    //     iso = ((x - y) / 2, (x + y) / 4)
    // implemented as the skew matrix X=(.5,.25) Y=(-.5,.25). The skewed
    // "Iso" Node2D (a child of the Board Control) carries everything that
    // must be skewed — the diamond floor, highlights, tokens, range circle —
    // while plain Controls (token name labels, HUD, action menu) stay
    // unskewed for readable text. All token positions are kept in ISO board
    // space (see IsoBoard), so every existing "cell -> token.Position" path
    // and test hook keeps working unchanged.
    private Node2D _iso = null!;
    private IsoGrid _grid = null!;

    // Frames an enemy waits before acting, so its action is visible.
    private const int EnemyThinkFrames = 12;

    // Basic gear everyone starts with; the weapon is rolled per combatant
    // (pistol or rifle) — see AssignStartingGear.
    private const string HeadGear = "Ballistic Cap";
    private const string BodyGear = "Weave Vest";
    private const int PartyBioStims = 2;

    public enum ActionMode { Move, Shoot, Melee, Grenade }

    private static readonly Vector2I[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
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

    private readonly List<Node2D> _tokens = new();
    // Token name labels (unskewed Controls, children of the board).
    private readonly List<Label> _tokenLabels = new();
    // The shooting-range circle overlay (shoot mode, party turns only): a
    // Polygon2D in the skewed Iso layer, so it appears as an iso ellipse.
    private Polygon2D _rangeCircle = null!;
    // Move-mode path preview (the BFS route line + destination circle), drawn
    // in the skewed Iso layer while the mouse hovers a reachable tile.
    private PathPreview _pathPreview = null!;
    private readonly System.Random _rng = new();

    private TurnSystem _turns = null!;
    private bool _turnActive;
    private Dictionary<Vector2I, int> _reachable = new();
    private HashSet<Vector2I> _shootTargets = new();
    // Cells the active party member can hit with the melee weapon: the four
    // directly adjacent squares (left, right, up, down) holding a live token.
    private HashSet<Vector2I> _meleeTargets = new();
    // Cells the active party member can throw a grenade to (throw range, any
    // walkable cell within the grenade's ThrowRange of the current cell).
    private HashSet<Vector2I> _grenadeTargets = new();

    // Per-turn budget: ONE action (shoot / wait / item) and ONE move. Mode
    // switching is free; using either budget latches it until END TURN.
    // UNDO (before END TURN) restores the turn-start cell and re-arms both.
    private bool _movedThisTurn;
    private Vector2I _turnStartCell;
    private ActionMode _actionMode = ActionMode.Move;
    private bool _actionUsed;

    // Move-mode path preview: the board cell currently under the mouse (None
    // when the pointer is off the board or the board doesn't cover it).
    private Vector2I? _hoverCell;

    // The actions menu: a rectangular panel pinned to the bottom-left with the
    // active character's name + equipment on top and the Shoot / Move / Wait /
    // BioStim buttons below.
    private PanelContainer _actionMenu = null!;
    private Label _actionMenuName = null!;
    private Label _actionMenuEquip = null!;
    private Button _shootButton = null!;
    private Button _meleeButton = null!;
    private Button _moveButton = null!;
    private Button _waitButton = null!;
    private Button _itemsButton = null!;
    private Button _bioStimButton = null!;
    private Button _grenadeButton = null!;
    private Button _endTurnMenuButton = null!;
    // The ITEMS select list: a panel of one button per usable item the active
    // combatant carries (BioStim / Grenade), shown when ITEMS is pressed.
    private PanelContainer _itemsMenu = null!;
    private readonly List<Button> _itemButtons = new();
    // True while the items select list is open (clicking elsewhere closes it).
    private bool _itemsMenuOpen;

    // Enemy auto-move: armed when an enemy is active; fires after a short delay.
    private bool _enemyMoveArmed;
    private int _enemyMoveFrames;
    // Set once the active enemy has moved, so the turn ends exactly once.
    private bool _enemyActed;

    // Top-left pixel of the viewport within the board (see camera section).
    private Vector2 _cameraPos;
    // Uniform board scale (1.0 = default). Zooming scales the WHOLE board
    // (iso layer + name labels) around the viewport center — see ZoomBoard.
    private float _cameraScale = 1f;
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
        // Board clicks are routed through the LEVEL ROOT, not the Board
        // Control. A Control only receives gui_input for points inside its
        // own local rect, and the Board's rect starts at local (0,0) — but
        // the iso diamond's left vertex sits at board-local x = -H*T/2, so
        // every tile in the down-left region of the board projects to local
        // x < 0 and the Board's hit-test dropped those clicks (Rook could
        // path through a green cell via BFS yet never click it). The level
        // root is full-rect; making it Stop makes it the topmost click-catcher
        // for every in-window point, so its gui_input receives the click and
        // OnLevelGuiInput maps the point back to board-local coordinates via
        // the board's global position. Sibling controls (Background) are
        // click-transparent so the click reaches the root's gui_input.
        MouseFilter = MouseFilterEnum.Stop;
        GetNode<Control>("Background").MouseFilter = MouseFilterEnum.Ignore;
        _board = GetNode<Control>("Board");
        _board.MouseFilter = MouseFilterEnum.Ignore;
        // The level root catches real clicks (its full-rect hit area covers the
        // diamond's negative-local region the Board's rect misses) and maps
        // them to board-local before calling OnBoardGuiInput. The Board's own
        // gui_input stays subscribed too: tests emit board-local clicks there
        // directly, and in the real game the Board (MouseFilter.Ignore) never
        // receives a click, so there is no double-handling.
        GuiInput += OnLevelGuiInput;
        _board.GuiInput += OnBoardGuiInput;
        // Hover tracking is cleared once the pointer leaves the window.
        // (Mouse motion itself arrives as GuiInput — see OnLevelGuiInput.)
        MouseExited += OnBoardMouseExited;
        foreach (var name in new[]
        {
            "Center", "Center/Panel",
            "Center/Panel/HUD", "Center/Panel/HUD/Turn", "Center/Panel/HUD/Info",
        })
            GetNode<Control>(name).MouseFilter = MouseFilterEnum.Ignore;
        // The top-right debug panel is a plain VBox on the board; give it a
        // translucent background so the text stays readable over any tile.
        var hudPanel = GetNode<Control>("Center/Panel");
        hudPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.09f, 0.65f),
            BorderColor = new Color(0.35f, 0.6f, 0.9f, 0.35f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8,
            ContentMarginTop = 6,
            ContentMarginRight = 8,
            ContentMarginBottom = 6,
        });
        AssignStartingGear();
        // BuildBoard reads each combatant's HP for the grid's HP readouts, so
        // the turn system (which owns the live combatant list) must exist first.
        _turns = new TurnSystem(Roster.ToList());
        _turns.StartEncounter();
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

        SetActive();
    }

    public override void _Process(double delta)
    {
        // First frame after layout: center the camera on the board (the
        // viewport Size is only valid once the tree has been laid out; in
        // headless test mode it is (0,0) and the camera stays at the origin).
        if (!_cameraCentered)
        {
            _cameraCentered = true;
            if (Size.X > 0 && Size.Y > 0)
                CenterCamera();
        }

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
        UpdateTokenLabels();
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
    //
    // The ISO board's bounding box in BOARD SPACE. The 2:1 skew maps the
    // logical (W x H) board to a diamond whose bounding box is
    //   x: [-H*T/2,  W*T/2]   (width  (W+H)*T/2)
    //   y: [ 0,      (W+H)*T/4]   (height (W+H)*T/4)
    // NOTE the negative left edge: cell (0,H) projects to board x = -H*T/2.
    // The camera clamps to THIS box (not a [0, size] box), otherwise the
    // left third of the diamond is pinned off-screen.
    private Rect2 _isoBounds;

    // Clamp the camera so the board stays on screen and apply it. The board
    // is a plain Control, so it is shown by offsetting it by -camera (the
    // board's own children keep their board-local coordinates; only the board
    // moves). The camera is the board pixel at the viewport's top-left.
    //
    // For each axis the two legal extremes are, for the CURRENT zoom:
    //   camMax = bounds.Min        (board's left/top edge at the screen edge)
    //   camMin = bounds.Max - view (board's right/bottom edge at the screen edge)
    // where view = window / current scale — the board-space span the viewport
    // covers right now. When the board is LARGER than that (camMin <= camMax)
    // the valid range is [camMin, camMax] and the board clamps at its edges;
    // the full board is therefore always reachable at every zoom level. When
    // the board is SMALLER (camMin > camMax) the range is [camMax, camMin]:
    // the board may slide anywhere while staying fully on-screen, and the
    // MIDPOINT ((bounds.Center) - view/2) is exactly where CenterCamera puts
    // it — i.e. the diamond is centered in the window at every resolution.
    //
    // This interval (not a single point) is what makes WASD panning visible on
    // a small board: the old clamp collapsed to one bottom-right-pinned point
    // when the board was smaller than the viewport, so the camera snapped back
    // every frame (no panning) and the diamond sat flush against the right and
    // bottom edges instead of centered.
    private void ApplyCamera()
    {
        if (_board == null)
            return;

        // The board is scaled by _cameraScale (mouse-wheel zoom), so the
        // viewport covers FEWER board pixels when zoomed in. The clamp view
        // must therefore be the window divided by the CURRENT scale: at the
        // far bottom-right, the board's right/bottom edge is pinned to the
        // screen edge, and since the visible span is smaller when zoomed in,
        // the camera can go further into the board — i.e. every square stays
        // reachable at any zoom level. (Clamping to a constant, wider view
        // left a dead zone at the map's far edges proportional to scale-1:
        // at max zoom (1.75) the bottom-right ~30% of the board was
        // unpannable.)
        float scale = _cameraScale > 0 ? _cameraScale : 1f;
        float viewW = (Size.X > 0 ? Size.X : 1) / scale;
        float viewH = (Size.Y > 0 ? Size.Y : 1) / scale;

        float loX = _isoBounds.End.X - viewW;   // board's right edge at screen right
        float hiX = _isoBounds.Position.X;      // board's left edge at screen left
        float loY = _isoBounds.End.Y - viewH;   // board's bottom edge at screen bottom
        float hiY = _isoBounds.Position.Y;      // board's top edge at screen top

        _cameraPos = new Vector2(
            Mathf.Clamp(_cameraPos.X, Mathf.Min(loX, hiX), Mathf.Max(loX, hiX)),
            Mathf.Clamp(_cameraPos.Y, Mathf.Min(loY, hiY), Mathf.Max(loY, hiY)));

        _board.Position = -_cameraPos;
        _board.Scale = new Vector2(scale, scale);
    }

    // The ISO diamond's true on-screen bounding box in board space:
    //   x: [-H*T/2, (W-H)*T/2 + H*T] = [-H*T/2, W*T/2]
    //   y: [0, (W+H)*T/4]
    // (see the _isoBounds comment for why the left edge is negative).
    private static Rect2 IsoBounds()
    {
        return new Rect2(
            -GridHeight * TileSize / 2f, 0f,
            (GridWidth + GridHeight) * TileSize / 2f,
            (GridWidth + GridHeight) * TileSize / 4f);
    }

    private void InitializeCamera()
    {
        _isoBounds = IsoBounds();
        // The viewport Size is not laid out until after _Ready, so the actual
        // centering is deferred to the first _Process frame (see _Process).
        // (Clicks are handled at the level root — see OnLevelGuiInput — so
        // the Board's own rect no longer matters for the hit-test.)
        ApplyCamera();
    }

    /// <summary>
    /// Test hook: restore the real ISO diamond bounding box (undo any
    /// <see cref="SetBoardPixelSize"/> override) and re-apply the camera.
    /// </summary>
    public void ResetBoardBounds()
    {
        _isoBounds = IsoBounds();
        ApplyCamera();
    }

    // The viewport Size is not laid out until after _Ready, so the actual
    // centering is deferred to the first _Process frame (see _Process).
    private bool _cameraCentered;

    private void CenterCamera()
    {
        _cameraCentered = true;
        // "Reset the view" = centered on the diamond AND no zoom, so a zoomed
        // camera is restored to the default scale along with its position.
        _cameraScale = 1f;
        // Start centered on the ISO diamond: the diamond's center is the
        // board-space point ((W-H)*T/4, (W+H)*T/8); the camera is the board
        // pixel at the viewport's top-left, i.e. that center minus the
        // viewport's half-size. Clamped for boards smaller than the viewport.
        var center = new Vector2(
            (GridWidth - GridHeight) * TileSize / 4f,
            (GridWidth + GridHeight) * TileSize / 8f);
        _cameraPos = center - new Vector2(Size.X / 2f, Size.Y / 2f);
        ApplyCamera();
    }

    /// <summary>Test hook: center the camera on the board again.</summary>
    public void ResetCamera()
    {
        CenterCamera();
    }

    // --- Iso coordinate helpers ------------------------------------------
    // Tokens, the floor grid and the range circle all live in LOGICAL board
    // space as children of the skewed "Iso" Node2D, so the 2:1 iso projection
    // is applied to their rendering automatically. The inverse helpers below
    // convert a board-local (GUI / ISO) point back to a grid cell, and map a
    // logical point to its on-screen (board-space) position for the unskewed
    // name labels.

    // A logical board point to its board-space (on-screen, pre-camera) point.
    public Vector2 IsoBoard(Vector2 logical)
        => new((logical.X - logical.Y) / 2f, (logical.X + logical.Y) / 4f);

    public Vector2 IsoCell(Vector2I cell)
        => IsoBoard(new Vector2(cell.X * TileSize, cell.Y * TileSize));

    public Vector2 IsoCellCenter(Vector2I cell)
        => IsoBoard(new Vector2((cell.X + 0.5f) * TileSize, (cell.Y + 0.5f) * TileSize));

    // The inverse projection: a board-local (ISO) point back to a grid cell.
    public Vector2I CellFromIso(Vector2 iso)
    {
        float gx = (iso.X + 2f * iso.Y) / TileSize;
        float gy = (-iso.X + 2f * iso.Y) / TileSize;
        return new Vector2I((int)Mathf.Floor(gx), (int)Mathf.Floor(gy));
    }

    // The grid cell a token currently occupies (tokens are stored in logical
    // board space, so this is a straight division).
    private Vector2I TokenCell(int index)
        => new Vector2I((int)(_tokens[index].Position.X / TileSize),
                        (int)(_tokens[index].Position.Y / TileSize));

    /// <summary>Test hook: treat the board's on-screen extent as w x h.</summary>
    public void SetBoardPixelSize(float w, float h)
    {
        // Rebuild the bounding box as a plain [0, w] x [0, h] area so the
        // pan/clamp test can exercise edge clamping with the familiar
        // (0,0) origin. (The real board uses the ISO diamond's true bounds,
        // which have a negative left edge — see InitializeCamera.)
        _isoBounds = new Rect2(0f, 0f, w, h);
        ApplyCamera();
    }

    // Root-level click entry point: the Board Control's own rect does not
    // cover the iso diamond's negative-local region, so clicks are caught
    // here (full-rect root, mouse_filter Stop) and converted to board-local
    // coordinates before being handed to OnBoardGuiInput. Direct
    // board.gui_input emissions from tests already arrive board-local and
    // bypass this transform.
    private void OnLevelGuiInput(InputEvent @event)
    {
        if (!(@event is InputEventMouse me) || _board == null)
            return;
        // Mouse-wheel zoom is handled here (window-local) and works anywhere
        // in the window — including over the action menu — so it is checked
        // before the menu-overlap guard below.
        if (me is InputEventMouseButton wheel && wheel.Pressed
            && (wheel.ButtonIndex == MouseButton.WheelUp
                || wheel.ButtonIndex == MouseButton.WheelDown))
        {
            ZoomBoard(wheel.ButtonIndex == MouseButton.WheelUp ? 1f : -1f);
            return;
        }
        // The board is scaled by _cameraScale (mouse-wheel zoom), so a
        // window-local point maps to board-local by subtracting the board's
        // on-screen origin AND dividing by the scale (the board's children
        // live in pre-scale board space). Without the divide, clicks land one
        // tile off once zoomed (see the zoom test / _zoom_probe).
        float scale = _cameraScale > 0 ? _cameraScale : 1f;
        var local = (me.Position - _board.GetGlobalPosition()) / scale;
        // A click landing on the action menu (or its items select list) is for
        // its buttons, not the board (the menu overlaps the diamond's
        // bottom-left corner). A click anywhere else closes the items list.
        if (_actionMenu != null && _actionMenu.GetRect().HasPoint(me.Position))
            return;
        if (_itemsMenu != null && _itemsMenu.GetRect().HasPoint(me.Position))
            return;
        CloseItemsMenu();
        me.Position = local;
        OnBoardGuiInput(me);
    }

    // Zoom the board in (dir > 0) or out (dir < 0) by one step, anchored on the
    // VIEWPORT CENTER so the point under the screen center stays put while the
    // scale changes (the standard, stable zoom-to-point behavior). The board
    // is scaled uniformly around its (0,0) origin, so after scaling the board's
    // on-screen center shifts by (scale - 1) * center; nudging the camera by
    // the same amount keeps that point fixed. The camera is then clamped to the
    // new (scaled) view, so panning stays valid at every zoom level.
    private void ZoomBoard(float dir)
    {
        if (_board == null || Size.X <= 0 || Size.Y <= 0)
            return;
        float old = _cameraScale;
        float next = Mathf.Clamp(old * Mathf.Pow(ZoomStep, dir), ZoomMin, ZoomMax);
        if (Mathf.Abs(next - old) < 1e-5f)
            return;
        var center = new Vector2(Size.X / 2f, Size.Y / 2f);
        _cameraPos += (next - old) * center;
        _cameraScale = next;
        ApplyCamera();
    }

    private void OnBoardGuiInput(InputEvent @event)
    {
        // Motion is tracked on every turn (party or enemy) so the hover cell
        // is fresh when the next turn's RefreshPathPreview runs; only CLICKS
        // are gated to party turns (one action + one move, each latched).
        if (@event is InputEventMouseMotion motion)
        {
            OnBoardMouseMoved(motion.Position);
            return;
        }
        if (!_turnActive || _turns.Current.IsEnemy)
            return;
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            TryBoardClick(mouse.Position);
    }

    // --- Move-mode path preview -------------------------------------------
    // While the mouse hovers a tile the active combatant can move to (move
    // mode, party's turn, move budget unspent), a line traces the exact BFS
    // route the token will take, ending in a circle on the destination tile.
    // Both live in the skewed Iso layer (logical coordinates) and are shown
    // ONLY in move mode — hovering in shoot mode, on an enemy turn or after
    // the move is spent shows nothing.

    private void OnBoardMouseMoved(Vector2 boardPos)
    {
        var cell = CellFromIso(boardPos);
        if (cell.X < 0 || cell.X >= GridWidth || cell.Y < 0 || cell.Y >= GridHeight)
            cell = Vector2I.MinValue; // off-board sentinel (a real cell can't be here)
        if (cell == _hoverCell)
            return;
        _hoverCell = cell;
        RefreshPathPreview();
    }

    private void OnBoardMouseExited()
    {
        _hoverCell = null;
        RefreshPathPreview();
    }

    // (Re)build the hover path overlay from the current state. Called on every
    // mouse move, on mode switch / move / turn change (via RefreshHighlights),
    // and after UNDO.
    private void RefreshPathPreview()
    {
        bool show = _hoverCell != null
            && _actionMode == ActionMode.Move
            && _turnActive
            && !_movedThisTurn
            && !_turns.Combatants[_turns.CurrentIndex].IsEnemy
            && _reachable.ContainsKey(_hoverCell.Value)
            && _hoverCell.Value != CurrentCell();
        if (!show)
        {
            _pathPreview.SetPath(new List<Vector2>());
            return;
        }
        var path = BuildPath(CurrentCell(), _hoverCell.Value);
        var pts = new List<Vector2>(path.Count);
        foreach (var c in path)
            pts.Add(new Vector2((c.X + 0.5f) * TileSize, (c.Y + 0.5f) * TileSize));
        _pathPreview.SetPath(pts);
    }

    // The BFS route from <start> to <goal> (both in the reachable set): walk
    // the distance map back from the goal, one step per tile. The result is
    // the same route the click-to-move would take.
    private List<Vector2I> BuildPath(Vector2I start, Vector2I goal)
    {
        var path = new List<Vector2I>();
        var cur = goal;
        // Bound the walk by the map size: the distance map has no cycles.
        for (int guard = 0; guard <= GridWidth * GridHeight && cur != start; guard++)
        {
            path.Add(cur);
            int d = _reachable[cur];
            Vector2I next = cur;
            foreach (var dir in Dirs)
            {
                var cand = cur + dir;
                if (_reachable.TryGetValue(cand, out int cd) && cd == d - 1)
                {
                    next = cand;
                    break;
                }
            }
            if (next == cur)
                break;
            cur = next;
        }
        path.Add(start); // the route begins at the character's cell
        path.Reverse();
        return path;
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
        // MoveTokenTo's RefreshHighlights ran while _movedThisTurn was still
        // latched; the move is re-armed now, so refresh the hover path.
        RefreshPathPreview();
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
        return index < 0 ? default : TokenCell(index);
    }

    /// <summary>Test hook: the board pixel the viewport's top-left is at.</summary>
    public Vector2 GetCameraPos() => _cameraPos;

    /// <summary>Test hook: pan the camera by a pixel offset (clamped).</summary>
    public void PanCameraBy(Vector2 offset)
    {
        _cameraPos += offset;
        ApplyCamera();
    }

    /// <summary>Test hook: the current board scale (1.0 = no zoom).</summary>
    public float GetCameraScale() => _cameraScale;

    /// <summary>
    /// Test hook: zoom in (dir &gt; 0) or out (dir &lt; 0) by one step, exactly
    /// as a mouse-wheel notch would — so the zoom path can be exercised
    /// without a real wheel event.
    /// </summary>
    public void ZoomBy(float dir) => ZoomBoard(dir);

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

    /// <summary>Whether cell (x, y) is a valid melee target for the active combatant.</summary>
    public bool IsMeleeTarget(int x, int y) => _meleeTargets.Contains(new Vector2I(x, y));

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

    /// <summary>
    /// Test hook: the shooting-range overlay as [x, y, width, height] — the
    /// board-space bounding box of the iso range diamond — when visible, or
    /// [-1, -1, -1, -1] when hidden. The overlay is a logical diamond (a
    /// 45-degree square with corners at distance r = range*TILE on the logical
    /// axes); under the iso skew that projects to a diamond on screen whose
    /// bounding box is a square of side 2r centered on the cell's board-space
    /// (iso) center.
    /// </summary>
    public float[] GetRangeCircle()
    {
        if (_rangeCircle == null || !_rangeCircle.Visible)
            return new[] { -1f, -1f, -1f, -1f };
        int radius = _turns.Combatants[_turns.CurrentIndex].Equipment.Weapon.Range * TileSize;
        var c = IsoCellCenter(CurrentCell());    // board-space center
        return new[] { c.X - radius, c.Y - radius, radius * 2f, radius * 2f };
    }

    /// <summary>Test hook: the active combatant's board-click mode (0 = Move, 1 = Shoot, 2 = Melee).</summary>
    public int GetActionMode() => (int)_actionMode;

    /// <summary>Test hook: simulate the mouse hovering cell (x, y) on the board.</summary>
    public void SetHoverCell(int x, int y) => OnBoardMouseMoved(IsoCellCenter(new Vector2I(x, y)));

    /// <summary>Test hook: simulate the mouse leaving the board.</summary>
    public void ClearHoverCell() => OnBoardMouseExited();

    /// <summary>Test hook: the hovered path's points (logical cell centers).</summary>
    public Vector2[] GetHoverPathPoints()
        => _pathPreview == null ? new Vector2[0] : _pathPreview.Points.ToArray();

    /// <summary>
    /// Test hook: the move-mode path preview state —
    /// [lineVisible, endVisible, endX, endY].
    /// </summary>
    public float[] GetPathPreview()
    {
        if (_pathPreview == null || !_pathPreview.Active)
            return new[] { 0f, 0f, -1f, -1f };
        var pos = _pathPreview.EndPoint; // logical cell center
        return new[]
        {
            1f, 1f,
            pos.X / TileSize - 0.5f,
            pos.Y / TileSize - 0.5f,
        };
    }

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

    // Give every combatant basic head/body gear, a randomly rolled ranged
    // weapon (50/50 pistol or rifle) AND a randomly rolled melee weapon
    // (50/50 knife 3 dmg or katana 5 dmg). Every combatant also starts with
    // one grenade; party members additionally start with BioStims.
    private void AssignStartingGear()
    {
        for (int i = 0; i < Roster.Length; i++)
        {
            var c = Roster[i];
            var weapon = _rng.Next(2) == 0 ? Weapon.Pistol : Weapon.Rifle;
            var melee = _rng.Next(2) == 0 ? Weapon.Katana : Weapon.Knife;
            c.Equipment = new Equipment(HeadGear, BodyGear, weapon, melee);
            c.Items[Item.Grenade] = 1;
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

    /// <summary>The named combatant's equipped melee weapon name ("" if unknown).</summary>
    public string GetMeleeWeaponName(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? "" : Roster[index].Equipment.MeleeWeapon.Name;
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

    /// <summary>The named combatant's melee weapon damage per hit.</summary>
    public int GetMeleeWeaponDamage(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? 0 : Roster[index].Equipment.MeleeWeapon.Damage;
    }

    /// <summary>Test hook: swap a combatant's weapon (recomputes if they are active).</summary>
    public void SetWeapon(string name, bool rifle)
    {
        int index = IndexOf(name);
        if (index < 0)
            return;
        var c = Roster[index];
        c.Equipment = new Equipment(HeadGear, BodyGear, rifle ? Weapon.Rifle : Weapon.Pistol, c.Equipment.MeleeWeapon);
        if (index == _turns.CurrentIndex)
        {
            ComputeShootTargets();
            ComputeMeleeTargets();
            RefreshHighlights();
        }
        UpdateActionMenu();
    }

    /// <summary>Test hook: swap a combatant's melee weapon (recomputes if they are active).</summary>
    public void SetMeleeWeapon(string name, bool katana)
    {
        int index = IndexOf(name);
        if (index < 0)
            return;
        var c = Roster[index];
        c.Equipment = new Equipment(c.Equipment.Head, c.Equipment.Body, c.Equipment.Weapon, katana ? Weapon.Katana : Weapon.Knife);
        if (index == _turns.CurrentIndex)
        {
            ComputeMeleeTargets();
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
    /// Test hook: how many of the named item ("BioStim" / "Grenade") the
    /// named combatant is carrying.
    /// </summary>
    public int GetItemCount(string name, string itemName)
    {
        int index = IndexOf(name);
        if (index < 0)
            return 0;
        var item = ResolveItem(itemName);
        return item == null ? 0 : Roster[index].GetItemCount(item);
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

    /// <summary>Test hook: is the named cell a valid grenade throw target?</summary>
    public bool IsGrenadeTarget(int x, int y) => _grenadeTargets.Contains(new Vector2I(x, y));

    /// <summary>
    /// Test hook: detonate the active combatant's grenade on cell (x, y)
    /// (must be a valid throw target). Returns false if the throw was
    /// invalid or the action was already spent.
    /// </summary>
    public bool UseGrenade(int x, int y)
    {
        if (_actionUsed || !_turnActive || _turns.Current.IsEnemy)
            return false;
        if (!_grenadeTargets.Contains(new Vector2I(x, y)))
            return false;
        ApplyGrenade(_turns.CurrentIndex, new Vector2I(x, y));
        MarkActionUsed();
        return true;
    }

    /// <summary>Test hook: whether the items select list is currently open.</summary>
    public bool GetItemsMenuOpen() => _itemsMenuOpen;

    /// <summary>Test hook: open or close the items select list directly.</summary>
    public void SetItemsMenuOpen(bool open)
    {
        _itemsMenuOpen = open;
        if (_itemsMenu != null)
            _itemsMenu.Visible = open;
    }

    /// <summary>Test hook: force the active combatant's grenade throw mode.</summary>
    public void ForceGrenadeMode() => OnGrenadePressed();

    private static Item? ResolveItem(string itemName) => itemName switch
    {
        "BioStim" => Item.BioStim,
        "Grenade" => Item.Grenade,
        _ => null,
    };

    // --- Items (BioStim / Grenade) -----------------------------------------
    // The active party member's one action can be spent on an item. The ITEMS
    // button opens a select list (one entry per usable item); picking BioStim
    // heals, picking Grenade enters grenade-throw mode (aim, then click a
    // target cell). Both cost the action for the turn (like shoot/melee/wait).

    // Toggle the items select list open/closed.
    private void OnItemsPressed()
    {
        if (!_turnActive || _turns.Current.IsEnemy)
            return;
        _itemsMenuOpen = !_itemsMenuOpen;
        _itemsMenu.Visible = _itemsMenuOpen;
        UpdateActionMenu();
    }

    // Close the items select list (called when the pointer clicks elsewhere).
    private void CloseItemsMenu()
    {
        if (!_itemsMenuOpen)
            return;
        _itemsMenuOpen = false;
        _itemsMenu.Visible = false;
        UpdateActionMenu();
    }

    // The active party member spends its action on a BioStim (if it has one
    // and is below full HP).
    private void OnBioStimPressed()
    {
        if (!_turnActive || _turns.Current.IsEnemy || _actionUsed)
            return;
        var c = _turns.Current;
        if (c.UseItem(Item.BioStim))
        {
            _infoLabel.Text = $"{c.Name} uses a BioStim and recovers {Item.BioStim.HealValue} HP ({c.HitPoints} HP)";
            RefreshTokenHp(_turns.CurrentIndex);
            MarkActionUsed();
            CloseItemsMenu();
        }
    }

    // The active party member spends its action throwing a grenade: it enters
    // grenade mode (yellow throw-target highlights), then a board click on a
    // highlighted cell detonates it there.
    private void OnGrenadePressed()
    {
        if (!_turnActive || _turns.Current.IsEnemy || _actionUsed)
            return;
        if (_turns.Current.GetItemCount(Item.Grenade) <= 0)
            return;
        CloseItemsMenu();
        SetActionMode(ActionMode.Grenade);
    }

    // A board click in grenade mode: detonate the grenade on the clicked cell
    // (must be a valid throw target).
    private void TryThrowGrenade(Vector2I cell)
    {
        if (_actionUsed || !_grenadeTargets.Contains(cell))
            return;
        ApplyGrenade(_turns.CurrentIndex, cell);
        MarkActionUsed();
    }

    // Common grenade application: the thrower consumes a grenade, then every
    // live combatant standing on the blast cells (the landing cell + its four
    // orthogonal neighbours) takes the grenade's blast damage. Shared by the
    // player's throw action and the enemy AI.
    private void ApplyGrenade(int throwerIndex, Vector2I cell)
    {
        var thrower = _turns.Combatants[throwerIndex];
        if (!thrower.ConsumeItem(Item.Grenade))
            return;
        var blast = new List<Vector2I> { cell };
        foreach (var dir in Dirs)
            blast.Add(cell + dir);
        var hits = new List<string>();
        for (int i = 0; i < _tokens.Count; i++)
        {
            var victim = _turns.Combatants[i];
            if (victim.IsDead)
                continue;
            if (!blast.Contains(TokenCell(i)))
                continue;
            victim.HitPoints -= Item.Grenade.BlastDamage;
            hits.Add(victim.Name);
            RefreshTokenHp(i);
            if (victim.IsDead)
                KillToken(i);
        }
        _infoLabel.Text = hits.Count == 0
            ? $"{thrower.Name} throws a Grenade; it hits nothing."
            : $"{thrower.Name} throws a Grenade on {cell.X},{cell.Y} for {Item.Grenade.BlastDamage} dmg — {string.Join(", ", hits)}";
    }

    // Rebuild the grenade throw-target set for the active combatant from its
    // CURRENT position: every walkable cell within the grenade's ThrowRange
    // (Chebyshev distance, so it's a square of side 2*ThrowRange+1 centered on
    // the thrower — "throw up to N squares away in any direction").
    private void ComputeGrenadeTargets()
    {
        _grenadeTargets.Clear();
        if (_turns.Combatants[_turns.CurrentIndex].IsEnemy)
            return;
        int range = Item.Grenade.ThrowRange;
        var from = CurrentCell();
        for (int dy = -range; dy <= range; dy++)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                var cell = new Vector2I(from.X + dx, from.Y + dy);
                if (cell.X < 0 || cell.X >= GridWidth || cell.Y < 0 || cell.Y >= GridHeight)
                    continue;
                if (Walkable[cell.Y, cell.X] != 1)
                    continue;
                _grenadeTargets.Add(cell);
            }
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
        // A new turn always starts in MOVE mode, even if the previous
        // combatant ended their turn in shoot mode (or used WAIT there).
        _actionMode = ActionMode.Move;
        _turnLabel.Text = $"ROUND {_turns.Round} — TURN: {_turns.Current.Name}";
        _infoLabel.Text = BuildInitiativeInfo();
        // Active token glows full-bright, the rest dim (modulate also covers
        // the token's name label).
        for (int i = 0; i < _tokens.Count; i++)
            _tokens[i].Modulate = i == index ? Colors.White : new Color(1, 1, 1, 0.55f);
        _reachable = ComputeDistances(CurrentCell(), _turns.Current.MovementRange, index);
        ComputeShootTargets();
        ComputeMeleeTargets();
        ComputeGrenadeTargets();
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
            + "  —  initiative d5 + Agility sets order (high first) · move = 5 + Agi/2 · per turn: ONE action (SHOOT / WAIT / BioStim) + ONE move · shoot = weapon's range CIRCLE (pistol 4 / rifle 6), no shooting through walls or characters, weapon dmg (2 / 3) · HP = 5 + Con · WASD pans the map · END TURN";
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

    private void TryBoardClick(Vector2 boardPos)
    {
        // Board clicks arrive in ISO board space (the board is a Control, so
        // GuiInput coordinates are board-local). A single inverse projection
        // maps them back to a grid cell — no snapping to logical coordinates
        // (that hijacked clicks near logical cell centers to the wrong iso
        // cell, which broke "move around the obstacle" clicks). Tests emit
        // clicks at IsoCellCenter, which this projects back exactly.
        var cell = CellFromIso(boardPos);
        if (_actionMode == ActionMode.Shoot)
            TryShootAt(cell);
        else if (_actionMode == ActionMode.Melee)
            TryMeleeAt(cell);
        else if (_actionMode == ActionMode.Grenade)
            TryThrowGrenade(cell);
        else
            MoveTo(cell);
    }

    // Shoot: the target must sit inside the active combatant's weapon's
    // shooting CIRCLE (dx^2+dy^2 <= range^2, any direction) with a clear line
    // of sight — from the combatant's CURRENT position, whether or not it has
    // moved this turn. Deals the weapon's damage.
    private void TryShootAt(Vector2I cell)
    {
        if (_actionUsed || !_shootTargets.Contains(cell))
            return;
        int targetIndex = -1;
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (TokenCell(i) == cell)
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

    // Melee: the target must stand on one of the FOUR directly adjacent
    // squares (left, right, up, down — no diagonals) of the active
    // combatant's CURRENT position and be alive. Deals the melee weapon's
    // damage. Like a shot, it fires from wherever the combatant now stands.
    private void TryMeleeAt(Vector2I cell)
    {
        if (_actionUsed || !_meleeTargets.Contains(cell))
            return;
        int targetIndex = -1;
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (TokenCell(i) == cell)
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
        ApplyMelee(_turns.CurrentIndex, targetIndex);
        MarkActionUsed();
    }

    // Common melee application: melee-weapon damage on the target, HUD line,
    // token HP update, death removal. Shared by the player's melee action and
    // the enemy AI.
    private void ApplyMelee(int attackerIndex, int targetIndex)
    {
        var attacker = _turns.Combatants[attackerIndex];
        var target = _turns.Combatants[targetIndex];
        int damage = attacker.Equipment.MeleeWeapon.Damage;
        target.HitPoints -= damage;
        _infoLabel.Text = $"{attacker.Name} strikes {target.Name} with the {attacker.Equipment.MeleeWeapon.Name} for {damage} ({target.HitPoints} HP left)";
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
    private void OnMeleeModePressed() => SetActionMode(ActionMode.Melee);
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
        // A move changes where shots/strikes are fired from: refresh the
        // shoot-, melee-, and grenade-target sets from the new position so a
        // shoot/melee/grenade-after-move aims from here.
        if (index == _turns.CurrentIndex)
        {
            ComputeShootTargets();
            ComputeMeleeTargets();
            ComputeGrenadeTargets();
        }
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
        // A move changes where shots/strikes are fired from: refresh the
        // shoot-, melee-, and grenade-target sets from the new position so a
        // shoot/melee/grenade-after-move aims from here.
        ComputeShootTargets();
        ComputeMeleeTargets();
        ComputeGrenadeTargets();
        RefreshHighlights();
    }

    /// <summary>Test hook: act for the active enemy now (AI move + shoot + end turn).</summary>
    public void ForceEnemyMove() => EnemyAct();

    // Enemy AI (same rules as the player: one action + one move per turn):
    // the enemy spends its full movement range walking the BFS path toward
    // the nearest living party member, stopping inside its weapon's shooting
    // circle, then PREFERS the ranged attack: it shoots the target — from
    // the new position — when that is possible, and falls back to the melee
    // weapon when the target is on a directly adjacent square (e.g. the
    // enemy already stood in shooting range but a wall blocks the line of
    // sight). The move and the action each latch the same per-turn budgets a
    // player's actions use (_movedThisTurn / _actionUsed), so the actions
    // menu shows exactly what the enemy has spent. Then its turn ends
    // automatically. Guarded so it acts exactly once per turn.
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
            int d = DistanceToCell(dist, TokenCell(i));
            if (d < best)
            {
                best = d;
                targetIndex = i;
            }
        }
        return targetIndex;
    }

    // The enemy's one move for the turn: it approaches <targetIndex> along
    // the BFS path but STOPS as soon as it stands inside the target's shooting
    // circle (target within the weapon's range with a clear line of sight), so
    // it moves INTO shooting range instead of right up to the party member.
    // If it can already shoot, it doesn't move at all. It also stops when
    // adjacent (the target's token is a BFS "wall") or boxed in by obstacles.
    // Latches the move budget exactly like a player's move.
    private void EnemyMoveToward(int index, int targetIndex)
    {
        var enemy = _turns.Combatants[index];
        var targetCell = TokenCell(targetIndex);
        int range = enemy.Equipment.Weapon.Range;
        if (CanShootAt(CurrentCell(), targetCell, range))
            return; // already in shooting range: hold position, let the shot land
        int steps = enemy.MovementRange;
        var pos = CurrentCell();
        while (steps-- > 0)
        {
            // BFS from the target treating the ENEMY as the moving token, so
            // the enemy's own cell is in the map (its distance = how far it
            // still is). The target's token is a wall, so once adjacent no
            // neighbour can get any closer.
            var toward = ComputeDistances(targetCell, int.MaxValue, index);
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
            // A single step cannot jump OUT of the shooting circle (the max
            // in-circle distance is ~range*1.414, so one tile keeps the enemy
            // inside), so the first in-circle step is a valid stopping point.
            if (CanShootAt(pos, targetCell, range))
                break;
        }
        if (pos != CurrentCell())
        {
            MoveTokenTo(index, pos);
            _movedThisTurn = true;
            UpdateActionMenu();
        }
    }

    // Can a shooter standing on <from> hit <cell> with a weapon of <range>?
    // <cell> must sit inside the weapon's shooting circle (dx^2 + dy^2 <=
    // range^2) with a clear line of sight (no obstacle or other token on the
    // segment). Used by the enemy AI to decide where to stop and what to fire.
    private bool CanShootAt(Vector2I from, Vector2I cell, int range)
    {
        return IsShootCell(from, cell, range) && HasLineOfSight(from, cell, range);
    }

    // The enemy's action: it PREFERS to shoot — if <targetIndex> is inside
    // its weapon's shooting circle from the enemy's CURRENT position with a
    // clear line of sight, shoot it (weapon damage). When the shot is not
    // possible but the target stands on one of the four directly adjacent
    // squares, it falls back to the melee weapon (melee damage). Latches the
    // action budget exactly like a player's shoot or melee.
    private void EnemyShoot(int index, int targetIndex)
    {
        var enemy = _turns.Combatants[index];
        int weaponRange = enemy.Equipment.Weapon.Range;
        var from = CurrentCell();
        var cell = TokenCell(targetIndex);
        if (CanShootAt(from, cell, weaponRange))
        {
            ApplyShot(index, targetIndex);
            _actionUsed = true;
            UpdateActionMenu();
            return;
        }
        // Melee fallback: the target is on a directly adjacent square (the
        // enemy stood in its shooting circle already, so a shot means the
        // line of sight is blocked — a knife/katana can still swing).
        int dx = cell.X - from.X;
        int dy = cell.Y - from.Y;
        if (Math.Abs(dx) + Math.Abs(dy) == 1)
        {
            ApplyMelee(index, targetIndex);
            _actionUsed = true;
            UpdateActionMenu();
        }
    }

    private Vector2I CurrentCell()
    {
        return TokenCell(_turns.CurrentIndex);
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
            if (TokenCell(i) == cell)
                return true;
        }
        return false;
    }

    // <to> is a shoot position for a weapon of <range> iff it sits inside the
    // weapon's shooting CIRCLE centered on <from>: dx^2 + dy^2 <= range^2
    // (any direction, any distance 1..range — the classic "radius of the
    // weapon's range" rule used by most tactics games).
    private static bool IsShootCell(Vector2I from, Vector2I to, int range)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        return (dx != 0 || dy != 0) && dx * dx + dy * dy <= range * range;
    }

    // Clear line of sight from <from> to <to> (a cell within the weapon's
    // shooting circle): every INTERMEDIATE cell on the straight segment
    // between them must be walkable and hold no live token — so a shot cannot
    // pass through obstacles or through other characters. The segment is
    // walked one cell at a time with an integer Bresenham (horizontal,
    // vertical, diagonal and sub-diagonal slopes alike); the target's own
    // cell is the endpoint and not an obstacle.
    private bool HasLineOfSight(Vector2I from, Vector2I to, int range)
    {
        if (!IsShootCell(from, to, range))
            return false;
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        int ax = Math.Abs(dx), ay = Math.Abs(dy);

        // Exact 45-degree diagonals: the segment passes through every
        // intermediate cell's center, so walk them one by one.
        if (ax == ay)
        {
            var diag = from;
            while (diag != to)
            {
                diag = new Vector2I(diag.X + sx, diag.Y + sy);
                if (diag == to)
                    break;
                if (Walkable[diag.Y, diag.X] != 1)
                    return false; // wall on the diagonal
                if (IsOccupiedBy(diag, -1))
                    return false; // another character on the diagonal
            }
            return true;
        }

        // Other slopes (including steeper/shallower like (4,1)): integer
        // Bresenham walks the cells whose centers the segment crosses.
        int steps = Math.Max(ax, ay);
        var cur = from;
        int e2 = 2 * (ax >= ay ? ay : ax); // doubled error accumulator
        int d2 = 2 * (ax >= ay ? ax : ay); // doubled dominant delta
        for (int i = 0; i < steps; i++)
        {
            if (ax >= ay)
            {
                cur = new Vector2I(cur.X + sx, cur.Y);
                if (e2 >= d2)
                {
                    cur = new Vector2I(cur.X, cur.Y + sy);
                    e2 -= d2;
                }
            }
            else
            {
                cur = new Vector2I(cur.X, cur.Y + sy);
                if (e2 >= d2)
                {
                    cur = new Vector2I(cur.X + sx, cur.Y);
                    e2 -= d2;
                }
            }
            e2 += 2 * (ax >= ay ? ay : ax);
            if (cur == to)
                break; // target's own cell: endpoint, not an obstacle
            if (Walkable[cur.Y, cur.X] != 1)
                return false; // shot passes through a wall
            if (IsOccupiedBy(cur, -1))
                return false; // shot passes through another character
        }
        return true;
    }

    // Rebuild the shoot-target set for the active combatant from its CURRENT
    // position: every cell inside the weapon's shooting circle
    // (dx^2 + dy^2 <= range^2) with a clear line of sight (no obstacle or
    // other token on the segment).
    private void ComputeShootTargets()
    {
        _shootTargets.Clear();
        if (_turns.Combatants[_turns.CurrentIndex].IsEnemy)
            return;
        int range = _turns.Combatants[_turns.CurrentIndex].Equipment.Weapon.Range;
        var from = CurrentCell();
        for (int dy = -range; dy <= range; dy++)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                if (dx * dx + dy * dy > range * range)
                    continue;
                var cell = new Vector2I(from.X + dx, from.Y + dy);
                if (cell.X < 0 || cell.X >= GridWidth || cell.Y < 0 || cell.Y >= GridHeight)
                    continue;
                if (HasLineOfSight(from, cell, range))
                    _shootTargets.Add(cell);
            }
        }
    }

    // Rebuild the melee-target set for the active combatant from its CURRENT
    // position: the FOUR directly adjacent squares (left, right, up, down —
    // no diagonals) that hold a live token. A melee swing always lands if the
    // target stands there, so no line-of-sight check.
    private void ComputeMeleeTargets()
    {
        _meleeTargets.Clear();
        if (_turns.Combatants[_turns.CurrentIndex].IsEnemy)
            return;
        var from = CurrentCell();
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (_tokens[i].Visible == false)
                continue;
            var cell = TokenCell(i);
            if (cell != from && Math.Abs(cell.X - from.X) + Math.Abs(cell.Y - from.Y) == 1)
                _meleeTargets.Add(cell);
        }
    }

    private void RefreshHighlights()
    {
        // The shooting-range circle: a soft ring centered on the active
        // combatant, radius = weapon range in tiles. Shown in shoot mode for
        // party turns so the player can see exactly who is in range (the red
        // diamonds inside it are the cells that are actually shootable).
        // Enemies don't need the visual — the AI knows its own range. It is
        // a Polygon2D in the skewed Iso layer (a logical circle that the
        // iso skew renders as an ellipse on the board plane).
        if (_rangeCircle == null)
        {
            _rangeCircle = new Polygon2D
            {
                Name = "RangeCircle",
                Color = new Color(0.95f, 0.45f, 0.35f, 0.10f),
                ZIndex = 1, // above the floor grid, under the tokens
            };
            _iso.AddChild(_rangeCircle);
        }
        bool showCircle = _actionMode == ActionMode.Shoot
            && _turnActive
            && !_turns.Combatants[_turns.CurrentIndex].IsEnemy;
        _rangeCircle.Visible = showCircle;
        if (showCircle)
        {
            int radius = _turns.Combatants[_turns.CurrentIndex].Equipment.Weapon.Range * TileSize;
            // The overlay lives in the skewed Iso layer (logical space),
            // centered on the cell's LOGICAL center. It is a 45-degree square
            // (corners at distance r on the logical axes): the iso skew
            // projects it onto the board as a diamond whose bounding box is a
            // square of side 2r around the cell's board-space center — a
            // readable "range" marker that stays square on screen.
            var cell = CurrentCell();
            float cx = (cell.X + 0.5f) * TileSize;
            float cy = (cell.Y + 0.5f) * TileSize;
            _rangeCircle.Position = new Vector2(cx, cy);
            _rangeCircle.Polygon = new[]
            {
                new Vector2(radius, 0f),
                new Vector2(0f, radius),
                new Vector2(-radius, 0f),
                new Vector2(0f, -radius),
            };
        }

        // Move mode: green diamonds on reachable tiles. Shoot mode: red
        // diamonds on every shootable tile (in range with line of sight,
        // from the CURRENT position). Melee mode: orange diamonds on the
        // four adjacent tiles holding a live token. Grenade mode: yellow
        // diamonds on every throwable tile (within throw range of the
        // CURRENT position). The grid layer draws them; only one view is
        // shown at a time.
        bool shootView = _actionMode == ActionMode.Shoot
            && !_turns.Combatants[_turns.CurrentIndex].IsEnemy;
        bool meleeView = _actionMode == ActionMode.Melee
            && !_turns.Combatants[_turns.CurrentIndex].IsEnemy;
        bool grenadeView = _actionMode == ActionMode.Grenade
            && !_turns.Combatants[_turns.CurrentIndex].IsEnemy;
        _grid.Reachable = (shootView || meleeView || grenadeView) ? new HashSet<Vector2I>() : _reachable.Keys;
        _grid.Shootable = shootView ? _shootTargets : new HashSet<Vector2I>();
        _grid.Meleeable = meleeView ? _meleeTargets : new HashSet<Vector2I>();
        _grid.Grenadeable = grenadeView ? _grenadeTargets : new HashSet<Vector2I>();
        // While aiming a grenade, show the cross-shaped blast area (the
        // hovered cell + its four orthogonal neighbours) as a brighter
        // preview; empty otherwise.
        var blastPreview = new HashSet<Vector2I>();
        if (grenadeView && _hoverCell != null && _grenadeTargets.Contains(_hoverCell.Value))
        {
            blastPreview.Add(_hoverCell.Value);
            foreach (var dir in Dirs)
                blastPreview.Add(_hoverCell.Value + dir);
        }
        _grid.BlastCells = blastPreview;
        RefreshGridLabels();
        _grid.Rebuild();
        // Any change that affects mode/move/turn state also changes what the
        // hover path preview may show.
        RefreshPathPreview();
    }

    // Rebuild the per-token HP text list the grid layer draws (live tokens
    // only; dead ones are removed from the map).
    private void RefreshGridLabels()
    {
        _grid.TokenLabels.Clear();
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (!_tokens[i].Visible)
                continue;
            var cell = TokenCell(i);
            _grid.TokenLabels.Add((cell.X, cell.Y, $"{_turns.Combatants[i].HitPoints}"));
        }
    }

    // The unskewed name labels (board-space Controls) follow their token's
    // iso position.
    private void UpdateTokenLabels()
    {
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (_tokenLabels[i] == null)
                continue;
            var cell = TokenCell(i);
            _tokenLabels[i].Position = IsoCellCenter(cell) + new Vector2(-32, -34);
        }
    }

    private void BuildBoard()
    {
        // The skewed isometric layer: a Node2D under the board Control whose
        // transform maps logical board pixels to the 2:1 iso projection.
        // (A Control's own transform is always orthonormal, so the skew has
        // to live on a Node2D; Node2D children compose the board's offset,
        // which is how the camera works.)
        var iso = new Node2D { Name = "Iso" };
        iso.Set("transform", new Transform2D(new Vector2(0.5f, 0.25f), new Vector2(-0.5f, 0.25f), Vector2.Zero));
        _iso = iso;
        _board.AddChild(_iso);

        // The floor grid + highlights + HP readouts, drawn in logical board
        // pixels inside the skewed layer.
        _grid = new IsoGrid { Name = "Grid", Cols = GridWidth, Rows = GridHeight, Tile = TileSize, Map = Walkable };
        _iso.AddChild(_grid);

        // Move-mode path preview (hover a reachable tile to see the route).
        _pathPreview = new PathPreview { Name = "HoverPath", ZIndex = 1 };
        _iso.AddChild(_pathPreview);

        for (int i = 0; i < Roster.Length; i++)
        {
            var (name, color) = (Roster[i].Name, Roster[i].Color);
            var (sx, sy) = StartCells[i];
            // Party tokens are P0.., enemy tokens E0.. (test hooks key off this).
            var tokenName = Roster[i].IsEnemy ? $"E{i - 4}" : $"P{i}";
            var token = new TokenNode(tokenName, color, TileSize)
            {
                Position = new Vector2(sx * TileSize, sy * TileSize),
            };
            _iso.AddChild(token);
            _tokens.Add(token);
            // Unskewed name label (board-space Control) above the diamond.
            var label = new Label
            {
                Text = name,
                HorizontalAlignment = HorizontalAlignment.Center,
                Size = new Vector2(64, 16),
                MouseFilter = MouseFilterEnum.Ignore,
                ZIndex = 2,
            };
            label.Modulate = new Color(0.95f, 0.95f, 1f);
            label.AddThemeFontSizeOverride("font_size", 14);
            _board.AddChild(label);
            _tokenLabels.Add(label);
        }
        RefreshGridLabels();
        _grid.Rebuild();
        UpdateTokenLabels();
    }

    // Refresh the grid layer's HP text after a combatant's HP changed.
    private void RefreshTokenHp(int index)
    {
        RefreshGridLabels();
        _grid.Rebuild();
    }

    // HP hit 0: remove the token from the map. It no longer blocks movement
    // (see IsOccupiedBy) and its turn is skipped (see TurnSystem.EndTurn).
    private void KillToken(int index)
    {
        _tokens[index].Visible = false;
        _tokenLabels[index].Visible = false;
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
            // PASS: the menu overlaps the diamond's bottom-left corner. Its
            // BUTTONS (MouseFilter.Stop) still catch their own clicks; the
            // panel's other area is click-transparent so a click there bubbles
            // to the level root, where OnLevelGuiInput's menu-overlap guard
            // keeps it from also moving a token. (Clicks are caught at the
            // level root, not the Board — see OnLevelGuiInput.)
            MouseFilter = MouseFilterEnum.Pass,
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
        _meleeButton = new Button { Name = "Melee", Text = "MELEE", CustomMinimumSize = new Vector2(84, 34) };
        _moveButton = new Button { Name = "Move", Text = "MOVE", CustomMinimumSize = new Vector2(84, 34) };
        _waitButton = new Button { Name = "Wait", Text = "WAIT", CustomMinimumSize = new Vector2(84, 34) };
        _itemsButton = new Button { Name = "Items", Text = "ITEMS", CustomMinimumSize = new Vector2(84, 34) };
        _shootButton.Pressed += OnShootModePressed;
        _meleeButton.Pressed += OnMeleeModePressed;
        _moveButton.Pressed += OnMoveModePressed;
        _waitButton.Pressed += OnWaitPressed;
        _itemsButton.Pressed += OnItemsPressed;
        actions.AddChild(_shootButton);
        actions.AddChild(_meleeButton);
        actions.AddChild(_moveButton);
        actions.AddChild(_waitButton);
        actions.AddChild(_itemsButton);
        vbox.AddChild(actions);
        // The ITEMS select list: one button per usable item the active
        // combatant carries (BioStim / Grenade). Hidden until ITEMS is pressed.
        _itemsMenu = new PanelContainer
        {
            Name = "ItemsMenu",
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _itemsMenu.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.11f, 0.15f, 0.95f),
            BorderColor = new Color(0.95f, 0.9f, 0.3f, 0.8f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8,
            ContentMarginTop = 8,
            ContentMarginRight = 8,
            ContentMarginBottom = 8,
        });
        var itemsVbox = new VBoxContainer { Name = "ItemsVBox" };
        itemsVbox.AddThemeConstantOverride("separation", 6);
        _bioStimButton = new Button { Name = "BioStim", Text = "BIOSTIM x0", CustomMinimumSize = new Vector2(160, 30) };
        _grenadeButton = new Button { Name = "Grenade", Text = "GRENADE x0", CustomMinimumSize = new Vector2(160, 30) };
        _bioStimButton.Pressed += OnBioStimPressed;
        _grenadeButton.Pressed += OnGrenadePressed;
        _itemButtons.Add(_bioStimButton);
        _itemButtons.Add(_grenadeButton);
        itemsVbox.AddChild(_bioStimButton);
        itemsVbox.AddChild(_grenadeButton);
        _itemsMenu.AddChild(itemsVbox);
        vbox.AddChild(_itemsMenu);
        // Second row: END TURN, so the turn can be closed without reaching for
        // the top-right debug panel.
        var turnRow = new HBoxContainer { Name = "TurnRow" };
        turnRow.AddThemeConstantOverride("separation", 8);
        _endTurnMenuButton = new Button { Name = "EndTurn", Text = "END TURN", CustomMinimumSize = new Vector2(160, 34) };
        _endTurnMenuButton.Pressed += OnEndTurnPressed;
        turnRow.AddChild(_endTurnMenuButton);
        vbox.AddChild(turnRow);
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
        int nades = current.GetItemCount(Item.Grenade);
        _bioStimButton.Text = $"BIOSTIM x{stims}";
        _grenadeButton.Text = $"GRENADE x{nades}";
        // One action (shoot/melee/wait/item) and one move per turn; each
        // budget disables its own buttons independently.
        bool enemyTurn = current.IsEnemy;
        _shootButton.Disabled = enemyTurn || _actionUsed;
        _meleeButton.Disabled = enemyTurn || _actionUsed;
        _moveButton.Disabled = enemyTurn || _movedThisTurn;
        _waitButton.Disabled = enemyTurn || _actionUsed;
        // ITEMS is disabled once the action is spent (or on enemy turns); it
        // can still be opened to look at the list before spending the action.
        _itemsButton.Disabled = enemyTurn || _actionUsed;
        _bioStimButton.Disabled = _actionUsed || stims <= 0 || current.HitPoints >= current.MaxHitPoints;
        _grenadeButton.Disabled = _actionUsed || nades <= 0;
        _itemsMenu.Visible = _itemsMenuOpen && !enemyTurn;
        _shootButton.Modulate = _actionMode == ActionMode.Shoot ? Colors.White : new Color(1, 1, 1, 0.55f);
        _meleeButton.Modulate = _actionMode == ActionMode.Melee ? Colors.White : new Color(1, 1, 1, 0.55f);
        _moveButton.Modulate = _actionMode == ActionMode.Move ? Colors.White : new Color(1, 1, 1, 0.55f);
        _itemsButton.Modulate = _actionMode == ActionMode.Grenade ? Colors.White : new Color(1, 1, 1, 0.55f);
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

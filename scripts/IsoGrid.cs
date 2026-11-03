using System.Collections.Generic;
using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// The isometric board floor: a Node2D (placed under the level's skewed "Iso"
/// node) that draws every tile as a 2:1 diamond in LOGICAL board pixels
/// (x*TileSize, y*TileSize), plus the move/shoot highlights and the tokens'
/// HP readouts. Obstacles are drawn as darker, extruded blocks.
/// </summary>
public partial class IsoGrid : Node2D
{
    public int Cols;
    public int Rows;
    public int Tile = 48;
    public int[,] Map;
    // Cells the active combatant may move to (green diamonds).
    public IEnumerable<Vector2I> Reachable { get; set; } = new HashSet<Vector2I>();
    // Cells the active combatant can shoot (yellow diamonds).
    public IEnumerable<Vector2I> Shootable { get; set; } = new HashSet<Vector2I>();
    // The hovered tile, drawn red while it is a valid shoot target.
    public IEnumerable<Vector2I> HoverTarget { get; set; } = new HashSet<Vector2I>();
    // Cells the active combatant can hit with the melee weapon (orange diamonds):
    // the four directly adjacent squares holding a live token.
    public IEnumerable<Vector2I> Meleeable { get; set; } = new HashSet<Vector2I>();
    // Cells the active party member can throw a grenade to (yellow diamonds).
    public IEnumerable<Vector2I> Grenadeable { get; set; } = new HashSet<Vector2I>();
    // The cross-shaped blast area of the hovered grenade target (5 cells),
    // shown as a red cross preview while aiming.
    public IEnumerable<Vector2I> BlastCells { get; set; } = new HashSet<Vector2I>();
    // Cells the active combatant is overwatching (cyan diamonds).
    public IEnumerable<Vector2I> OverwatchCells { get; set; } = new HashSet<Vector2I>();
    // (x, y, hp text) per live token, drawn above the token's diamond.
    public List<(int X, int Y, string Text)> TokenLabels = new();

    // Floor texture (loaded from assets/textures/floor/). When set, walkable
    // tiles are drawn with this texture instead of flat colors. The texture
    // is mapped onto each tile's logical bounding square; the iso skew on the
    // parent node projects it to a diamond automatically.
    public Texture2D? FloorTexture;

    private static readonly Color FloorA = new(0.13f, 0.16f, 0.24f);
    private static readonly Color FloorB = new(0.09f, 0.11f, 0.16f);
    private static readonly Color Edge = new(0.35f, 0.5f, 0.7f, 0.16f);
    private static readonly Color WallTop = new(0.16f, 0.14f, 0.22f);
    private static readonly Color WallSideL = new(0.09f, 0.08f, 0.14f);
    private static readonly Color WallSideR = new(0.12f, 0.10f, 0.17f);
    private const float WallHeight = 18f;
    private static readonly Color MoveHl = new(0.3f, 0.9f, 0.6f, 0.28f);
    private static readonly Color ShootHl = new(0.95f, 0.9f, 0.3f, 0.30f);
    private static readonly Color HoverHl = new(0.95f, 0.35f, 0.3f, 0.55f);
    private static readonly Color MeleeHl = new(0.98f, 0.62f, 0.2f, 0.42f);
    private static readonly Color GrenadeHl = new(0.95f, 0.9f, 0.3f, 0.30f);
    // The blast preview is a RED cross over the 5 blast cells while aiming
    // (drawn after the yellow throw targets so it reads on top of them).
    private static readonly Color BlastHl = new(0.95f, 0.30f, 0.25f, 0.60f);
    // Overwatch: the watched line (cyan diamonds) — distinct from the yellow
    // shoot/grenade targets so the player can see the danger line.
    private static readonly Color OverwatchHl = new(0.35f, 0.85f, 0.95f, 0.45f);
    private static readonly Color HpText = new(0.9f, 0.95f, 1f);

    // Static floor geometry, cached once so _Draw (which runs on every
    // highlight change) allocates nothing for the 448 tiles.
    private struct FloorTile
    {
        public Vector2[] Poly;
        public Color Color;
        // Edge outline segments (DrawMultiline point pairs); empty for walls.
        public Vector2[] Ring;
        // Logical bounding rect for texture drawing (tile origin to tile+1).
        public Rect2 TexRect;
        public bool Walkable;
    }

    private struct WallFace
    {
        public Vector2[] Poly;
        public Color Color;
        // Edge outline segments for the top face; empty for side faces.
        public Vector2[] Ring;
    }

    private static readonly Color WallEdge = new(0.4f, 0.35f, 0.6f, 0.3f);

    private readonly List<FloorTile> _floorTiles = new();
    private readonly List<WallFace> _wallFaces = new();
    private bool _floorBuilt;

    public void Rebuild() => QueueRedraw();

    private void EnsureFloorBuilt()
    {
        if (_floorBuilt)
            return;
        _floorBuilt = true;
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Cols; x++)
            {
                bool walkable = Map[y, x] == 1;
                var poly = Diamond(x, y);
                _floorTiles.Add(new FloorTile
                {
                    Poly = poly,
                    Color = walkable ? ((x + y) % 2 == 0 ? FloorA : FloorB) : WallTop,
                    Ring = walkable ? RingSegments(poly) : Array.Empty<Vector2>(),
                    TexRect = new Rect2(x * Tile, y * Tile, Tile, Tile),
                    Walkable = walkable,
                });
                if (!walkable)
                {
                    // Obstacle block: the top diamond lifted by WallHeight
                    // plus the two screen-downward faces (poly[0]=right ->
                    // poly[1]=bottom -> poly[2]=left are the on-screen south
                    // rim of the diamond) so it reads as a low block.
                    var top = new[]
                    {
                        poly[0] + Vector2.Up * WallHeight,
                        poly[1] + Vector2.Up * WallHeight,
                        poly[2] + Vector2.Up * WallHeight,
                        poly[3] + Vector2.Up * WallHeight,
                    };
                    _wallFaces.Add(new WallFace
                    {
                        Poly = new[] { poly[0], poly[1], top[1], top[0] },
                        Color = WallSideL,
                    });
                    _wallFaces.Add(new WallFace
                    {
                        Poly = new[] { poly[1], poly[2], top[2], top[1] },
                        Color = WallSideR,
                    });
                    _wallFaces.Add(new WallFace { Poly = top, Color = WallTop, Ring = RingSegments(top) });
                }
            }
        }
    }

    // The four corners of a logical cell in the skewed (iso) local space.
    // The skew X=(.5,.25) Y=(-.5,.25) projects this square onto the board as
    // a 2:1 iso diamond; the corner order here is [right, bottom, left, top]
    // in BOARD space — adjacent corners for side faces, never opposites.
    private Vector2 CornerRight(int cx, int cy) => new((cx + 1) * Tile, cy * Tile);
    private Vector2 CornerBottom(int cx, int cy) => new((cx + 1) * Tile, (cy + 1) * Tile);
    private Vector2 CornerLeft(int cx, int cy) => new(cx * Tile, (cy + 1) * Tile);
    private Vector2 CornerTop(int cx, int cy) => new(cx * Tile, cy * Tile);

    private Vector2[] Diamond(int cx, int cy) =>
        new[]
        {
            CornerRight(cx, cy),   // right  (screen east)
            CornerBottom(cx, cy),  // bottom (screen south)
            CornerLeft(cx, cy),    // left   (screen west)
            CornerTop(cx, cy),     // top    (screen north)
        };

    // A closed ring expressed as the point PAIRS that DrawMultiline wants
    // (each consecutive pair is one line segment, and the last wraps to the
    // first): c0c1, c1c2, c2c3, c3c0 -> 8 points for a 4-corner diamond.
    private Vector2[] RingSegments(Vector2[] ring)
    {
        var out_ = new Vector2[ring.Length * 2];
        for (int i = 0; i < ring.Length; i++)
        {
            out_[i * 2] = ring[i];
            out_[i * 2 + 1] = ring[(i + 1) % ring.Length];
        }
        return out_;
    }

    public override void _Draw()
    {
        if (Map == null)
            return;
        EnsureFloorBuilt();

        // 1. Floor tiles (geometry cached in EnsureFloorBuilt).
        foreach (var t in _floorTiles)
        {
            if (t.Walkable && FloorTexture != null)
            {
                // Draw the texture onto the tile's logical bounding square.
                // The iso skew on the parent node projects this square to a
                // 2:1 diamond on screen automatically.
                DrawTextureRect(FloorTexture, t.TexRect, false);
            }
            else
            {
                DrawColoredPolygon(t.Poly, t.Color);
            }
            if (t.Ring.Length > 0)
                DrawMultiline(t.Ring, Edge, 1f);
        }
        foreach (var f in _wallFaces)
        {
            DrawColoredPolygon(f.Poly, f.Color);
            if (f.Ring != null)
                DrawMultiline(f.Ring, WallEdge, 1f);
        }

        // 2. Highlights (move = green, shoot range = yellow, hovered shoot
        // target = red, melee = orange, grenade targets = yellow, blast
        // preview = brighter yellow).
        foreach (var cell in Reachable)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), MoveHl);
        foreach (var cell in Shootable)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), ShootHl);
        foreach (var cell in HoverTarget)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), HoverHl);
        foreach (var cell in Meleeable)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), MeleeHl);
        foreach (var cell in Grenadeable)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), GrenadeHl);
        foreach (var cell in BlastCells)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), BlastHl);
        foreach (var cell in OverwatchCells)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), OverwatchHl);

        // 3. Token HP readouts, drawn last so they sit on top of everything.
        var font = ThemeDB.FallbackFont;
        foreach (var (x, y, text) in TokenLabels)
        {
            // Cell center in this (logical) local space, lifted above the tile.
            var pos = new Vector2((x + 0.5f) * Tile, (y + 0.5f) * Tile) + new Vector2(0, -34);
            DrawStringOutline(font, pos, text, HorizontalAlignment.Center, 0, 14, 3,
                new Color(0, 0, 0, 0.9f));
            DrawString(font, pos, text, HorizontalAlignment.Center, 0, 14, HpText);
        }
    }
}

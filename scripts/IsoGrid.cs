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
    // Cells the active combatant can shoot (red diamonds).
    public IEnumerable<Vector2I> Shootable { get; set; } = new HashSet<Vector2I>();
    // Cells the active combatant can hit with the melee weapon (orange diamonds):
    // the four directly adjacent squares holding a live token.
    public IEnumerable<Vector2I> Meleeable { get; set; } = new HashSet<Vector2I>();
    // Cells the active party member can throw a grenade to (yellow diamonds).
    public IEnumerable<Vector2I> Grenadeable { get; set; } = new HashSet<Vector2I>();
    // The cross-shaped blast area of the hovered grenade target (5 cells),
    // shown as a brighter preview while aiming.
    public IEnumerable<Vector2I> BlastCells { get; set; } = new HashSet<Vector2I>();
    // (x, y, hp text) per live token, drawn above the token's diamond.
    public List<(int X, int Y, string Text)> TokenLabels = new();

    private static readonly Color FloorA = new(0.13f, 0.16f, 0.24f);
    private static readonly Color FloorB = new(0.09f, 0.11f, 0.16f);
    private static readonly Color Edge = new(0.35f, 0.5f, 0.7f, 0.16f);
    private static readonly Color WallTop = new(0.16f, 0.14f, 0.22f);
    private static readonly Color WallSideL = new(0.09f, 0.08f, 0.14f);
    private static readonly Color WallSideR = new(0.12f, 0.10f, 0.17f);
    private const float WallHeight = 14f;
    private static readonly Color MoveHl = new(0.3f, 0.9f, 0.6f, 0.28f);
    private static readonly Color ShootHl = new(0.95f, 0.35f, 0.3f, 0.38f);
    private static readonly Color MeleeHl = new(0.98f, 0.62f, 0.2f, 0.42f);
    private static readonly Color GrenadeHl = new(0.95f, 0.9f, 0.3f, 0.30f);
    private static readonly Color BlastHl = new(0.95f, 0.9f, 0.3f, 0.55f);
    private static readonly Color HpText = new(0.9f, 0.95f, 1f);

    public void Rebuild() => QueueRedraw();

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

        // 1. Floor tiles.
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Cols; x++)
            {
                bool walkable = Map[y, x] == 1;
                var poly = Diamond(x, y);
                var color = walkable ? ((x + y) % 2 == 0 ? FloorA : FloorB) : WallTop;
                DrawColoredPolygon(poly, color);
                if (walkable)
                {
                    DrawMultiline(RingSegments(poly), Edge, 1f);
                }
                else
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
                    DrawColoredPolygon(new[] { poly[0], poly[1], top[1], top[0] }, WallSideL);
                    DrawColoredPolygon(new[] { poly[1], poly[2], top[2], top[1] }, WallSideR);
                    DrawColoredPolygon(top, WallTop);
                    DrawMultiline(RingSegments(top), new Color(0.4f, 0.35f, 0.6f, 0.3f), 1f);
                }
            }
        }

        // 2. Highlights (move = green, shoot = red, melee = orange,
        // grenade targets = yellow, blast preview = brighter yellow).
        foreach (var cell in Reachable)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), MoveHl);
        foreach (var cell in Shootable)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), ShootHl);
        foreach (var cell in Meleeable)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), MeleeHl);
        foreach (var cell in Grenadeable)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), GrenadeHl);
        foreach (var cell in BlastCells)
            DrawColoredPolygon(Diamond(cell.X, cell.Y), BlastHl);

        // 3. Token HP readouts, drawn last so they sit on top of everything.
        var font = ThemeDB.FallbackFont;
        foreach (var (x, y, text) in TokenLabels)
        {
            // Cell center in this (logical) local space, lifted above the tile.
            var pos = new Vector2((x + 0.5f) * Tile, (y + 0.5f) * Tile) + new Vector2(0, -26);
            DrawStringOutline(font, pos, text, HorizontalAlignment.Center, 0, 12, 3,
                new Color(0, 0, 0, 0.9f));
            DrawString(font, pos, text, HorizontalAlignment.Center, 0, 12, HpText);
        }
    }
}

using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// A combatant token on the isometric board. It is a Node2D that lives in the
/// level's skewed "Iso" layer at its token's LOGICAL cell corner
/// (x*TileSize, y*TileSize) and draws a filled diamond in its own local space.
/// Because the whole Iso layer is skewed, that local "square" footprint is
/// rendered as a 2:1 iso diamond automatically — no per-token projection math
/// is needed. The character's name is drawn by the level as a separate
/// unskewed Control label (text should not be skewed).
/// </summary>
public partial class TokenNode : Node2D
{
    private const float Pad = 9;
    private readonly Color _color;
    private readonly int _tile;

    public TokenNode(string name, Color color, int tileSize)
    {
        Name = name;
        _color = color;
        _tile = tileSize;
        ZIndex = 1; // above the floor grid
    }

    public override void _Draw()
    {
        // The token's footprint is a square inset by Pad in logical space;
        // under the Iso skew this draws as a diamond. A darker square drawn
        // first (slightly larger, smaller inset) shows as a thin outline
        // border around the body so the token reads against the floor.
        float o = Pad - 2f;
        DrawColoredPolygon(new[]
        {
            new Vector2(o, o),
            new Vector2(_tile - o, o),
            new Vector2(_tile - o, _tile - o),
            new Vector2(o, _tile - o),
        }, new Color(0.05f, 0.06f, 0.1f, 0.9f));
        DrawColoredPolygon(new[]
        {
            new Vector2(Pad, Pad),
            new Vector2(_tile - Pad, Pad),
            new Vector2(_tile - Pad, _tile - Pad),
            new Vector2(Pad, _tile - Pad),
        }, _color);
    }
}

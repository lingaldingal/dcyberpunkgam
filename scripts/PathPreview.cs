using System.Collections.Generic;
using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// Move-mode path preview: a small Node2D in the skewed Iso layer that draws
/// the BFS route (a polyline through the cell centers) and a circle on the
/// destination cell. Hidden entirely unless a reachable cell is hovered.
/// Polyline2D / Circle2D are not in the 4.7.2 C# binding, so this draws them
/// directly.
/// </summary>
public partial class PathPreview : Node2D
{
    public static readonly Color LineColor = new(0.4f, 0.95f, 0.6f, 0.9f);
    public static readonly Color EndColor = new(0.4f, 0.95f, 0.6f, 0.35f);
    private const float LineWidth = 3f;
    private const float EndRadius = 48f * 0.5f; // one half-tile

    // The route in LOGICAL board pixels (cell centers). Empty = hidden.
    public List<Vector2> Points { get; set; } = new();

    // True while a reachable cell is hovered (the whole overlay is shown/hidden
    // as one unit, so both the line and the end circle follow this flag).
    public bool Active => Points.Count > 0;

    public Vector2 EndPoint => Points.Count > 0 ? Points[Points.Count - 1] : Vector2.Zero;

    public void SetPath(List<Vector2> points)
    {
        Points = points ?? new List<Vector2>();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Points.Count < 2)
            return;
        var pts = new Vector2[Points.Count];
        Points.CopyTo(pts, 0);
        DrawPolyline(pts, LineColor, LineWidth, true);
        DrawCircle(EndPoint, EndRadius, EndColor);
        DrawArc(EndPoint, EndRadius, 0, Mathf.Tau, 48, new Color(0.4f, 0.95f, 0.6f, 0.8f), 2f, true);
    }
}

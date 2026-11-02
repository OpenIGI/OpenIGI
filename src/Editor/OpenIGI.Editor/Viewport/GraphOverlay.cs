using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Formats.Ai;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The selected AI graph drawn over the world: its links as lines and its nodes as dots, the picked node ringed. Click
/// a node to pick it; the move gizmo then moves the node instead of the graph.
/// </summary>
internal sealed class GraphOverlay
{
    // Nodes and links beyond this are not drawn, so the view stays responsive on the largest graphs.
    private const double DrawDistanceMetres = 400.0;

    /// <summary>Gets the picked node's id, or <see langword="null"/>.</summary>
    public int? PickedNode { get; private set; }

    /// <summary>Forgets the picked node.</summary>
    public void Unpick() => PickedNode = null;

    /// <summary>Gets where the picked node stands, in world metres.</summary>
    /// <param name="task">The AIGraph task.</param>
    /// <param name="graph">Its document.</param>
    /// <returns>The position, or <see langword="null"/> when no node is picked.</returns>
    public Vector3d? PickedPosition(MissionTask task, GraphDocument graph)
    {
        if (PickedNode is not { } id)
        {
            return null;
        }

        foreach (GraphDocumentNode node in graph.Nodes)
        {
            if (node.Id == id && node.PositionRecord >= 0)
            {
                return MissionGraphs.ToWorldMetres(task, node.Position);
            }
        }

        PickedNode = null;
        return null;
    }

    /// <summary>Picks the node nearest a point on screen, within a radius.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="task">The AIGraph task.</param>
    /// <param name="graph">Its document.</param>
    /// <param name="point">The point.</param>
    /// <param name="radius">The radius in pixels.</param>
    /// <returns>Whether a node was picked.</returns>
    public bool Pick(ViewportProjector projector, MissionTask task, GraphDocument graph, UiVec2 point, float radius)
    {
        int? best = null;
        float bestDistance = radius;
        foreach (GraphDocumentNode node in graph.Nodes)
        {
            if (node.PositionRecord >= 0 && projector.TryProject(MissionGraphs.ToWorldMetres(task, node.Position), out UiVec2 screen))
            {
                float distance = UiVec2.Distance(screen, point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = node.Id;
                }
            }
        }

        PickedNode = best;
        return best is not null;
    }

    /// <summary>Draws the graph.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="projector">The view.</param>
    /// <param name="task">The AIGraph task.</param>
    /// <param name="graph">Its document.</param>
    public void Draw(UiPainter painter, EditorTheme theme, ViewportProjector projector, MissionTask task, GraphDocument graph)
    {
        Dictionary<int, (UiVec2 Screen, bool Visible)> placed = new(graph.Nodes.Count);
        foreach (GraphDocumentNode node in graph.Nodes)
        {
            if (node.PositionRecord < 0)
            {
                continue;
            }

            Vector3d world = MissionGraphs.ToWorldMetres(task, node.Position);
            bool near = Vector3d.Distance(world, projector.Eye) < DrawDistanceMetres;
            bool visible = projector.TryProject(world, out UiVec2 screen) && near;
            placed[node.Id] = (screen, visible);
        }

        Bgra32 link = theme.Accent.WithAlpha(150);
        foreach ((int from, int to) in graph.Links)
        {
            if (placed.TryGetValue(from, out var a) && placed.TryGetValue(to, out var b) && a.Visible && b.Visible)
            {
                painter.StrokeLine(a.Screen, b.Screen, theme.Hairline, link);
            }
        }

        foreach ((int id, (UiVec2 screen, bool visible)) in placed)
        {
            if (!visible)
            {
                continue;
            }

            bool picked = id == PickedNode;
            painter.FillDot(screen, theme.Px(picked ? 6f : 3.5f), picked ? theme.Text : theme.Accent);
            if (picked)
            {
                painter.DrawText(theme.SmallFont, $"node {id}", new UiVec2(screen.X + theme.Px(9f), screen.Y - theme.Px(7f)), theme.SmallTextSize, theme.Text);
            }
        }
    }
}

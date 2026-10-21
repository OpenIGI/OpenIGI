using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>The footing a mind gives its soldier from the graph, <c>human.c Human_GroundCollision</c> (its HumanAI half).</content>
public sealed partial class Igi2HumanAi : IIgi2GraphFooting
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The stretch runs from the node the soldier stands on to the node beside it: the first step of its way ahead, or, between
    /// legal nodes, whichever of the two it does not stand on. With no legal nodes the stretch may instead run back to the node it
    /// came from, when that node lies nearer the soldier than the node it stands on does.
    /// </para>
    /// <para>
    /// The footing lies under where the move started, at the stretch's height that far along it; its normal is straight up and its
    /// material is the standing node's. The light is the standing node's and the next one's, mixed by the same fraction, even when
    /// the stretch was turned back.
    /// </para>
    /// </remarks>
    public bool TryGetFooting(Vector3d from, Igi2Intersection ground, out float light)
    {
        light = 0f;
        if (Graph is not { } graph || graph.NodeAt(CurrentNode) is not { } node)
        {
            return false;
        }

        Vector3d start = node.Position;
        ground.Position = from;
        ground.EnterMaterial = node.Material >= 0 ? node.Material : null;
        ground.IsHit = true;
        ground.Normal = new Vector3f(0f, 0f, 1f);

        Igi2AiGraphNode? beside = null;
        Vector3d end = start;
        if (MovementQueue.Count > 0 && graph.NodeAt(MovementQueue[0].NodeId) is { } step)
        {
            beside = step;
            end = step.Position;
        }

        if (LegalNodes.IsUsed)
        {
            int other = LegalNodes.Node1 == node.Id ? LegalNodes.Node2 : LegalNodes.Node1;
            beside = graph.NodeAt(other);
            end = beside?.Position ?? start;
        }
        else if (graph.NodeAt(PreviousNode == -1 ? CurrentNode : PreviousNode) is { } back)
        {
            Vector3d behind = back.Position;
            Fpu hereX = (Fpu)start.X - behind.X;
            Fpu hereY = (Fpu)start.Y - behind.Y;
            Fpu hereZ = (Fpu)start.Z - behind.Z;
            Fpu fromX = (Fpu)from.X - behind.X;
            Fpu fromY = (Fpu)from.Y - behind.Y;
            Fpu fromZ = (Fpu)from.Z - behind.Z;
            Fpu toHere = Fpu.Sqrt((hereX * hereX) + (hereY * hereY) + (hereZ * hereZ));
            Fpu toFrom = Fpu.Sqrt((fromY * fromY) + (fromZ * fromZ) + (fromX * fromX));
            if (toHere.Value > toFrom.Value)
            {
                end = start;
                start = behind;
            }
        }

        Fpu acrossX = (Fpu)end.X - start.X;
        Fpu acrossY = (Fpu)end.Y - start.Y;
        Fpu acrossZ = (Fpu)end.Z - start.Z;
        Fpu alongX = (Fpu)from.X - start.X;
        Fpu alongY = (Fpu)from.Y - start.Y;
        Fpu alongZ = (Fpu)from.Z - start.Z;
        Fpu length = Fpu.Sqrt((acrossX * acrossX) + (acrossY * acrossY) + (acrossZ * acrossZ));
        Fpu fraction = (Fpu)0f;
        if (length.Value > 0.0)
        {
            fraction = Fpu.Sqrt((alongX * alongX) + (alongY * alongY) + (alongZ * alongZ)) / length;
            if (fraction.Value < 0.0)
            {
                fraction = (Fpu)0f;
            }
            else if (fraction.Value > 1.0)
            {
                fraction = (Fpu)1f;
            }
        }

        light = beside is not null ? ((((Fpu)beside.LightValue - node.LightValue) * fraction) + node.LightValue).F : node.LightValue;
        ground.Position = new Vector3d(from.X, from.Y, ((fraction * acrossZ) + start.Z).Value);
        ground.HitObject = graph.Ground;
        return true;
    }
}

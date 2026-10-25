using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The pure geometry a soldier's AI measures with: the node squares the planner and the walker test, where a line
/// leaves one, where an AI aims at a human, and the heading wrap every turn goes through.
/// </summary>
/// <remarks>Nothing here reads or writes a soldier's state; each answer depends on its arguments alone.</remarks>
internal static class Igi1AiGeometry
{
    /// <summary>IGI 1's oriented node-box test, <c>sub_4F9A40</c>.</summary>
    /// <param name="position">The world position to test.</param>
    /// <param name="node">The graph node and its authored heading.</param>
    /// <param name="devianceMetres">The extra half-extent supplied by the caller.</param>
    /// <returns><see langword="true"/> when the point lies in the rotated XY square.</returns>
    public static bool IsInsideNodeBox(Vector3d position, GraphNode node, double devianceMetres)
    {
        ArgumentNullException.ThrowIfNull(node);

        double dx = position.X - node.Position.X;
        double dy = position.Y - node.Position.Y;
        double sin = Math.Sin(node.Gamma);
        double cos = Math.Cos(node.Gamma);
        double localX = (cos * dx) + (sin * dy);
        double localY = (cos * dy) - (sin * dx);
        double halfExtent = (node.Radius + devianceMetres) * GraphNode.MetreInUnits;
        return localX >= -halfExtent && localX <= halfExtent
            && localY >= -halfExtent && localY <= halfExtent;
    }

    /// <summary>Where an AI aims at a human — its origin, half a metre up.</summary>
    /// <param name="target">The target's feet, as the port keeps a human's position.</param>
    /// <param name="crouching">Whether the target is crouching, which halves the raise.</param>
    /// <returns>The aim point, in engine units.</returns>
    /// <remarks>
    /// <c>sub_451380</c> copies the target task's own position (<c>Human+32</c>, its origin) out of the shared
    /// record and adds <b>2048 units (0.5 m) standing, 1024 (0.25 m) crouching</b> to Z, picking
    /// 1024 when the target is crouched and 2048 otherwise. The port's position is the feet, so the origin's height
    /// goes in first: chest height, where adding the raise to the feet aimed at the shins.
    /// </remarks>
    public static Vector3d AimPointOf(Vector3d target, bool crouching = false) =>
        new(target.X, target.Y, target.Z + (crouching
            ? Igi1HumanAi.HumanOriginCrouchedInUnits + 1024.0
            : Igi1HumanAi.HumanOriginStandingInUnits + 2048.0));

    /// <summary>
    /// Where a line out of a node's centre leaves its square — <c>0x4520C0</c>.
    /// </summary>
    /// <param name="node">The node, whose square is its radius either side of the centre on the world axes.</param>
    /// <param name="direction">The line's direction; only its horizontal part is used.</param>
    /// <param name="exit">
    /// <see langword="true"/> for the edge the line leaves by, <see langword="false"/> for the one it enters by.
    /// </param>
    /// <returns>The point on the edge at the centre's height, or the centre when the line is too short to leave.</returns>
    public static Vector3d EdgePoint(GraphNode node, Vector3d direction, bool exit)
    {
        double half = node.Radius * GraphNode.MetreInUnits;
        double dx = direction.X;
        double dy = direction.Y;
        double offsetX;
        double offsetY;
        if (Math.Abs(dx) > half && Math.Abs(half / dx * dy) <= half)
        {
            offsetX = Math.CopySign(half, dx);
            offsetY = Math.Abs(half / dx) * dy;
        }
        else if (Math.Abs(dy) > half && Math.Abs(half / dy * dx) <= half)
        {
            offsetX = Math.Abs(half / dy) * dx;
            offsetY = Math.CopySign(half, dy);
        }
        else
        {
            return node.Position;
        }

        return exit
            ? new Vector3d(node.Position.X + offsetX, node.Position.Y + offsetY, node.Position.Z)
            : new Vector3d(node.Position.X - offsetX, node.Position.Y - offsetY, node.Position.Z);
    }

    /// <summary>Whether a node is one of a door's pair — a hop between two of them crosses the door.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when its criteria carry the door bit.</returns>
    public static bool IsDoorNode(GraphNode node) => (node.Criteria & GraphNodeCriteria.Door) != 0;

    /// <summary>Wraps a heading into <c>[-pi, pi]</c>, a turn at a time.</summary>
    /// <param name="radians">The heading or turn, in radians.</param>
    /// <returns>The same angle, wrapped.</returns>
    public static float Wrap(float radians)
    {
        while (radians > MathF.PI)
        {
            radians -= MathF.Tau;
        }

        while (radians < -MathF.PI)
        {
            radians += MathF.Tau;
        }

        return radians;
    }
}

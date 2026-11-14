namespace OpenIGI.Editor.Viewport;

/// <summary>One panel of a fence run: where it stands across the ground, in metres, and its turn (<c>Gamma</c>).</summary>
/// <param name="X">East, in metres.</param>
/// <param name="Y">North, in metres.</param>
/// <param name="Gamma">Its turn about the up axis, in radians.</param>
internal readonly record struct FencePanel(double X, double Y, double Gamma);

/// <summary>
/// A fence carried on from one panel toward a point: panels end to end along the line, each turned so its long side follows
/// it, as the levels lay theirs (a <c>Fence</c> task a panel: Position, Gamma, Model).
/// </summary>
internal static class FenceRun
{
    /// <summary>The panels from the first toward a point: the first turned along the line, then one a panel''s length on, as many as fit.</summary>
    /// <param name="startX">The first panel's east, in metres.</param>
    /// <param name="startY">Its north, in metres.</param>
    /// <param name="halfX">Half its size along its own X, in metres.</param>
    /// <param name="halfY">Half its size along its own Y, in metres.</param>
    /// <param name="toX">Where the run goes to, east.</param>
    /// <param name="toY">Where it goes to, north.</param>
    /// <returns>The panels, the first among them; only the first when the point is nearer than a panel.</returns>
    /// <remarks>
    /// A turn of <c>Gamma</c> takes a panel's X to <c>(cos, sin)</c> and its Y to <c>(-sin, cos)</c> across the ground; its long
    /// side is the one that follows the line.
    /// </remarks>
    public static IReadOnlyList<FencePanel> Layout(double startX, double startY, double halfX, double halfY, double toX, double toY)
    {
        double dx = toX - startX, dy = toY - startY;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        double length = 2 * Math.Max(halfX, halfY);
        if (distance < 1e-6 || length < 0.05)
        {
            return [];
        }

        double heading = Math.Atan2(dy, dx);
        double gamma = halfX >= halfY ? heading : heading - (Math.PI / 2);
        (double ux, double uy) = (dx / distance, dy / distance);
        int count = 1 + (int)Math.Floor(distance / length);
        return [.. Enumerable.Range(0, count).Select(i => new FencePanel(startX + (ux * length * i), startY + (uy * length * i), gamma))];
    }
}

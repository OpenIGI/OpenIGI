using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// A line of sight against a straight spline segment: <c>SplineObjSegment_LineOfSightHandler</c> (<c>igi2 0x5B9E70</c>),
/// with the arithmetic the client's FPU performs.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the solid models, a segment answers sight with its line trace (<see cref="Igi2SplineSegmentTrace"/>), cast
/// again and again: each cast starts 40.96 units past the last face it struck, so every face along the line is met in
/// turn. A sight already crossed elsewhere starts its first cast past that crossing.
/// </para>
/// <para>
/// Only a face entered from the front, with a material, counts: it marks the sight crossed, becomes its material and
/// where it was crossed, and lets through its material's transparency. The casts stop when the line runs clear, when a
/// face lets nothing more through, or, on a sight crossed before, at a face no nearer than the last once nothing gets
/// through.
/// </para>
/// </remarks>
public static class Igi2SplineSegmentLineOfSight
{
    /// <summary>How far past a struck face the next cast starts: <c>40.96</c> units, a centimetre.</summary>
    private const float Step = 40.96f;

    /// <summary>Meets one segment.</summary>
    /// <param name="segment">The segment.</param>
    /// <param name="sight">The line of sight.</param>
    public static void Meet(Igi2SplineSegment segment, Igi2LineOfSight sight)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(sight);

        Vector3d origin = sight.Start, end = sight.End;
        float dx = ((Fpu)end.X - origin.X).F, dy = ((Fpu)end.Y - origin.Y).F, dz = ((Fpu)end.Z - origin.Z).F;
        if (dx == 0f && dy == 0f && dz == 0f)
        {
            return;
        }

        if (sight.Includes is { } includes && !includes(segment.Owner))
        {
            return;
        }

        float length = Fpu.Sqrt(((Fpu)dx * dx) + ((Fpu)dy * dy) + ((Fpu)dz * dz)).F;
        Vector3d start = origin;
        float reached;
        if (sight.IsHit)
        {
            // Past the crossing already found: along the unit direction by its distance and a centimetre.
            reached = ((Fpu)length * sight.T).F;
            Fpu inverse = (Fpu)1.0 / length;
            float ux = ((Fpu)dx * inverse).F, uy = ((Fpu)dy * inverse).F;
            Fpu uz = inverse * dz;
            Fpu past = (Fpu)reached + Step;
            float py = (past * uy).F, pz = (past * uz).F;
            start = new Vector3d(
                (((Fpu)ux * past) + start.X).Value,
                ((Fpu)py + start.Y).Value,
                ((Fpu)pz + start.Z).Value);
        }
        else
        {
            reached = length;
        }

        Igi2LineTrace cast = new(1) { Includes = sight.Includes };
        Igi2Intersection hit = cast.Intersections[0];
        cast.Ends[0] = end;
        hit.IsHit = false;
        while (true)
        {
            cast.Starts[0] = start;
            Igi2SplineSegmentTrace.Trace(segment, cast);
            if (!hit.IsHit)
            {
                return;
            }

            float hx = ((Fpu)hit.Position.X - origin.X).F, hy = ((Fpu)hit.Position.Y - origin.Y).F;
            Fpu hz = (Fpu)hit.Position.Z - origin.Z;
            Fpu distance = Fpu.Sqrt(((Fpu)hx * hx) + ((Fpu)hy * hy) + (hz * hz));
            if (sight.IsHit && distance.Value >= reached && sight.Transparency <= 0f)
            {
                return;
            }

            int? material = hit.EnterMaterial ?? hit.LeaveMaterial;
            Vector3f n = hit.Normal;
            if (material is { } row && (((Fpu)dz * n.Z) + ((Fpu)dy * n.Y) + ((Fpu)dx * n.X)).Value < 0.0)
            {
                sight.IsHit = true;
                sight.T = (distance / length).F;
                float transparency = sight.Materials[row]?.QMaterial.Transparency ?? 0f;
                sight.Transparency = ((Fpu)sight.Transparency * transparency).F;
                sight.Material = row;
                if (sight.Transparency == 0f)
                {
                    return;
                }
            }

            reached = distance.F;
            Fpu step = (Fpu)Step / length;
            float sy = (step * dy).F, sz = (step * dz).F;
            start = new Vector3d(
                (((Fpu)dx * step) + hit.Position.X).Value,
                ((Fpu)sy + hit.Position.Y).Value,
                ((Fpu)sz + hit.Position.Z).Value);
            hit.IsHit = false;
        }
    }
}

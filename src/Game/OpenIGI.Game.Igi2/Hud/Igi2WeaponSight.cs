using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Hud;

/// <summary>One screen-space segment of a weapon sight.</summary>
/// <param name="X1">The start's horizontal position.</param>
/// <param name="Y1">The start's vertical position.</param>
/// <param name="X2">The end's horizontal position.</param>
/// <param name="Y2">The end's vertical position.</param>
/// <param name="Alpha">The segment's share of the configured opacity.</param>
public readonly record struct Igi2SightLine(float X1, float Y1, float X2, float Y2, float Alpha = 0.8f);

/// <summary>Weapon sights sized by the player's current precision, as weapontype.c WeaponType_StatusDisplayHandler.</summary>
public static class Igi2WeaponSight
{
    /// <summary>The largest sight's segment buffer.</summary>
    public const int MaximumLines = 128;

    /// <summary>Writes the current weapon's sight without allocating a frame's geometry.</summary>
    /// <param name="lines">Space for <see cref="MaximumLines"/> segments.</param>
    /// <param name="sight">The weapon's Sight Display Type.</param>
    /// <param name="precision">The sum of settled weapon and posture precision, in degrees.</param>
    /// <param name="fieldOfView">The view's tangent of the half angle.</param>
    /// <param name="width">The screen width.</param>
    /// <param name="height">The screen height.</param>
    /// <param name="thick">Whether two-pixel lines are selected.</param>
    /// <returns>The segments written.</returns>
    public static int Write(Span<Igi2SightLine> lines, int sight, float precision, float fieldOfView, int width, int height, bool thick)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lines.Length, MaximumLines);
        if (sight is <= 0 or > 8 || width <= 0 || height <= 0 || fieldOfView <= 0f) { return 0; }
        // weapontype.c WeaponType_StatusDisplayHandler: radius, then integer half-thickness and centre.
        Fpu projected = (((Fpu)1.0 / fieldOfView) * height * precision) * 0.013089969754219055;
        float radius = projected.Value >= 1.0 ? projected.F : 1f;
        int halfThickness = thick ? 1 : 0;
        float cx = (((Fpu)width * 0.5) + 1.0 - halfThickness).F;
        float cy = (((Fpu)height * 0.5) + 1.0 - halfThickness).F;
        int count = 0;
        if (sight == 2)
        {
            // Shotgun: short tangential strokes around the precision circle.
            int dots = Math.Clamp(Fpu.ToInt32((Fpu)radius * 6.2831854820251465 * 0.1666666716337204), 1, MaximumLines);
            for (int i = 0; i < dots; i++)
            {
                double angle = i * (Math.Tau / dots);
                float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
                lines[count++] = new Igi2SightLine(cx + (x * radius) - y, cy + (y * radius) + x,
                    cx + (x * radius) + y, cy + (y * radius) - x);
            }
            return count;
        }
        if (sight == 3)
        {
            // SMG: four short arcs, each made from the retail sixteen-point circle segment.
            Fpu projectedArc = ((Fpu)width * 0.01718750037252903) / radius;
            float arc = projectedArc.Value <= 0.7853981852531433 ? projectedArc.F : 0.7853982f;
            for (int side = 0; side < 4; side++)
            {
                double start = (side * 1.5707963705062866) - (arc * 0.5);
                for (int i = 0; i < 15; i++)
                {
                    double a = start + (arc * i / 15.0), b = start + (arc * (i + 1) / 15.0);
                    lines[count++] = new Igi2SightLine(cx + ((float)Math.Cos(a) * radius), cy + ((float)Math.Sin(a) * radius),
                        cx + ((float)Math.Cos(b) * radius), cy + ((float)Math.Sin(b) * radius), 0.2f);
                }
            }
            return count;
        }
        for (int side = 0; side < 4; side++)
        {
            if (sight == 6)
            {
                float corner = ((Fpu)radius * 0.7071059942245483).F;
                Add(lines, ref count, cx, cy, side, corner - 4f, corner + 4f, corner + 4f, corner - 4f);
            }
            else if (sight == 5)
            {
                Add(lines, ref count, cx, cy, side, radius, radius, radius + 8f, radius + 8f);
                Add(lines, ref count, cx, cy, side, radius + 8f, radius + 8f, radius + 8f, radius);
            }
            else
            {
                Add(lines, ref count, cx, cy, side, radius, 0f, radius + 8f, 0f);
            }
        }
        return count;
    }

    private static void Add(Span<Igi2SightLine> lines, ref int count, float cx, float cy, int side, float x1, float y1, float x2, float y2)
    {
        (float sin, float cos) = side switch { 0 => (0f, 1f), 1 => (1f, 0f), 2 => (0f, -1f), _ => (-1f, 0f) };
        lines[count++] = new Igi2SightLine(cx + (cos * x1) - (sin * y1), cy + (sin * x1) + (cos * y1),
            cx + (cos * x2) - (sin * y2), cy + (sin * x2) + (cos * y2));
    }
}

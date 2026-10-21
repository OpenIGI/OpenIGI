using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>Where one of the death solver's nine rays ended up.</summary>
/// <param name="Hit">Whether the ray met anything.</param>
/// <param name="Position">Where it met it, in engine units. Meaningless when <paramref name="Hit"/> is false.</param>
/// <param name="Distance">
/// How the nine results are ranked. The original compares the <c>double</c> at <c>+96</c> of the batched
/// cast's 112-byte result record and keeps the smallest; what that field holds exactly is unread, so the
/// port ranks by distance from the ray's own origin, which is the only reading the surrounding arithmetic
/// supports.
/// </param>
public readonly record struct SoldierDeathRay(bool Hit, Vector3d Position, double Distance);

/// <summary>
/// <c>0x45D5B0</c> — the solver a soldier's death state runs to decide how far the body is allowed to
/// travel while its death clip plays, and therefore where a corpse comes to rest.
/// </summary>
/// <remarks>
/// <para>
/// A death animation carries real root motion: the body pitches forward or sideways over the clip. Played
/// unchecked against a wall that is closer than the clip's stride, it would put the corpse inside the wall.
/// The engine solves it once, at the moment of death, and returns a <b>corrective velocity</b> that the
/// death state's per-tick (<c>0x45C700</c>) adds to the position every tick:
/// </para>
/// <code>
/// human.position += human[+3384]     // three doubles, this function's output
/// </code>
/// <para>
/// <b>The correction is always backwards along the clip's own motion, and never vertical.</b> The
/// arithmetic is what makes that work: over the clip's <c>duration</c> ticks the correction subtracts
/// exactly the part of the stride the obstruction leaves no room for, so the body travels to the
/// obstruction and stops. Nothing here makes a body <em>fall</em> — the drop is the death clip's own root
/// motion plus the ordinary ground query.
/// </para>
/// <para>
/// Every constant is read from the disassembly at instruction level: <c>0x5333B8</c> = 4096.0f (the
/// one-metre extension), <c>0x533430</c> = 2.0833334e-4f (one 4800th, the clip's duration unit),
/// <c>-3276.8f</c> and <c>+1638.4f</c> (the ray heights), and <c>22.0 × 0.017453292</c> (the fan angle).
/// </para>
/// </remarks>
public static class SoldierDeathFall
{
    /// <summary>How far past the clip's own stride the rays are cast — <c>0x5333B8</c>, one metre.</summary>
    /// <remarks>
    /// The direction is rescaled so its length becomes <c>stride + 4096</c>, and that extended length is
    /// what the shortfall is measured against. So a body whose stride would end exactly against a wall is
    /// still corrected: the extra metre is the margin the solver keeps.
    /// </remarks>
    public const double ExtensionInUnits = 4096.0;

    /// <summary>The lowest of the three ray heights, relative to the body's origin — <b>−0.8 m</b>.</summary>
    public const float LowestRayHeight = -3276.8f;

    /// <summary>The step between the three ray heights — <b>0.4 m</b>.</summary>
    public const float RayHeightStep = 1638.4f;

    /// <summary>How many heights the fan is cast at.</summary>
    public const int RayHeights = 3;

    /// <summary>The fan's half-spread in degrees — three rays at −22°, 0° and +22°.</summary>
    public const double RaySpreadInDegrees = 22.0;

    /// <summary>The degrees-to-radians factor, the same single-precision constant the view cones use.</summary>
    public const float DegreesToRadians = 0.017453292f;

    /// <summary>How many rays the fan casts — three heights by three angles.</summary>
    public const int RayCount = 9;

    /// <summary>The clip-duration unit — <c>0x533430</c>, one 4800th of a second.</summary>
    public const float SecondsPerDurationUnit = 0.00020833334f;

    /// <summary>Ticks per second, which the caller multiplies the duration by.</summary>
    public const double TicksPerSecond = 30.0;

    /// <summary>Casts one of the nine rays.</summary>
    /// <param name="from">The ray's origin, in engine units.</param>
    /// <param name="to">The ray's far end, in engine units.</param>
    /// <returns>What it met.</returns>
    public delegate SoldierDeathRay Cast(Vector3d from, Vector3d to);

    /// <summary>Builds the nine rays the solver fans out of a dying body.</summary>
    /// <param name="body">The body's world position, in engine units — <c>Human+32</c>.</param>
    /// <param name="direction">The clip's total root motion, extended and turned into world space.</param>
    /// <returns>Nine <c>(origin, destination)</c> pairs, in the original's own order.</returns>
    /// <remarks>
    /// The order is heights outermost, angles innermost, starting at the lowest height and the leftmost
    /// angle — which is the order the ranking pass then walks, so it decides ties.
    /// </remarks>
    public static IReadOnlyList<(Vector3d From, Vector3d To)> Fan(Vector3d body, Vector3d direction)
    {
        List<(Vector3d, Vector3d)> rays = new(RayCount);
        for (int height = 0; height < RayHeights; height++)
        {
            double lift = LowestRayHeight + (RayHeightStep * height);
            for (int step = -1; step <= 1; step++)
            {
                float angle = (float)(step * RaySpreadInDegrees) * DegreesToRadians;
                Vector3d turned = RotateAboutZ(direction, angle);
                Vector3d from = new(body.X, body.Y, body.Z + lift);
                rays.Add((from, new Vector3d(from.X + turned.X, from.Y + turned.Y, from.Z + turned.Z)));
            }
        }

        return rays;
    }

    /// <summary>
    /// The clip's total root motion, flattened, extended by a metre and turned into world space.
    /// </summary>
    /// <param name="clip">The death clip.</param>
    /// <param name="facing">The body's heading in radians about Z — <c>Human+1292</c>.</param>
    /// <param name="reach">Receives the extended length, which the shortfall is measured against.</param>
    /// <returns>The world-space direction, or a zero vector when the clip does not travel.</returns>
    /// <remarks>
    /// <b>The vertical component is dropped, and it is dropped in an odd way.</b> The first key's Z is
    /// overwritten with zero <em>before</em> the last key is sampled, and the delta's Z is then that zeroed
    /// slot negated — so it is always <c>−0</c>, whatever either key's Z was. Reproduced as written: the
    /// correction the solver returns is purely horizontal, which is consistent with it being a
    /// travel limiter rather than anything to do with falling.
    /// </remarks>
    public static Vector3d Direction(BoneAnimation clip, float facing, out double reach)
    {
        ArgumentNullException.ThrowIfNull(clip);

        reach = 0.0;
        ReadOnlySpan<BoneTranslationKey> root = clip.Translation;
        if (root.Length == 0)
        {
            return default;
        }

        Vector3f first = root[0].Position;
        Vector3f last = root[root.Length - 1].Position;

        double dx = last.X - first.X;
        double dy = last.Y - first.Y;
        const double dz = 0.0;

        double stride = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (stride <= 0.0)
        {
            return default;
        }

        double scale = (stride + ExtensionInUnits) / stride;
        Vector3d local = new(dx * scale, dy * scale, dz * scale);

        // The original re-takes the length of the scaled vector rather than using stride + 4096, and the
        // two differ in the last bits. Reproduced.
        reach = Math.Sqrt((local.X * local.X) + (local.Y * local.Y) + (local.Z * local.Z));

        return RotateAboutZ(local, facing);
    }

    /// <summary>Runs the whole solver.</summary>
    /// <param name="clip">The death clip that is about to play.</param>
    /// <param name="body">The body's world position, in engine units.</param>
    /// <param name="facing">The body's heading in radians about Z.</param>
    /// <param name="cast">The world cast the nine rays go through.</param>
    /// <returns>
    /// The corrective velocity in engine units per tick, to be added to the position every tick while the
    /// clip plays. A zero vector means "the clip may travel unchecked".
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Three ways out give a zero correction, and all three are the original's: the clip does not travel;
    /// none of the nine rays met anything; or the nearest obstruction is <em>further</em> than the extended
    /// reach, in which case there is nothing to correct for.
    /// </para>
    /// <para>
    /// The fourth case is the odd one. When the obstruction is at exactly the ray's own origin — an exact
    /// three-way equality, not a tolerance — the correction is the <b>whole</b> direction, so the body ends
    /// the clip where it started. Everything between scales linearly by how far short the obstruction is.
    /// </para>
    /// </remarks>
    public static Vector3d Solve(BoneAnimation clip, Vector3d body, float facing, Cast cast)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(cast);

        Vector3d direction = Direction(clip, facing, out double reach);
        if (direction is { X: 0.0, Y: 0.0, Z: 0.0 })
        {
            return default;
        }

        IReadOnlyList<(Vector3d From, Vector3d To)> rays = Fan(body, direction);

        int best = -1;
        double bestDistance = 0.0;
        Vector3d hit = default;
        for (int i = 0; i < rays.Count; i++)
        {
            SoldierDeathRay result = cast(rays[i].From, rays[i].To);
            if (!result.Hit)
            {
                continue;
            }

            if (best < 0 || result.Distance < bestDistance)
            {
                best = i;
                bestDistance = result.Distance;
                hit = result.Position;
            }
        }

        if (best < 0)
        {
            return default;
        }

        // Measured from the ray's own ORIGIN back to the hit, horizontally. The origin's Z differs from the
        // body's by the ray height, and the Z term is zeroed here anyway.
        Vector3d origin = rays[best].From;
        double shortX = origin.X - hit.X;
        double shortY = origin.Y - hit.Y;

        double ticks = clip.Duration * (double)SecondsPerDurationUnit * TicksPerSecond;
        if (ticks == 0.0)
        {
            return default;
        }

        // 0x4B3320 is an exact equality on all three components, not a tolerance. Reproduced.
        if (shortX == 0.0 && shortY == 0.0)
        {
            return Scale(direction, -(1.0 / ticks));
        }

        double shortfall = Math.Sqrt((shortX * shortX) + (shortY * shortY));
        if (shortfall > reach)
        {
            return default;
        }

        double fraction = 1.0 - (shortfall / reach);
        return Scale(Scale(direction, fraction), -(1.0 / ticks));
    }

    /// <summary>
    /// Turns a vector about Z, the way <c>0x4B3BE0</c>'s matrix is applied — rows dotted with the vector.
    /// </summary>
    /// <param name="v">The vector.</param>
    /// <param name="radians">The angle.</param>
    /// <returns>The turned vector.</returns>
    /// <remarks>
    /// <c>0x4B3BE0</c> builds <c>{cos, −sin, 0; sin, cos, 0; 0, 0, 1}</c> and the caller applies it as
    /// <c>M·v</c>, which is the engine's <em>other</em> convention from <c>Matrix3f.Transform</c>.
    /// Applying it the wrong way round would place a corpse by the transpose of
    /// its own heading, which is a mirror image, so this is written out rather than routed through the
    /// helper that means the opposite.
    /// </remarks>
    public static Vector3d RotateAboutZ(Vector3d v, float radians)
    {
        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        return new Vector3d((cos * v.X) - (sin * v.Y), (sin * v.X) + (cos * v.Y), v.Z);
    }

    private static Vector3d Scale(Vector3d v, double by) => new(v.X * by, v.Y * by, v.Z * by);
}

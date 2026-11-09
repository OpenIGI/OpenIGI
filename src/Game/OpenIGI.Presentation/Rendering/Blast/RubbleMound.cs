using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// The heap a building brought down leaves on its footprint: a low, ragged mound of broken concrete that rises under the
/// dust as the building comes down, which its slabs then come to rest on.
/// </summary>
/// <remarks>
/// One shape for what is drawn (<see cref="BlastDebris"/>) and what the pieces land on (<see cref="DebrisCollision"/>), so a
/// slab lies on the heap it is seen on. Its height is a smooth dome, broken by two octaves of seeded noise and with a
/// ragged edge, in world units about its middle on the ground.
/// </remarks>
public sealed class RubbleMound
{
    // When it rises, in ticks after it is made: under the dust, as the first slabs come down.
    private const int RiseStartTicks = 6;
    private const int RiseTicks = 45;

    private readonly float _phase0;
    private readonly float _phase1;
    private readonly float _phase2;

    /// <summary>Initializes a new instance of the <see cref="RubbleMound"/> class.</summary>
    /// <param name="groundUnits">Its middle on the ground, world units.</param>
    /// <param name="radiusUnits">How far it spreads, world units.</param>
    /// <param name="heightUnits">How high its crown stands, world units.</param>
    /// <param name="seed">The seed its shape is drawn from.</param>
    public RubbleMound(Vector3d groundUnits, double radiusUnits, double heightUnits, uint seed)
    {
        GroundUnits = groundUnits;
        RadiusUnits = Math.Max(radiusUnits, 1.0);
        HeightUnits = Math.Max(heightUnits, 1.0);
        Seed = seed;
        var random = new IgiRandom(seed ^ 0x5EB81Eu);
        _phase0 = random.NextSingle() * MathF.Tau;
        _phase1 = random.NextSingle() * MathF.Tau;
        _phase2 = random.NextSingle() * MathF.Tau;
    }

    /// <summary>Gets its middle on the ground, world units.</summary>
    public Vector3d GroundUnits { get; }

    /// <summary>Gets how far it spreads, world units.</summary>
    public double RadiusUnits { get; }

    /// <summary>Gets how high its crown stands, world units.</summary>
    public double HeightUnits { get; }

    /// <summary>Gets the seed its shape is drawn from.</summary>
    public uint Seed { get; }

    /// <summary>Gets the ticks since it was made.</summary>
    public int Ticks { get; private set; }

    /// <summary>Steps it one game tick.</summary>
    public void Tick() => Ticks++;

    /// <summary>How far it has risen at this frame, nought to one.</summary>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    /// <returns>The share of its full height.</returns>
    public float Risen(float blend) =>
        BombBlast.Smooth(RiseStartTicks, RiseStartTicks + RiseTicks, Ticks + Math.Clamp(blend, 0f, 1f));

    /// <summary>How ragged its edge is in a direction: how far it reaches that way, as a share of its radius.</summary>
    /// <param name="angle">The direction, radians about its middle.</param>
    /// <returns>The share, about 0.75 to 1.1.</returns>
    public float EdgeAt(float angle) =>
        0.92f + (0.1f * MathF.Sin((3f * angle) + _phase0)) + (0.06f * MathF.Sin((7f * angle) + _phase1)) + (0.04f * MathF.Sin((13f * angle) + _phase2));

    /// <summary>Its full height at a point off its middle, world units; nought past its edge.</summary>
    /// <param name="dx">How far east of its middle, world units.</param>
    /// <param name="dy">How far north.</param>
    /// <returns>The height above the ground.</returns>
    public double HeightAt(double dx, double dy)
    {
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        float angle = MathF.Atan2((float)dy, (float)dx);
        double r = distance / (RadiusUnits * EdgeAt(angle));
        if (r >= 1.0)
        {
            return 0.0;
        }

        double dome = Math.Pow(1.0 - (r * r), 1.4);
        float u = (float)(dx / RadiusUnits);
        float v = (float)(dy / RadiusUnits);
        float lumps = (0.18f * MathF.Sin((u * 7.1f) + _phase0) * MathF.Sin((v * 6.3f) + _phase1))
            + (0.09f * MathF.Sin((u * 15.7f) + _phase2) * MathF.Sin((v * 17.3f) + _phase0));
        return HeightUnits * dome * (1.0 + lumps);
    }

    /// <summary>The height it stands to now at a point, world units, above the ground at its middle.</summary>
    /// <param name="at">The point, world units.</param>
    /// <returns>The ground's height there with the heap risen so far; <see cref="double.NegativeInfinity"/> off it.</returns>
    public double TopAt(Vector3d at)
    {
        double dx = at.X - GroundUnits.X;
        double dy = at.Y - GroundUnits.Y;
        if ((dx * dx) + (dy * dy) > RadiusUnits * RadiusUnits * 1.25)
        {
            return double.NegativeInfinity;
        }

        double h = HeightAt(dx, dy) * Risen(0f);
        return h <= 0.0 ? double.NegativeInfinity : GroundUnits.Z + h;
    }

    /// <summary>Its upward normal at a point, from the slope of its full shape.</summary>
    /// <param name="at">The point, world units.</param>
    /// <returns>The normal.</returns>
    public Vector3d NormalAt(Vector3d at)
    {
        const double Step = 64.0;
        double dx = at.X - GroundUnits.X;
        double dy = at.Y - GroundUnits.Y;
        double rise = Risen(0f);
        double sx = (HeightAt(dx + Step, dy) - HeightAt(dx - Step, dy)) * rise / (2.0 * Step);
        double sy = (HeightAt(dx, dy + Step) - HeightAt(dx, dy - Step)) * rise / (2.0 * Step);
        Vector3d n = new(-sx, -sy, 1.0);
        return n / n.Length;
    }
}

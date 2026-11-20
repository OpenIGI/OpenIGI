using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The dust a bomb's wreckage throws: the surge a building throws as it comes down, and what its rubble raises.</summary>
public sealed partial class ImpactParticleRenderer
{
    /// <summary>Throws the surge of dust a building throws out along the ground as it comes down.</summary>
    /// <param name="groundUnits">Its footprint's middle on the ground, world units.</param>
    /// <param name="footprintMetres">How far its footprint reaches from its middle.</param>
    /// <param name="heightMetres">How tall it stood.</param>
    /// <param name="seed">Its seed.</param>
    /// <param name="intensity">How rich the burst is, <c>r_impact_particles</c>.</param>
    public void SpawnCollapseSurge(Vector3d groundUnits, float footprintMetres, float heightMetres, uint seed, float intensity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (intensity <= 0f)
        {
            return;
        }

        var random = new IgiRandom(seed ^ 0x5D57u);
        float scale = Math.Clamp(intensity, 0f, 2f);
        float f = MathF.Max(footprintMetres, 2f);
        float h = MathF.Max(heightMetres, 3f);

        // Out from the walls along the ground, a ring that rolls and hangs.
        int ring = (int)(64 * scale);
        for (int i = 0; i < ring; i++)
        {
            float angle = ((i + random.NextSingle()) / ring) * MathF.Tau;
            Vector3f outward = new(MathF.Cos(angle), MathF.Sin(angle), 0f);
            float start = f * (0.6f + (random.NextSingle() * 0.4f));
            float shade = 0.62f + (random.NextSingle() * 0.15f);
            Emit(new ImpactParticle
            {
                Position = Offset(groundUnits, outward * (start * UnitsPerMetre), 0.5f + (random.NextSingle() * h * 0.25f)),
                PreviousPosition = Offset(groundUnits, outward * (start * UnitsPerMetre), 0.5f),
                Velocity = new Vector3f(outward.X * PerTick(4f + (random.NextSingle() * 5f)), outward.Y * PerTick(4f + (random.NextSingle() * 5f)), PerTick(0.4f + (random.NextSingle() * 1.2f))),
                LifeTicks = 150 + (int)random.NextBounded(120),
                Size = 1.2f + (random.NextSingle() * f * 0.18f),
                Growth = 0.03f + (random.NextSingle() * 0.03f),
                GravityScale = 0f,
                Drag = 0.95f,
                Red = 0.66f * shade,
                Green = 0.6f * shade,
                Blue = 0.52f * shade,
            });
        }

        // Up out of the middle, as the floors come down on one another.
        int column = (int)(28 * scale);
        for (int i = 0; i < column; i++)
        {
            Vector3f spot = new((random.NextSingle() - 0.5f) * f * UnitsPerMetre, (random.NextSingle() - 0.5f) * f * UnitsPerMetre, 0f);
            float shade = 0.58f + (random.NextSingle() * 0.15f);
            Emit(new ImpactParticle
            {
                Position = Offset(groundUnits, spot, random.NextSingle() * h * 0.6f),
                PreviousPosition = Offset(groundUnits, spot, 0f),
                Velocity = new Vector3f(0f, 0f, PerTick(1.5f + (random.NextSingle() * 3f))),
                LifeTicks = 140 + (int)random.NextBounded(100),
                Size = 1.5f + (random.NextSingle() * 2.5f),
                Growth = 0.04f,
                GravityScale = 0f,
                Drag = 0.96f,
                Red = 0.66f * shade,
                Green = 0.6f * shade,
                Blue = 0.52f * shade,
            });
        }
    }

    /// <summary>Throws the dust and grit a piece of rubble raises where it lands, or sheds as it falls.</summary>
    /// <param name="atUnits">Where, world units.</param>
    /// <param name="sizeMetres">How big the piece is.</param>
    /// <param name="strength">How hard it landed, nought to one.</param>
    /// <param name="intensity">How rich the burst is, <c>r_impact_particles</c>.</param>
    public void SpawnRubbleDust(Vector3d atUnits, float sizeMetres, float strength, float intensity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (intensity <= 0f || strength <= 0f)
        {
            return;
        }

        float size = Math.Clamp(sizeMetres, 0.2f, 4f);
        int puffs = Math.Max(1, (int)MathF.Round((1f + (strength * 5f)) * Math.Clamp(intensity, 0f, 2f)));
        for (int i = 0; i < puffs; i++)
        {
            Vector3f outward = ScatterAround(_random, Vector3f.UnitZ, spread: 1.3f);
            float shade = 0.6f + ((float)_random.NextDouble() * 0.15f);
            float speed = PerTick((0.5f + ((float)_random.NextDouble() * 2.5f)) * strength * 2f);
            Emit(new ImpactParticle
            {
                Position = atUnits,
                PreviousPosition = atUnits,
                Velocity = new Vector3f(outward.X * speed, outward.Y * speed, MathF.Abs(outward.Z) * speed * 0.5f),
                LifeTicks = 60 + (int)(_random.NextDouble() * 70.0),
                Size = size * (0.5f + ((float)_random.NextDouble() * 0.6f)),
                Growth = size * 0.012f,
                GravityScale = 0f,
                Drag = 0.94f,
                Red = 0.66f * shade,
                Green = 0.6f * shade,
                Blue = 0.52f * shade,
            });
        }

        // Grit kicked up by a hard landing.
        int grit = (int)(strength * 6f);
        for (int i = 0; i < grit; i++)
        {
            Vector3f direction = ScatterAround(_random, Vector3f.UnitZ, spread: 0.9f);
            float shade = 0.25f + ((float)_random.NextDouble() * 0.2f);
            Emit(new ImpactParticle
            {
                Position = atUnits,
                PreviousPosition = atUnits,
                Velocity = direction * PerTick(2f + ((float)_random.NextDouble() * 4f)),
                LifeTicks = 20 + (int)(_random.NextDouble() * 20.0),
                Size = 0.04f + ((float)_random.NextDouble() * 0.06f),
                Growth = 0f,
                GravityScale = 1f,
                Drag = 0.99f,
                Red = shade,
                Green = shade * 0.95f,
                Blue = shade * 0.9f,
            });
        }
    }

    // Metres a second as world units a tick.
    private static float PerTick(float metresPerSecond) => metresPerSecond * UnitsPerMetre / 30f;

    // A point beside another, lifted off the ground by some metres.
    private static Vector3d Offset(Vector3d atUnits, Vector3f byUnits, float liftMetres) =>
        new(atUnits.X + byUnits.X, atUnits.Y + byUnits.Y, atUnits.Z + byUnits.Z + (liftMetres * UnitsPerMetre));
}

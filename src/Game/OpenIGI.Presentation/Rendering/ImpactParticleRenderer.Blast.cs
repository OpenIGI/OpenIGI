using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The remastered bomb blast's ground: the dust ring its front rolls out, the embers it throws up, the chips it flings.</summary>
public sealed partial class ImpactParticleRenderer
{
    // How many of each a full blast throws, at r_impact_particles 1.
    private const int BlastDustPuffs = 84;
    private const int BlastEmbers = 150;
    private const int BlastChips = 90;

    /// <summary>Throws the ground's part of a bomb going off.</summary>
    /// <param name="atUnits">Where it went off, in world units, on the ground.</param>
    /// <param name="radiusMetres">How far it kills, the size the rest is scaled by.</param>
    /// <param name="seed">The blast's seed: every machine that shows it throws the same.</param>
    /// <param name="intensity">How rich the burst is, <c>r_impact_particles</c>; nought throws nothing.</param>
    /// <remarks>
    /// <para>
    /// The dust ring is the front lifting the ground as it passes: big soft puffs laid flat round the blast, thrown
    /// outward along the ground and braked hard by the air, so the ring races out and then hangs and spreads -- the base
    /// surge every large charge on open ground throws. The embers are burning fragments thrown high in a cone, falling as
    /// sparks do; the chips are dark grit thrown low and fast.
    /// </para>
    /// <para>
    /// Drawn from the blast's own seed and not the shared stream, so the shot is the same on every client and the
    /// stream the original's effects draw from is not touched.
    /// </para>
    /// </remarks>
    public void SpawnBombBlast(Vector3d atUnits, float radiusMetres, uint seed, float intensity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (intensity <= 0f)
        {
            return;
        }

        var random = new IgiRandom(seed);
        float scale = Math.Clamp(intensity, 0f, 2f);
        float r = MathF.Max(radiusMetres, 1f);

        int puffs = (int)(BlastDustPuffs * scale);
        for (int i = 0; i < puffs; i++)
        {
            float angle = ((i + random.NextSingle()) / puffs) * MathF.Tau;
            float start = r * (0.15f + (random.NextSingle() * 0.25f));
            float speed = PerTick(r * (1.4f + (random.NextSingle() * 1.1f)));
            float shade = 0.42f + (random.NextSingle() * 0.18f);
            Vector3f outward = new(MathF.Cos(angle), MathF.Sin(angle), 0f);
            Emit(new ImpactParticle
            {
                Position = Offset(atUnits, outward * (start * UnitsPerMetre), 0.4f + (random.NextSingle() * 0.8f)),
                PreviousPosition = Offset(atUnits, outward * (start * UnitsPerMetre), 0.4f),
                Velocity = new Vector3f(outward.X * speed, outward.Y * speed, PerTick(0.6f + (random.NextSingle() * 1.4f))),
                LifeTicks = 75 + (int)random.NextBounded(60),
                Size = r * (0.08f + (random.NextSingle() * 0.07f)),
                Growth = r * (0.004f + (random.NextSingle() * 0.004f)),
                GravityScale = 0f,
                Drag = 0.9f,
                Red = 0.62f * shade,
                Green = 0.55f * shade,
                Blue = 0.46f * shade,
            });
        }

        int embers = (int)(BlastEmbers * scale);
        for (int i = 0; i < embers; i++)
        {
            Vector3f direction = ScatterAround(random, Vector3f.UnitZ, spread: 0.75f);
            float speed = PerTick(12f + (random.NextSingle() * 30f));
            float warmth = 0.6f + (random.NextSingle() * 0.4f);
            Emit(new ImpactParticle
            {
                Position = Offset(atUnits, direction * (r * 0.15f * UnitsPerMetre), 0.5f),
                PreviousPosition = atUnits,
                Velocity = direction * speed,
                LifeTicks = 35 + (int)random.NextBounded(70),
                Size = 0.06f + (random.NextSingle() * 0.08f),
                Growth = 0f,
                GravityScale = 0.8f,
                Drag = 0.985f,
                Red = 1f,
                Green = 0.45f + (0.35f * warmth),
                Blue = 0.12f * warmth,
                IsSpark = true,
            });
        }

        int chips = (int)(BlastChips * scale);
        for (int i = 0; i < chips; i++)
        {
            Vector3f direction = ScatterAround(random, Vector3f.UnitZ, spread: 1.4f);
            if (direction.Z < 0.05f)
            {
                direction = new Vector3f(direction.X, direction.Y, 0.05f + MathF.Abs(direction.Z));
            }

            float speed = PerTick(15f + (random.NextSingle() * 35f));
            float shade = 0.12f + (random.NextSingle() * 0.12f);
            Emit(new ImpactParticle
            {
                Position = Offset(atUnits, default, 0.3f),
                PreviousPosition = atUnits,
                Velocity = direction * speed,
                LifeTicks = 30 + (int)random.NextBounded(45),
                Size = 0.08f + (random.NextSingle() * 0.12f),
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

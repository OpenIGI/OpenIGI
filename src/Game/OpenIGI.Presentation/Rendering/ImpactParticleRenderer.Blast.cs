using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The remastered bomb blast's ground and air: the dust ring its front rolls out, the embers and the fingers of smoke it
/// throws up, the chips it flings -- and the dust a building throws as it comes down.
/// </summary>
public sealed partial class ImpactParticleRenderer
{
    // How many of each a full blast throws, at r_impact_particles 1.
    private const int BlastDustPuffs = 96;
    private const int BlastEmbers = 170;
    private const int BlastLateSparks = 70;
    private const int BlastChips = 100;
    private const int BlastTrails = 12;

    // The most smoke fingers in the air at once, whatever is thrown.
    private const int MostBlastTrails = 48;

    private readonly List<BlastTrail> _blastTrails = [];

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
    /// sparks do, and a second, slower wave of them as the fireball breaks; the chips are dark grit thrown low and fast.
    /// The fingers are the heavy burning pieces every large blast throws out of its fireball: each arcs up and over, a
    /// spark at its head, leaving a thickening trail of dark smoke behind it.
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
            float speed = PerTick(r * (1.5f + (random.NextSingle() * 1.2f)));
            float shade = 0.4f + (random.NextSingle() * 0.18f);
            Vector3f outward = new(MathF.Cos(angle), MathF.Sin(angle), 0f);
            Emit(new ImpactParticle
            {
                Position = Offset(atUnits, outward * (start * UnitsPerMetre), 0.4f + (random.NextSingle() * 1.2f)),
                PreviousPosition = Offset(atUnits, outward * (start * UnitsPerMetre), 0.4f),
                Velocity = new Vector3f(outward.X * speed, outward.Y * speed, PerTick(0.8f + (random.NextSingle() * 2.2f))),
                LifeTicks = 120 + (int)random.NextBounded(90),
                Size = r * (0.11f + (random.NextSingle() * 0.1f)),
                Growth = r * (0.005f + (random.NextSingle() * 0.004f)),
                GravityScale = 0f,
                Drag = 0.92f,
                Red = 0.62f * shade,
                Green = 0.55f * shade,
                Blue = 0.46f * shade,
            });
        }

        int embers = (int)(BlastEmbers * scale);
        for (int i = 0; i < embers; i++)
        {
            Vector3f direction = ScatterAround(random, Vector3f.UnitZ, spread: 0.8f);
            float speed = PerTick(14f + (random.NextSingle() * 36f));
            float warmth = 0.6f + (random.NextSingle() * 0.4f);
            Emit(new ImpactParticle
            {
                Position = Offset(atUnits, direction * (r * 0.15f * UnitsPerMetre), 0.5f),
                PreviousPosition = atUnits,
                Velocity = direction * speed,
                LifeTicks = 45 + (int)random.NextBounded(80),
                Size = 0.08f + (random.NextSingle() * 0.1f),
                Growth = 0f,
                GravityScale = 0.8f,
                Drag = 0.985f,
                Red = 1f,
                Green = 0.5f + (0.35f * warmth),
                Blue = 0.15f * warmth,
                IsSpark = true,
            });
        }

        // The second wave: slower sparks out of the fireball's skin as it breaks open, wider and lower.
        int late = (int)(BlastLateSparks * scale);
        for (int i = 0; i < late; i++)
        {
            Vector3f direction = ScatterAround(random, Vector3f.UnitZ, spread: 1.2f);
            float speed = PerTick(5f + (random.NextSingle() * 12f));
            Emit(new ImpactParticle
            {
                Position = Offset(atUnits, direction * (r * 0.45f * UnitsPerMetre), r * 0.35f),
                PreviousPosition = Offset(atUnits, direction * (r * 0.44f * UnitsPerMetre), r * 0.35f),
                Velocity = direction * speed,
                LifeTicks = 30 + (int)random.NextBounded(60),
                Size = 0.05f + (random.NextSingle() * 0.07f),
                Growth = 0f,
                GravityScale = 0.5f,
                Drag = 0.97f,
                Red = 1f,
                Green = 0.42f + (random.NextSingle() * 0.25f),
                Blue = 0.08f,
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
                Size = 0.08f + (random.NextSingle() * 0.14f),
                Growth = 0f,
                GravityScale = 1f,
                Drag = 0.99f,
                Red = shade,
                Green = shade * 0.95f,
                Blue = shade * 0.9f,
            });
        }

        // The fingers: up and out in a wide cone, fast enough to clear the fireball and arc over.
        int trails = Math.Min((int)MathF.Round(BlastTrails * scale), MostBlastTrails - _blastTrails.Count);
        for (int i = 0; i < trails; i++)
        {
            float angle = ((i + (random.NextSingle() * 0.7f)) / Math.Max(trails, 1)) * MathF.Tau;
            float climb = 0.55f + (random.NextSingle() * 0.8f);
            Vector3f direction = new Vector3f(MathF.Cos(angle), MathF.Sin(angle), climb).Normalized();
            float speed = PerTick(11f + (random.NextSingle() * 9f) + MathF.Sqrt(r));
            _blastTrails.Add(new BlastTrail
            {
                Position = Offset(atUnits, direction * (r * 0.25f * UnitsPerMetre), 0.8f),
                Velocity = direction * speed,
                LifeTicks = 26 + (int)random.NextBounded(22),
                Size = r * (0.05f + (random.NextSingle() * 0.03f)),
                Seed = random.NextRaw(),
            });
        }
    }

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

    // Steps every smoke finger: it arcs under gravity, its head glows, and it lays a puff of smoke where it passes.
    private void TickBlastTrails()
    {
        for (int i = _blastTrails.Count - 1; i >= 0; i--)
        {
            BlastTrail trail = _blastTrails[i];
            if (++trail.AgeTicks >= trail.LifeTicks)
            {
                _blastTrails.RemoveAt(i);
                continue;
            }

            Vector3d from = trail.Position;
            trail.Velocity = new Vector3f(trail.Velocity.X * 0.985f, trail.Velocity.Y * 0.985f, (trail.Velocity.Z - (GravityPerTick * 0.6f)) * 0.985f);
            trail.Position = new Vector3d(from.X + trail.Velocity.X, from.Y + trail.Velocity.Y, from.Z + trail.Velocity.Z);
            float life = trail.AgeTicks / (float)trail.LifeTicks;

            // Dark at the head, greying as it hangs; thicker the longer it has burned.
            uint h = unchecked((trail.Seed * 747796405u) + ((uint)trail.AgeTicks * 2891336453u));
            float jitter = ((h >> 8) & 0xFF) / 255f;
            float shade = 0.22f + (0.16f * life) + (0.05f * jitter);
            Emit(new ImpactParticle
            {
                Position = trail.Position,
                PreviousPosition = from,
                Velocity = new Vector3f(trail.Velocity.X * 0.05f, trail.Velocity.Y * 0.05f, PerTick(0.4f)),
                LifeTicks = 70 + (int)(jitter * 60f),
                Size = trail.Size * (0.9f + (life * 1.4f)),
                Growth = trail.Size * 0.025f,
                GravityScale = 0f,
                Drag = 0.95f,
                Red = shade,
                Green = shade * 0.96f,
                Blue = shade * 0.92f,
            });

            // The burning piece at its head, while it still burns.
            if (life < 0.75f)
            {
                Emit(new ImpactParticle
                {
                    Position = trail.Position,
                    PreviousPosition = from,
                    Velocity = trail.Velocity * 0.5f,
                    LifeTicks = 3,
                    Size = 0.18f + (trail.Size * 0.12f),
                    Growth = 0f,
                    GravityScale = 0f,
                    Drag = 1f,
                    Red = 1f,
                    Green = 0.62f - (0.3f * life),
                    Blue = 0.2f - (0.15f * life),
                    IsSpark = true,
                });
            }
        }
    }

    // Metres a second as world units a tick.
    private static float PerTick(float metresPerSecond) => metresPerSecond * UnitsPerMetre / 30f;

    // A point beside another, lifted off the ground by some metres.
    private static Vector3d Offset(Vector3d atUnits, Vector3f byUnits, float liftMetres) =>
        new(atUnits.X + byUnits.X, atUnits.Y + byUnits.Y, atUnits.Z + byUnits.Z + (liftMetres * UnitsPerMetre));

    // One smoke finger in the air.
    private sealed class BlastTrail
    {
        public Vector3d Position { get; set; }

        public Vector3f Velocity { get; set; }

        public int AgeTicks { get; set; }

        public int LifeTicks { get; init; }

        public float Size { get; init; }

        public uint Seed { get; init; }
    }
}

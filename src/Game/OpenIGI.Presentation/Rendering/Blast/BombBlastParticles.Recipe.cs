using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>The bomb's blast as it is thrown: what goes where, how fast, how big and for how long.</summary>
public sealed partial class BombBlastParticles
{
    private const float TicksPerSecond = 30f;

    // Gravity, metres a second squared.
    private const float Gravity = 9.8f;

    /// <summary>Sets a bomb's blast off.</summary>
    /// <param name="atUnits">Where it went off, on the ground, in world units.</param>
    /// <param name="radiusMetres">How far it kills, which scales the rest.</param>
    /// <param name="seed">The blast's seed: every machine throws the same.</param>
    /// <param name="light">Whether to throw the light tier's third, for the browser and phones.</param>
    /// <remarks>
    /// <para>
    /// The moment it goes off: a flash wider than the street, seen through walls for a fraction of a second, and a horizontal
    /// streak across it; a knot of fire tongues swelling out of the ground and cooling to red within the second; burning
    /// pieces thrown high and out on ropes of smoke; embers and fire trails; dark grit thrown low and fast.
    /// </para>
    /// <para>
    /// After it: the smoke of the fireball rising in three billows, a cap thrown up ahead of them, two plumes shot straight up,
    /// a ring of smoke pushed out sideways and a lower one of dust rolled out along the ground; and for ten seconds a column
    /// feeding the cloud from below and a smoulder of wisps and ash where it stood -- all of it leaning with one wind.
    /// </para>
    /// </remarks>
    public void Start(Vector3d atUnits, float radiusMetres, uint seed, bool light)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var random = new IgiRandom(seed ^ 0xB1A57u);
        float scale = Math.Clamp(radiusMetres / 40f, 0.75f, 1.5f);
        float share = light ? 0.35f : 1f;

        // One wind for the whole cloud, its way drawn from the seed.
        float windAngle = random.NextSingle() * MathF.Tau;
        Vector3f wind = new(MathF.Cos(windAngle), MathF.Sin(windAngle), 0f);
        Recipe r = new(this, random, atUnits, scale, share, wind);

        r.Flash();
        r.Fireball();
        r.Smoke();
        r.Ground();
        r.Thrown(light);
        _emitters.Add(new BlastEmitter(BlastEmitterKind.Column, atUnits, default, seed ^ 0xC01u, 10f * TicksPerSecond, scale, share, wind));
        _emitters.Add(new BlastEmitter(BlastEmitterKind.Smoulder, atUnits, default, seed ^ 0x5310u, 10f * TicksPerSecond, scale, share, wind));
    }

    /// <summary>Throws the dust a building throws up as it comes down: a column out of its footprint and a surge along the ground.</summary>
    /// <param name="groundUnits">The footprint's middle on the ground, world units.</param>
    /// <param name="footprintMetres">How far the footprint reaches from its middle.</param>
    /// <param name="heightMetres">How tall the building stood.</param>
    /// <param name="seed">Its seed.</param>
    /// <param name="light">Whether to throw the light tier's third.</param>
    public void StartCollapse(Vector3d groundUnits, float footprintMetres, float heightMetres, uint seed, bool light)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var random = new IgiRandom(seed ^ 0xD057u);
        float f = MathF.Max(footprintMetres, 2f);
        float h = MathF.Max(heightMetres, 3f);
        int billows = Count(14 + (int)(f * 1.2f), light ? 0.35f : 1f);
        for (int i = 0; i < billows; i++)
        {
            float angle = random.NextSingle() * MathF.Tau;
            float reach = f * MathF.Sqrt(random.NextSingle()) * 0.8f;
            Vector3d at = Offset(groundUnits, new Vector3f(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach, random.NextSingle() * h * 0.5f));
            float shade = 0.56f + (random.NextSingle() * 0.12f);
            float size = (f * 0.35f) + (random.NextSingle() * f * 0.4f);
            Emit(new BlastSprite
            {
                Position = at,
                Previous = at,
                Velocity = PerTick(new Vector3f(MathF.Cos(angle) * 2f, MathF.Sin(angle) * 2f, 1f + (random.NextSingle() * 2.5f))),
                Push = PerTickSquared(new Vector3f(0f, 0f, 0.3f)),
                Keep = 0.975f,
                Age = -(int)(random.NextSingle() * 18f),
                Life = Ticks(7f + (random.NextSingle() * 5f)),
                SizeFrom = size * 0.4f,
                SizeTo = size * 1.6f,
                Swell = 3f,
                Aspect = 1f,
                Turn = random.NextSingle() * MathF.Tau,
                Spin = (random.NextSingle() - 0.5f) * 0.01f,
                Red = 0.7f * shade,
                Green = 0.64f * shade,
                Blue = 0.56f * shade,
                Strength = 0.85f,
                FadeIn = 0.06f,
                FadeOut = 0.5f,
                Variant = (byte)random.NextBounded(4),
                Look = BlastSpriteLook.Smoke,
            });
        }
    }

    private static int Count(int count, float share) => Math.Max(1, (int)MathF.Round(count * share));

    private static int Ticks(float seconds) => Math.Max(1, (int)MathF.Round(seconds * TicksPerSecond));

    private static Vector3f PerTick(Vector3f metresPerSecond) => metresPerSecond * (UnitsPerMetre / TicksPerSecond);

    private static Vector3f PerTickSquared(Vector3f metresPerSecondSquared) =>
        metresPerSecondSquared * (UnitsPerMetre / (TicksPerSecond * TicksPerSecond));

    private static Vector3d Offset(Vector3d atUnits, Vector3f byMetres) =>
        new(atUnits.X + (byMetres.X * UnitsPerMetre), atUnits.Y + (byMetres.Y * UnitsPerMetre), atUnits.Z + (byMetres.Z * UnitsPerMetre));

    // How much speed a sprite keeps a tick, from how much it keeps a second.
    private static float KeepPerTick(float perSecond) => MathF.Pow(perSecond, 1f / TicksPerSecond);

    // A direction in the upper half, its spread from straight up set by how much its rise is weighted.
    private static Vector3f Upward(IgiRandom random, float rise)
    {
        float angle = random.NextSingle() * MathF.Tau;
        float z = MathF.Pow(random.NextSingle(), 1f / MathF.Max(rise, 0.1f));
        float flat = MathF.Sqrt(MathF.Max(0f, 1f - (z * z)));
        return new Vector3f(MathF.Cos(angle) * flat, MathF.Sin(angle) * flat, z);
    }

    // One blast's throw, with what every part of it shares.
    private readonly struct Recipe(BombBlastParticles into, IgiRandom random, Vector3d at, float scale, float share, Vector3f wind)
    {
        // The wind's push on the smoke, metres a second squared.
        private const float WindPush = 0.45f;

        public void Flash()
        {
            // The core: wider than the street, warm white, gone in a second and a half.
            Sprite(BlastSpriteLook.Flare, Offset(at, new Vector3f(0f, 0f, 0.8f * scale)), default, 1.6f, 34f * scale, 46f * scale, 1f, 1f, 0.85f, 0.65f, 1f, 0f, 0.85f);
            Sprite(BlastSpriteLook.Flare, Offset(at, new Vector3f(0f, 0f, 1.2f * scale)), default, 0.9f, 12f * scale, 14f * scale, 1f, 1f, 0.9f, 0.75f, 0.9f, 0f, 0.9f);

            // The streak across it, the eye's own glare: nine times as wide as it is tall.
            Sprite(BlastSpriteLook.Flare, Offset(at, new Vector3f(0f, 0f, 1f * scale)), default, 0.5f, 48f * scale, 52f * scale, 8f, 1f, 0.82f, 0.62f, 0.55f, 0f, 1f, aspect: 9f);
        }

        public void Fireball()
        {
            // Tongues of fire swelling out of the ground, cooling from white to a dull red within the second.
            int tongues = Count(14, Math.Max(share, 0.6f));
            for (int i = 0; i < tongues; i++)
            {
                Vector3f way = Upward(random, 1.6f);
                float speed = (4f + (random.NextSingle() * 8f)) * scale;
                float size = (6f + (random.NextSingle() * 6f)) * scale;
                Sprite(
                    BlastSpriteLook.Fire,
                    Offset(at, (way * (random.NextSingle() * 1.5f * scale)) + new Vector3f(0f, 0f, 0.5f)),
                    PerTick(way * speed),
                    0.8f + (random.NextSingle() * 0.7f),
                    size * 0.35f,
                    size * 1.4f,
                    4f,
                    1f,
                    0.9f,
                    0.7f,
                    1f,
                    0.02f,
                    0.55f,
                    keep: KeepPerTick(0.1f),
                    push: new Vector3f(0f, 0f, 5f),
                    spin: (random.NextSingle() - 0.5f) * 0.08f,
                    delay: random.NextSingle() * 0.06f);
            }
        }

        public void Smoke()
        {
            // The fireball's own smoke: billows climbing out of it a beat apart, stacked into a column, hot inside at first.
            for (int i = 0; i < 6; i++)
            {
                Vector3f way = Upward(random, 3f);
                float size = (7f + (random.NextSingle() * 5f)) * scale * (1f - (i * 0.06f));
                Billow(
                    Offset(at, new Vector3f((random.NextSingle() - 0.5f) * 2f, (random.NextSingle() - 0.5f) * 2f, (1f + (i * 1.5f)) * scale)),
                    (way * (2f + (random.NextSingle() * 2f))) + new Vector3f(0f, 0f, (5f + (random.NextSingle() * 4f) + (i * 1.2f)) * scale),
                    10f + (random.NextSingle() * 3f),
                    size * 0.35f,
                    size,
                    0.7f,
                    1f,
                    heat: 1f - (i * 0.1f),
                    lift: 1.6f,
                    keep: 0.45f,
                    delay: i * 0.05f);
            }

            // The cap thrown up ahead of them, and two smaller ones beside it, braking as they climb.
            for (int i = 0; i < 3; i++)
            {
                float size = (i == 0 ? 21f : 13f + (random.NextSingle() * 4f)) * scale;
                float side = i == 0 ? 0f : (i == 1 ? -1f : 1f) * 3.5f * scale;
                Billow(
                    Offset(at, new Vector3f(side * wind.Y, side * -wind.X, (3f + (random.NextSingle() * 2.5f)) * scale)),
                    new Vector3f((random.NextSingle() - 0.5f) * 1.5f, (random.NextSingle() - 0.5f) * 1.5f, (13f + (random.NextSingle() * 4f)) * scale),
                    12f + (random.NextSingle() * 2f),
                    size * 0.33f,
                    size,
                    0.72f,
                    1f,
                    heat: 0.7f,
                    lift: 1.9f,
                    keep: 0.25f,
                    delay: 0.075f + (i * 0.04f));
            }

            // Two plumes shot straight up, tall and thin.
            for (int i = 0; i < 2; i++)
            {
                float size = (2.5f + (random.NextSingle() * 1.2f)) * scale;
                Billow(
                    Offset(at, new Vector3f((random.NextSingle() - 0.5f) * 2f, (random.NextSingle() - 0.5f) * 2f, 1f)),
                    new Vector3f((random.NextSingle() - 0.5f) * 2f, (random.NextSingle() - 0.5f) * 2f, (11f + (random.NextSingle() * 5f)) * scale),
                    3f + random.NextSingle(),
                    size,
                    size * 2.2f,
                    0.66f,
                    1f,
                    heat: 1f,
                    lift: 0f,
                    keep: 0.35f,
                    delay: 0.025f + (i * 0.05f),
                    aspect: 3.3f);
            }

            // A ring of smoke pushed out sideways.
            int ring = Count(6, Math.Max(share, 0.5f));
            for (int i = 0; i < ring; i++)
            {
                float angle = ((i + random.NextSingle()) / ring) * MathF.Tau;
                float speed = (6f + (random.NextSingle() * 3f)) * scale;
                float size = (4f + (random.NextSingle() * 3.5f)) * scale;
                Billow(
                    Offset(at, new Vector3f(0f, 0f, 0.8f)),
                    new Vector3f(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, 1f + (random.NextSingle() * 1.5f)),
                    8f + (random.NextSingle() * 4f),
                    size * 0.33f,
                    size * 1.25f,
                    0.74f,
                    0.95f,
                    heat: 0.6f,
                    lift: 0.6f,
                    keep: 0.3f,
                    delay: 0f);
            }
        }

        public void Ground()
        {
            // A low ring of dust rolled out along the ground, fast at first and hanging after.
            int puffs = Count(40, share);
            for (int i = 0; i < puffs; i++)
            {
                float angle = ((i + random.NextSingle()) / puffs) * MathF.Tau;
                float start = (1f + (random.NextSingle() * 2f)) * scale;
                float speed = (6f + (random.NextSingle() * 9f)) * scale;
                float size = (1.4f + (random.NextSingle() * 1.4f)) * scale;
                float shade = 0.62f + (random.NextSingle() * 0.12f);
                into.Emit(new BlastSprite
                {
                    Position = Offset(at, new Vector3f(MathF.Cos(angle) * start, MathF.Sin(angle) * start, 0.3f + (random.NextSingle() * 0.5f))),
                    Previous = at,
                    Velocity = PerTick(new Vector3f(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, 0.3f + (random.NextSingle() * 0.8f))),
                    Push = PerTickSquared((wind * WindPush * 0.5f) + new Vector3f(0f, 0f, 0.1f)),
                    Keep = KeepPerTick(0.12f),
                    Age = -(int)(random.NextSingle() * 3f),
                    Life = Ticks(2.5f + (random.NextSingle() * 4f)),
                    SizeFrom = size * 0.4f,
                    SizeTo = size * 2.6f,
                    Swell = 2.5f,
                    Aspect = 1f,
                    Turn = random.NextSingle() * MathF.Tau,
                    Spin = (random.NextSingle() - 0.5f) * 0.02f,
                    Red = 0.72f * shade,
                    Green = 0.66f * shade,
                    Blue = 0.58f * shade,
                    Strength = 0.8f,
                    FadeIn = 0.02f,
                    FadeOut = 0.6f,
                    Variant = (byte)random.NextBounded(4),
                    Look = BlastSpriteLook.Smoke,
                });
            }
        }

        public void Thrown(bool light)
        {
            // Embers: burning fragments thrown high, falling as they cool.
            int embers = Count(60, share);
            for (int i = 0; i < embers; i++)
            {
                Vector3f way = Upward(random, 1.4f);
                float speed = (14f + (random.NextSingle() * 40f)) * scale;
                into.Emit(new BlastSprite
                {
                    Position = Offset(at, new Vector3f(0f, 0f, 0.5f)),
                    Previous = at,
                    Velocity = PerTick(way * speed),
                    Push = PerTickSquared(new Vector3f(0f, 0f, -Gravity * 0.5f)),
                    Keep = KeepPerTick(0.35f),
                    Life = Ticks(1.2f + (random.NextSingle() * 2.8f)),
                    SizeFrom = 0.12f + (random.NextSingle() * 0.12f),
                    SizeTo = 0.02f,
                    Swell = 1f,
                    Aspect = 1f,
                    Red = 1f,
                    Green = 0.55f + (random.NextSingle() * 0.25f),
                    Blue = 0.25f,
                    Strength = 1f,
                    FadeOut = 0.4f,
                    Streak = 1.2f,
                    Look = BlastSpriteLook.Ember,
                });
            }

            // Fire trails: a few bright streaks shot up and out in the first instant.
            int trails = Count(16, share);
            for (int i = 0; i < trails; i++)
            {
                Vector3f way = Upward(random, 2.5f);
                float speed = (38f + (random.NextSingle() * 14f)) * scale;
                into.Emit(new BlastSprite
                {
                    Position = Offset(at, new Vector3f(0f, 0f, 0.5f)),
                    Previous = at,
                    Velocity = PerTick(way * speed),
                    Push = PerTickSquared(new Vector3f(0f, 0f, -Gravity * 0.3f)),
                    Keep = KeepPerTick(0.15f),
                    Age = -2,
                    Life = Ticks(0.8f + (random.NextSingle() * 0.8f)),
                    SizeFrom = 0.35f,
                    SizeTo = 0.05f,
                    Swell = 1f,
                    Aspect = 1f,
                    Red = 1f,
                    Green = 0.62f,
                    Blue = 0.38f,
                    Strength = 1f,
                    FadeOut = 0.5f,
                    Streak = 2.2f,
                    Look = BlastSpriteLook.Ember,
                });
            }

            // Grit: dark chips of what it stood on, thrown low and fast.
            int grit = Count(90, share);
            for (int i = 0; i < grit; i++)
            {
                Vector3f way = Upward(random, 0.7f);
                float speed = (15f + (random.NextSingle() * 35f)) * scale;
                float shade = 0.12f + (random.NextSingle() * 0.15f);
                into.Emit(new BlastSprite
                {
                    Position = Offset(at, new Vector3f(0f, 0f, 0.3f)),
                    Previous = at,
                    Velocity = PerTick(way * speed),
                    Push = PerTickSquared(new Vector3f(0f, 0f, -Gravity)),
                    Keep = KeepPerTick(0.6f),
                    Life = Ticks(1.5f + random.NextSingle()),
                    SizeFrom = 0.06f + (random.NextSingle() * 0.1f),
                    SizeTo = 0.04f,
                    Swell = 1f,
                    Aspect = 1f,
                    Red = shade,
                    Green = shade * 0.95f,
                    Blue = shade * 0.9f,
                    Strength = 0.95f,
                    FadeOut = 0.2f,
                    Streak = 0.6f,
                    Look = BlastSpriteLook.Grit,
                });
            }

            // Burning pieces lobbed high and out, each trailing a rope of smoke.
            int ropes = light ? 4 : 12;
            for (int i = 0; i < ropes; i++)
            {
                float angle = ((i + random.NextSingle()) / ropes) * MathF.Tau;
                float outward = (8f + (random.NextSingle() * 22f)) * scale;
                Vector3f velocity = new(MathF.Cos(angle) * outward, MathF.Sin(angle) * outward, (15f + (random.NextSingle() * 25f)) * scale);
                into._emitters.Add(new BlastEmitter(
                    BlastEmitterKind.Rope, Offset(at, new Vector3f(0f, 0f, 1f)), PerTick(velocity), random.NextRaw(), 3f * TicksPerSecond, scale, share, wind));
            }

            // Spark heads: a few bright pieces spitting sparks for half a second as they fly.
            int sparklers = light ? 2 : 5;
            for (int i = 0; i < sparklers; i++)
            {
                Vector3f way = Upward(random, 1.2f);
                float speed = (10f + (random.NextSingle() * 20f)) * scale;
                into._emitters.Add(new BlastEmitter(
                    BlastEmitterKind.Sparkler, Offset(at, new Vector3f(0f, 0f, 1f)), PerTick(way * speed), random.NextRaw(), 0.6f * TicksPerSecond, scale, share, wind));
            }
        }

        private void Billow(
            Vector3d position,
            Vector3f metresPerSecond,
            float lifeSeconds,
            float sizeFrom,
            float sizeTo,
            float shade,
            float strength,
            float heat,
            float lift,
            float keep,
            float delay,
            float aspect = 1f)
        {
            float tone = shade * (0.92f + (random.NextSingle() * 0.12f));
            into.Emit(new BlastSprite
            {
                Position = position,
                Previous = position,
                Velocity = PerTick(metresPerSecond),
                Push = PerTickSquared((wind * WindPush) + new Vector3f(0f, 0f, lift)),
                Keep = KeepPerTick(keep),
                Age = -(int)MathF.Round(delay * TicksPerSecond),
                Life = Ticks(lifeSeconds),
                SizeFrom = sizeFrom,
                SizeTo = sizeTo,
                Swell = 4.5f,
                Aspect = aspect,
                Turn = random.NextSingle() * MathF.Tau,
                Spin = (random.NextSingle() - 0.5f) * 0.012f,
                Red = tone,
                Green = tone * 0.98f,
                Blue = tone * 0.95f,
                Strength = strength,
                FadeIn = 0.02f,
                FadeOut = 0.45f,
                Heat = heat,
                HeatLife = 1.4f / lifeSeconds,
                Variant = (byte)random.NextBounded(4),
                Look = BlastSpriteLook.Smoke,
            });
        }

        private void Sprite(
            BlastSpriteLook look,
            Vector3d position,
            Vector3f velocity,
            float lifeSeconds,
            float sizeFrom,
            float sizeTo,
            float swell,
            float red,
            float green,
            float blue,
            float strength,
            float fadeIn,
            float fadeOut,
            float keep = 1f,
            Vector3f push = default,
            float spin = 0f,
            float delay = 0f,
            float aspect = 1f)
        {
            into.Emit(new BlastSprite
            {
                Position = position,
                Previous = position,
                Velocity = velocity,
                Push = PerTickSquared(push),
                Keep = keep,
                Age = -(int)MathF.Round(delay * TicksPerSecond),
                Life = Ticks(lifeSeconds),
                SizeFrom = sizeFrom,
                SizeTo = sizeTo,
                Swell = swell,
                Aspect = 1f / aspect,
                Turn = MathF.Abs(aspect - 1f) < 1e-3f ? random.NextSingle() * MathF.Tau : 0f,
                Spin = spin,
                Red = red,
                Green = green,
                Blue = blue,
                Strength = strength,
                FadeIn = fadeIn,
                FadeOut = fadeOut,
                Variant = (byte)random.NextBounded(4),
                Look = look,
            });
        }
    }
}

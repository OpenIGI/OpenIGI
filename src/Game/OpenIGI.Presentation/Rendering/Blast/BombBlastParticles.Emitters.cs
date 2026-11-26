using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>What keeps throwing sprites after the blast's first moment.</summary>
internal enum BlastEmitterKind : byte
{
    /// <summary>A burning piece in flight, trailing a rope of smoke and burning at its head.</summary>
    Rope,

    /// <summary>A bright piece in flight, spitting sparks.</summary>
    Sparkler,

    /// <summary>The column feeding the cloud from below for its first seconds.</summary>
    Column,

    /// <summary>The smoulder where the bomb stood: low wisps and specks of ash.</summary>
    Smoulder,
}

/// <summary>One thing still throwing a blast's sprites, stepped on the game's tick.</summary>
/// <param name="kind">What it is.</param>
/// <param name="position">Where it is, in world units.</param>
/// <param name="velocity">How far it moves a tick, in world units; nought for one that stays put.</param>
/// <param name="seed">Its own seed.</param>
/// <param name="lifeTicks">How long it throws.</param>
/// <param name="scale">The blast's size against the usual.</param>
/// <param name="share">How much of the usual it throws.</param>
/// <param name="wind">The blast's wind, flat.</param>
internal sealed class BlastEmitter(
    BlastEmitterKind kind, Vector3d position, Vector3f velocity, uint seed, float lifeTicks, float scale, float share, Vector3f wind)
{
    public BlastEmitterKind Kind { get; } = kind;

    public Vector3d Position { get; set; } = position;

    public Vector3f Velocity { get; set; } = velocity;

    public IgiRandom Random { get; } = new(seed);

    public int Age { get; set; }

    public int Life { get; } = Math.Max(1, (int)lifeTicks);

    public float Scale { get; } = scale;

    public float Share { get; } = share;

    public Vector3f Wind { get; } = wind;

    // Sprites owed but not yet thrown, for rates below one a tick.
    public float Owed { get; set; }
}

/// <summary>The blast's emitters at their work.</summary>
public sealed partial class BombBlastParticles
{
    // Steps one emitter, throwing what it throws this tick; false once it is done.
    private bool StepEmitter(BlastEmitter e)
    {
        if (++e.Age > e.Life)
        {
            return false;
        }

        float lived = e.Age / (float)e.Life;
        switch (e.Kind)
        {
            case BlastEmitterKind.Rope:
                Fly(e, gravity: 0.6f, keepPerSecond: 0.55f);
                RopePuff(e, lived);
                break;
            case BlastEmitterKind.Sparkler:
                Fly(e, gravity: 1.3f, keepPerSecond: 0.7f);
                Sparks(e, lived);
                break;
            case BlastEmitterKind.Column:
                Column(e, lived);
                break;
            case BlastEmitterKind.Smoulder:
                Smoulder(e, lived);
                break;
        }

        return true;
    }

    private static void Fly(BlastEmitter e, float gravity, float keepPerSecond)
    {
        float keep = KeepPerTick(keepPerSecond);
        Vector3f pull = PerTickSquared(new Vector3f(0f, 0f, -Gravity * gravity));
        e.Velocity = (e.Velocity + pull) * keep;
        e.Position = new Vector3d(e.Position.X + e.Velocity.X, e.Position.Y + e.Velocity.Y, e.Position.Z + e.Velocity.Z);
    }

    // A puff of the rope where the piece passes, thick where it started and thinning; and the fire at its head while it burns.
    private void RopePuff(BlastEmitter e, float lived)
    {
        IgiRandom r = e.Random;
        if ((e.Age & 1) == 0 || e.Share >= 0.99f)
        {
            float size = (0.5f + (r.NextSingle() * 0.7f)) * e.Scale * (1.6f - lived);
            float shade = 0.3f + (0.3f * lived) + (r.NextSingle() * 0.08f);
            Emit(new BlastSprite
            {
                Position = e.Position,
                Previous = e.Position,
                Velocity = e.Velocity * 0.04f,
                Push = PerTickSquared((e.Wind * 0.45f) + new Vector3f(0f, 0f, 0.6f)),
                Keep = KeepPerTick(0.3f),
                Life = Ticks(5f + (r.NextSingle() * 3.5f)),
                SizeFrom = size,
                SizeTo = size * 3.2f,
                Swell = 2.5f,
                Aspect = 1f,
                Turn = r.NextSingle() * MathF.Tau,
                Spin = (r.NextSingle() - 0.5f) * 0.02f,
                Red = shade,
                Green = shade * 0.97f,
                Blue = shade * 0.93f,
                Strength = 0.75f * (1f - (0.5f * lived)),
                FadeIn = 0.03f,
                FadeOut = 0.6f,
                Heat = lived < 0.5f ? 0.5f : 0f,
                HeatLife = 0.05f,
                Variant = (byte)r.NextBounded(4),
                Look = BlastSpriteLook.Smoke,
            });
        }

        if (lived < 0.8f)
        {
            Emit(new BlastSprite
            {
                Position = e.Position,
                Previous = e.Position,
                Velocity = e.Velocity * 0.2f,
                Keep = 1f,
                Life = 3,
                SizeFrom = (1.1f - lived) * 0.9f * e.Scale,
                SizeTo = (1.1f - lived) * 0.6f * e.Scale,
                Swell = 1f,
                Aspect = 1f,
                Turn = r.NextSingle() * MathF.Tau,
                Red = 1f,
                Green = 0.8f,
                Blue = 0.55f,
                Strength = 1f - lived,
                FadeOut = 0.6f,
                Variant = (byte)r.NextBounded(4),
                Look = BlastSpriteLook.Fire,
            });
        }
    }

    // A spray of short sparks, thick at first and thinning.
    private void Sparks(BlastEmitter e, float lived)
    {
        IgiRandom r = e.Random;
        int count = Count((int)(8f * (1f - lived)) + 1, e.Share);
        for (int i = 0; i < count; i++)
        {
            Vector3f jitter = new((r.NextSingle() - 0.5f) * 3f, (r.NextSingle() - 0.5f) * 3f, (r.NextSingle() - 0.5f) * 3f);
            Emit(new BlastSprite
            {
                Position = e.Position,
                Previous = e.Position,
                Velocity = (e.Velocity * 0.5f) + PerTick(jitter),
                Push = PerTickSquared(new Vector3f(0f, 0f, -Gravity * 1.3f)),
                Keep = KeepPerTick(0.5f),
                Life = Ticks(0.15f + (r.NextSingle() * 0.35f)),
                SizeFrom = 0.06f,
                SizeTo = 0.01f,
                Swell = 1f,
                Aspect = 1f,
                Red = 1f,
                Green = 0.78f,
                Blue = 0.45f,
                Strength = 1f,
                FadeOut = 0.5f,
                Streak = 0.8f,
                Look = BlastSpriteLook.Ember,
            });
        }
    }

    // The column feeding the cloud from below: a slow pulse of billows rising out of the crater, two a second.
    private void Column(BlastEmitter e, float lived)
    {
        IgiRandom r = e.Random;
        e.Owed += 2.5f / TicksPerSecond;
        while (e.Owed >= 1f)
        {
            e.Owed -= 1f;
            float size = (3.5f + (r.NextSingle() * 3f)) * e.Scale;
            float shade = 0.5f + (r.NextSingle() * 0.25f);
            Vector3d at = Offset(e.Position, new Vector3f((r.NextSingle() - 0.5f) * 5f, (r.NextSingle() - 0.5f) * 5f, -0.3f));
            Emit(new BlastSprite
            {
                Position = at,
                Previous = at,
                Velocity = PerTick(new Vector3f((r.NextSingle() - 0.5f) * 1.2f, (r.NextSingle() - 0.5f) * 1.2f, 3f + (r.NextSingle() * 2f))),
                Push = PerTickSquared((e.Wind * 0.5f) + new Vector3f(0f, 0f, 1.4f)),
                Keep = KeepPerTick(0.6f),
                Life = Ticks(7f + (r.NextSingle() * 2.5f)),
                SizeFrom = size * 0.17f,
                SizeTo = size,
                Swell = 2f,
                Aspect = 1f,
                Turn = r.NextSingle() * MathF.Tau,
                Spin = (r.NextSingle() - 0.5f) * 0.01f,
                Red = shade,
                Green = shade * 0.98f,
                Blue = shade * 0.95f,
                Strength = (0.75f + (r.NextSingle() * 0.12f)) * (1f - (0.45f * lived)),
                FadeIn = 0.25f,
                FadeOut = 0.3f,
                Variant = (byte)r.NextBounded(4),
                Look = BlastSpriteLook.Smoke,
            });
        }
    }

    // The smoulder where the bomb stood: low wisps curling off the scorched ground, and specks of ash drifting over it.
    private void Smoulder(BlastEmitter e, float lived)
    {
        IgiRandom r = e.Random;
        e.Owed += 6f * e.Share / TicksPerSecond;
        while (e.Owed >= 1f)
        {
            e.Owed -= 1f;
            float size = (0.4f + (r.NextSingle() * 0.5f)) * e.Scale;
            float shade = 0.15f + (r.NextSingle() * 0.3f);
            Vector3d at = Offset(e.Position, new Vector3f((r.NextSingle() - 0.5f) * 2.6f * e.Scale, (r.NextSingle() - 0.5f) * 2.6f * e.Scale, 0.1f));
            Emit(new BlastSprite
            {
                Position = at,
                Previous = at,
                Velocity = PerTick(new Vector3f((r.NextSingle() - 0.5f) * 0.6f, (r.NextSingle() - 0.5f) * 0.6f, 0.2f + (r.NextSingle() * 0.8f))),
                Push = PerTickSquared((e.Wind * 0.3f) + new Vector3f(0f, 0f, 0.2f)),
                Keep = KeepPerTick(0.7f),
                Life = Ticks(2f + (r.NextSingle() * 2f)),
                SizeFrom = size * 0.5f,
                SizeTo = size * 2.5f,
                Swell = 1.6f,
                Aspect = 1f,
                Turn = r.NextSingle() * MathF.Tau,
                Spin = (r.NextSingle() - 0.5f) * 0.04f,
                Red = shade,
                Green = shade,
                Blue = shade * 1.04f,
                Strength = 0.55f * (1f - (0.7f * lived)),
                FadeIn = 0.25f,
                FadeOut = 0.5f,
                Variant = (byte)r.NextBounded(4),
                Look = BlastSpriteLook.Smoke,
            });
        }

        if (e.Share >= 0.99f && (e.Age % 3) == 0)
        {
            Vector3d speck = Offset(e.Position, new Vector3f((r.NextSingle() - 0.5f) * 5f * e.Scale, (r.NextSingle() - 0.5f) * 5f * e.Scale, 0.3f + (r.NextSingle() * 4f)));
            float shade = 0.06f + (r.NextSingle() * 0.25f);
            Emit(new BlastSprite
            {
                Position = speck,
                Previous = speck,
                Velocity = PerTick(new Vector3f((r.NextSingle() - 0.5f) * 1.2f, (r.NextSingle() - 0.5f) * 1.2f, (r.NextSingle() - 0.5f) * 0.3f)),
                Push = PerTickSquared((e.Wind * 0.3f) + new Vector3f(0f, 0f, -0.1f)),
                Keep = KeepPerTick(0.8f),
                Life = Ticks(4f + (r.NextSingle() * 2f)),
                SizeFrom = 0.02f + (r.NextSingle() * 0.05f),
                SizeTo = 0f,
                Swell = 0.4f,
                Aspect = 1f,
                Red = shade,
                Green = shade,
                Blue = shade,
                Strength = 0.9f,
                FadeIn = 0.1f,
                FadeOut = 0.3f,
                Streak = 0.5f,
                Look = BlastSpriteLook.Grit,
            });
        }
    }
}

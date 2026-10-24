using OpenIGI.Core.Numerics;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.World.Particles;

/// <summary>One way a wound throws its drops: how many, how wide and how fast.</summary>
/// <param name="Minimum">The fewest drops thrown.</param>
/// <param name="Range">How many more may be: the count is the minimum plus a draw below this.</param>
/// <param name="ConeRadians">The cone's full opening angle.</param>
/// <param name="MinimumSpeed">The slowest a drop goes, in world units a tick.</param>
/// <param name="MaximumSpeed">The fastest.</param>
public readonly record struct BloodSpray(int Minimum, int Range, float ConeRadians, float MinimumSpeed, float MaximumSpeed);

/// <summary>
/// A title's blood: the numbers its wound effect carries (Project I.G.I. <c>0x463AD0</c>, I.G.I. 2
/// <c>HumanDamage_CreateFleshEnterEffect</c>). The two games run the same code with these alone different.
/// </summary>
/// <param name="DropSize">The drops' half-size, in world units.</param>
/// <param name="DropCapacity">How many drops one body shows at once.</param>
/// <param name="DropFade">The ticks a drop fades over once its life is spent.</param>
/// <param name="Forward">The drops thrown on along the round.</param>
/// <param name="Back">The drops thrown back towards the shooter.</param>
public sealed record BloodTuning(float DropSize, int DropCapacity, int DropFade, BloodSpray Forward, BloodSpray Back)
{
    /// <summary>The mist's sprite, in both titles.</summary>
    public const string MistSprite = "blood1.spr";

    /// <summary>The drops' sprite, in both titles.</summary>
    public const string DropSprite = "blood2.spr";

    /// <summary>The mist's half-size, in world units: 1228.8 in both.</summary>
    public const float MistSize = 1228.8f;

    /// <summary>How many mist quads one body shows at once: two in both.</summary>
    public const int MistCapacity = 2;

    /// <summary>The ticks the mist shows before it goes, and over which its frames play: fifteen in both.</summary>
    public const int MistLife = 15;

    /// <summary>The ticks a drop lives before it fades: thirty in both.</summary>
    public const int DropLife = 30;

    /// <summary>What a drop falls by each tick, in world units: real gravity, in both.</summary>
    public const float DropGravityPerTick = -14.866963f;

    /// <summary>How far off the blood is drawn: a hundred metres, in both.</summary>
    public const float DrawRange = 409600f;

    /// <summary>The mist's graphic flags, <c>0x1080000</c> in both: faded through its alpha, filtered.</summary>
    public const int MistFlags = Particle2DFlags.Filtered | Particle2DFlags.FadesByOpacity;

    /// <summary>The drops' graphic flags, <c>0x80000</c> in both: faded through their alpha.</summary>
    public const int DropFlags = Particle2DFlags.FadesByOpacity;

    /// <summary>The colour of blood no light tints, <c>127</c> grey: Project I.G.I.'s always, I.G.I. 2's for an unlit body.</summary>
    public static readonly Vector3f Unlit = new(127f, 127f, 127f);
}

/// <summary>
/// One body's blood, the effect both titles' engines throw where a round enters flesh: a mist at the wound and drops thrown
/// forward along the round and back towards the shooter, each body keeping one mist system and one drop system, made anew once
/// the last has burnt out.
/// </summary>
/// <remarks>
/// <para>
/// The order is the original's own, and so are its draws from the run stream: the mist first, taking none; then the forward
/// count, drawn before any of its drops; each drop its spin, then thrown within its cone by the engine's own stream; then the
/// back count and its drops along the wound's reversed direction. The stain a wound leaves on what stands behind it and the
/// stain a drop leaves where it lands are each title's own, against its own world, and come before (the wound's) and after
/// (the collider's) this.
/// </para>
/// <para>
/// The mist plays its sprite's frames over its fifteen ticks, a whole number of ticks to a frame (<c>15 / frames</c>: seven
/// frames show two ticks each, the last held a tick longer), and fades through its alpha; the drops fall and fade likewise.
/// </para>
/// </remarks>
public sealed class Particle2DBleeding
{
    /// <summary>Gets the body's mist, or <see langword="null"/> before it first bleeds.</summary>
    public Particle2DSystem? Mist { get; private set; }

    /// <summary>Gets the body's drops, or <see langword="null"/> before it first bleeds.</summary>
    public Particle2DSystem? Drops { get; private set; }

    /// <summary>Gets a value indicating whether both systems have burnt out, or were never made.</summary>
    public bool IsQuiet => Mist is not { IsDeleted: false } && Drops is not { IsDeleted: false };

    /// <summary>A drop's spin: about a degree and a half either side of a small turn, from one run-stream draw used twice.</summary>
    /// <param name="random">The run stream.</param>
    /// <returns>The spin, in radians a tick.</returns>
    public static float Spin(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Fpu share = (Fpu)(double)(ushort)random.NextRaw() * IgiRandom.Scale;
        return (((((share - 0.5) + share) - 0.5) * 0.05235987901687622) + 0.012799081392586231).F;
    }

    /// <summary>Throws one wound's blood.</summary>
    /// <param name="particles">The engine the body's systems run in.</param>
    /// <param name="tuning">The title's numbers.</param>
    /// <param name="position">Where the round entered.</param>
    /// <param name="direction">The round's direction; need not be of unit length.</param>
    /// <param name="random">The run stream: the counts and each drop's spin.</param>
    /// <param name="colour">The colour a newly made system takes, on the scale where 128 is white.</param>
    /// <param name="mistFrames">How many frames the mist's sprite has.</param>
    /// <param name="landing">What the drops land on, or <see langword="null"/> for drops that pass through.</param>
    public void Spray(
        Particle2DModule particles, BloodTuning tuning, Vector3d position, Vector3f direction, IgiRandom random, Vector3f colour,
        int mistFrames, IParticleCollider? landing)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(random);
        Vector3f along = Unit(direction);
        if (Mist is not { IsDeleted: false })
        {
            int frames = Math.Max(1, mistFrames);
            Particle2DLook mist = new(
                BloodTuning.MistSprite, IsAnimated: true, frames, BloodTuning.MistLife / frames, colour, BloodTuning.MistSize, BloodTuning.MistSize,
                BloodTuning.DrawRange, BloodTuning.MistFlags | Particle2DFlags.DepthTested);
            Mist = particles.New(mist, Vector3f.Zero, sizeDelta: 0f, BloodTuning.MistCapacity, collider: null);
        }

        Mist.AddParticle(position, Vector3f.Zero, intensity: 1f, rotationDelta: 0f, rotation: 0f, size: 1f, BloodTuning.MistLife, fadeTime: 0);
        if (Drops is not { IsDeleted: false })
        {
            Particle2DLook drops = new(
                BloodTuning.DropSprite, IsAnimated: false, Frames: 1, AnimationDelta: 0, colour, tuning.DropSize, tuning.DropSize,
                BloodTuning.DrawRange, BloodTuning.DropFlags | Particle2DFlags.DepthTested);
            Drops = particles.New(drops, new Vector3f(0f, 0f, BloodTuning.DropGravityPerTick), sizeDelta: 0f, tuning.DropCapacity, landing);
        }

        Throw(Drops, tuning.Forward, position, along, random, tuning.DropFade);
        Throw(Drops, tuning.Back, position, new Vector3f(-along.X, -along.Y, -along.Z), random, tuning.DropFade);
    }

    /// <summary>Lets the body's systems go, as its owner leaving the level does.</summary>
    public void Clear()
    {
        Mist = null;
        Drops = null;
    }

    private static void Throw(Particle2DSystem drops, BloodSpray spray, Vector3d position, Vector3f axis, IgiRandom random, int fade)
    {
        int count = (int)(random.NextRaw() % (uint)Math.Max(1, spray.Range)) + spray.Minimum;
        for (int i = 0; i < count; i++)
        {
            drops.AddConeParticle(
                position, axis, spray.ConeRadians, spray.MinimumSpeed, spray.MaximumSpeed, intensity: 1f, Spin(random), rotation: 0f, size: 1f,
                BloodTuning.DropLife, fade);
        }
    }

    // A vector scaled by the reciprocal of its length, or left as it is when zero.
    private static Vector3f Unit(Vector3f v)
    {
        if (v.X == 0f && v.Y == 0f && v.Z == 0f)
        {
            return v;
        }

        Fpu length = Fpu.Sqrt((((Fpu)v.X * v.X) + ((Fpu)v.Y * v.Y)) + ((Fpu)v.Z * v.Z));
        Fpu inverse = (Fpu)1.0 / length;
        return new Vector3f(((Fpu)v.X * inverse).F, ((Fpu)v.Y * inverse).F, ((Fpu)v.Z * inverse).F);
    }
}

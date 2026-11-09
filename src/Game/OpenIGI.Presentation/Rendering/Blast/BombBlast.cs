using OpenIGI.Core.Numerics;
using OpenIGI.UI.Animation;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// One bomb going off, as the remastered blast draws it: where, how big, and how far into its few seconds it is -- with the
/// curves every part of the blast reads its strength from, so the light, the shockwave, the fireball and the shake all agree
/// on one timeline.
/// </summary>
/// <remarks>
/// <para>
/// Everything is a function of the blast's age in seconds, which is counted on the game's fixed tick and blended by the
/// frame's fraction of one, so a <c>-lockstep</c> capture of a blast is the same picture every time.
/// </para>
/// <para>
/// The shapes are taken from footage of large charges rather than from any game: a flash that peaks within a frame or two
/// and is mostly gone in a third of a second; a fireball that reaches most of its size in about a tenth of a second and
/// then grows slowly while it rises and cools from white through orange to a dull red; a shock front leaving faster than
/// sound and slowing to it; and a dark column that climbs for seconds after the fire has gone out.
/// </para>
/// </remarks>
public sealed class BombBlast
{
    /// <summary>How fast sound -- and what the blast does to the ear and the ground underfoot -- travels, in metres a second.</summary>
    public const float SoundMetresPerSecond = 343f;

    /// <summary>How long a blast lives, in seconds: past the round's five-second close, so the level's reset takes it.</summary>
    public const float LifeSeconds = 20f;

    /// <summary>Initializes a new instance of the <see cref="BombBlast"/> class.</summary>
    /// <param name="centreMetres">Where it went off, in world metres.</param>
    /// <param name="radiusMetres">How far it kills, in metres.</param>
    /// <param name="seed">The seed every random part of it draws from, the same on every machine that shows it.</param>
    public BombBlast(Vector3d centreMetres, float radiusMetres, uint seed)
    {
        CentreMetres = centreMetres;
        RadiusMetres = MathF.Max(radiusMetres, 1f);
        Seed = seed;
    }

    /// <summary>Gets where it went off, in world metres.</summary>
    public Vector3d CentreMetres { get; }

    /// <summary>Gets how far it kills, in metres; the size everything else is scaled by.</summary>
    public float RadiusMetres { get; }

    /// <summary>Gets the seed every random part of it draws from.</summary>
    public uint Seed { get; }

    /// <summary>Gets how many game ticks it has run.</summary>
    public int Ticks { get; private set; }

    /// <summary>Gets a value indicating whether it has burned out and can be forgotten.</summary>
    public bool IsSpent => Ticks / (float)TickRate >= LifeSeconds;

    /// <summary>Gets the game ticks a second its age is counted in.</summary>
    public static int TickRate => 30;

    /// <summary>The seed a blast at a place draws from: a hash of where it is, so every machine that shows it agrees.</summary>
    /// <param name="atUnits">Where it went off, in world units.</param>
    /// <returns>The seed.</returns>
    public static uint SeedAt(Vector3d atUnits)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)(long)Math.Round(atUnits.X)) * 16777619u;
            h = (h ^ (uint)(long)Math.Round(atUnits.Y)) * 16777619u;
            h = (h ^ (uint)(long)Math.Round(atUnits.Z)) * 16777619u;
            return h == 0 ? 0x9E3779B9u : h;
        }
    }

    /// <summary>Steps it one game tick.</summary>
    public void Tick() => Ticks++;

    /// <summary>Its age at this frame, in seconds.</summary>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    /// <returns>The age.</returns>
    public float Age(float blend) => (Ticks + Math.Clamp(blend, 0f, 1f)) / TickRate;

    /// <summary>How long after it went off its sound and shake reach a place, in seconds.</summary>
    /// <param name="distanceMetres">How far away the place is.</param>
    /// <returns>The delay.</returns>
    public static float ArrivalSeconds(float distanceMetres) => MathF.Max(0f, distanceMetres) / SoundMetresPerSecond;

    /// <summary>How bright its light is, against its peak: a flash within two frames, falling fast, then the fire's glow.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>The brightness, one at the flash.</returns>
    public float LightLevel(float age)
    {
        if (age < 0f)
        {
            return 0f;
        }

        float rise = Smooth(0f, 0.035f, age);
        float flash = MathF.Exp(-age / 0.16f);
        float fire = 0.28f * (1f - Smooth(0.25f, 3.0f, age));

        // The fire's light breathes as the fireball rolls; seeded, so every machine sees the same flicker.
        float flicker = 1f + (0.18f * MathF.Sin((age * 23f) + (Seed & 0xFF)) * MathF.Sin((age * 7.3f) + ((Seed >> 8) & 0xFF)));
        return rise * (flash + (fire * flicker));
    }

    /// <summary>The colour temperature of its light: white-hot at the flash, cooling to a deep orange as the fire burns down.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>The temperature, in kelvin.</returns>
    public static float LightKelvin(float age) => Interpolate.Lerp(6500f, 1700f, Smooth(0f, 1.4f, age));

    /// <summary>How far its light reaches, in metres.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>The reach.</returns>
    public float LightReachMetres(float age) => RadiusMetres * Interpolate.Lerp(4.5f, 2.2f, Smooth(0f, 1.5f, age));

    /// <summary>Where its visible shock front is, in metres from its centre.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>The front's radius.</returns>
    /// <remarks>
    /// Slower than sound, deliberately: a front at the speed of sound crosses the screen in a frame or two and is never
    /// seen. This is the ring of refracted air that every slow-motion recording of a charge shows, at a speed the eye can
    /// follow; the sound and the shake still arrive at the speed of sound.
    /// </remarks>
    public float ShockRadiusMetres(float age) => RadiusMetres * 5.5f * (1f - MathF.Exp(-age / 0.32f));

    /// <summary>How strongly the shock front bends the picture behind it.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>Its strength, one when it leaves, nought when it is gone.</returns>
    public static float ShockStrength(float age) => age < 0f ? 0f : Smooth(0f, 0.02f, age) * (1f - Smooth(0.15f, 0.9f, age));

    /// <summary>The fireball's radius: most of its size in a tenth of a second, then slow growth as it rises.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>The radius, in metres.</returns>
    public float FireballRadiusMetres(float age) =>
        RadiusMetres * ((0.55f * (1f - MathF.Exp(-age / 0.09f))) + (0.18f * Smooth(0.1f, 3f, age)));

    /// <summary>How hot the fireball still is, which picks its colour and how much light it gives.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>One at the flash, nought when only smoke is left.</returns>
    public static float FireballHeat(float age) => 1f - Smooth(0.05f, 2.4f, age);

    /// <summary>How high the top of the smoke column has climbed above the centre, in metres.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>The height.</returns>
    public float ColumnHeightMetres(float age) => RadiusMetres * (0.35f + (1.9f * Smooth(0.15f, 7f, age)));

    /// <summary>How dense the smoke is, which fades as the round's close runs out.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>One at its thickest.</returns>
    public static float SmokeDensity(float age) => Smooth(0.05f, 0.6f, age) * (1f - Smooth(9f, LifeSeconds, age));

    /// <summary>How much of a concussion somebody standing at a distance takes: how hard it stuns, rings and whites out.</summary>
    /// <param name="distanceMetres">How far from the centre they stand.</param>
    /// <returns>One at the centre, nought beyond about three times the killing radius.</returns>
    public float ConcussionAt(float distanceMetres) =>
        1f - Smooth(RadiusMetres * 0.6f, RadiusMetres * 3f, distanceMetres);

    /// <summary>How much trauma -- the shake's strength -- it gives somebody at a distance.</summary>
    /// <param name="distanceMetres">How far from the centre they stand.</param>
    /// <returns>One within the killing radius, falling to nought at about seven times it.</returns>
    public float TraumaAt(float distanceMetres) => 1f - Smooth(RadiusMetres * 0.8f, RadiusMetres * 7f, distanceMetres);

    /// <summary>Hermite step from nought at <paramref name="from"/> to one at <paramref name="to"/>.</summary>
    /// <param name="from">Where it starts to rise.</param>
    /// <param name="to">Where it reaches one.</param>
    /// <param name="x">The input.</param>
    /// <returns>The step.</returns>
    public static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / MathF.Max(to - from, 1e-6f), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

}

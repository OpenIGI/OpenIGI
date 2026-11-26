using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>How a bomb's sprite is drawn.</summary>
internal enum BlastSpriteLook : byte
{
    /// <summary>A billow of smoke or dust, blended over the scene, lit from above, glowing from within while it is hot.</summary>
    Smoke,

    /// <summary>A tongue of fire, added to the scene's light.</summary>
    Fire,

    /// <summary>The flash: a wide glow added over everything, through walls.</summary>
    Flare,

    /// <summary>A burning fragment, a streak of light along its travel.</summary>
    Ember,

    /// <summary>A dark chip of what the bomb stood on, a streak blended over the scene.</summary>
    Grit,
}

/// <summary>One sprite of a bomb's blast, stepped on the game's tick.</summary>
/// <remarks>
/// Everything a sprite does over its life is set as it is thrown -- its speed and what slows it, how it grows, how it fades,
/// how hot it glows -- so a blast is decided in the moment it goes off, from its seed, the same on every machine.
/// </remarks>
internal struct BlastSprite
{
    /// <summary>Where it is, in world units.</summary>
    public Vector3d Position;

    /// <summary>Where it was a tick ago, in world units.</summary>
    public Vector3d Previous;

    /// <summary>How far it moves a tick, in world units.</summary>
    public Vector3f Velocity;

    /// <summary>What it is pushed by each tick, in world units a tick: gravity downward, the hot air's lift and the wind.</summary>
    public Vector3f Push;

    /// <summary>How much of its speed it keeps each tick.</summary>
    public float Keep;

    /// <summary>The ticks it has lived; negative while it waits to be thrown.</summary>
    public int Age;

    /// <summary>How many ticks it lives.</summary>
    public int Life;

    /// <summary>How big it starts, in metres.</summary>
    public float SizeFrom;

    /// <summary>How big it ends, in metres.</summary>
    public float SizeTo;

    /// <summary>How fast it grows: one evenly, higher grows most of the way early.</summary>
    public float Swell;

    /// <summary>How tall it is against how wide: one round and free to turn, more than one stood up, less than one laid flat.</summary>
    public float Aspect;

    /// <summary>Which way it is turned, in radians.</summary>
    public float Turn;

    /// <summary>How fast it turns, in radians a tick.</summary>
    public float Spin;

    /// <summary>Its colour, red.</summary>
    public float Red;

    /// <summary>Its colour, green.</summary>
    public float Green;

    /// <summary>Its colour, blue.</summary>
    public float Blue;

    /// <summary>How opaque or bright it is at its most.</summary>
    public float Strength;

    /// <summary>The share of its life it takes to come in.</summary>
    public float FadeIn;

    /// <summary>The share of its life it fades out over, at its end.</summary>
    public float FadeOut;

    /// <summary>How brightly smoke glows from the fire inside it as it is thrown.</summary>
    public float Heat;

    /// <summary>The share of its life the glow lasts.</summary>
    public float HeatLife;

    /// <summary>How long a streak is drawn, in ticks of its travel.</summary>
    public float Streak;

    /// <summary>Which of the texture's four drawings it is.</summary>
    public byte Variant;

    /// <summary>How it is drawn.</summary>
    public BlastSpriteLook Look;

    /// <summary>How far through its life it is at a frame, nought to one.</summary>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    /// <returns>The share of its life.</returns>
    public readonly float Lived(float blend) => Math.Clamp((Age + blend) / Math.Max(Life, 1), 0f, 1f);

    /// <summary>How big it is at a point of its life, in metres.</summary>
    /// <param name="lived">The share of its life.</param>
    /// <returns>Its size.</returns>
    public readonly float SizeAt(float lived) => SizeFrom + ((SizeTo - SizeFrom) * (1f - MathF.Pow(1f - lived, Math.Max(Swell, 0.05f))));

    /// <summary>How much of its strength shows at a point of its life.</summary>
    /// <param name="lived">The share of its life.</param>
    /// <returns>Nought to one.</returns>
    public readonly float FadeAt(float lived)
    {
        float rise = FadeIn > 0f ? Math.Clamp(lived / FadeIn, 0f, 1f) : 1f;
        float fall = FadeOut > 0f ? Math.Clamp((1f - lived) / FadeOut, 0f, 1f) : 1f;
        return rise * fall * (2f - fall) * Strength;
    }
}

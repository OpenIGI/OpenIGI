using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World.Particles;

/// <summary>What both games' <c>Smoke</c> task draws its plume with: its sprites, blends and scales (Project I.G.I.'s <c>0x437510</c>).</summary>
public static class SmokePlume
{
    /// <summary>The sprites a plume's "Sprite index" picks: a FireBall's flame, blood, and the two smokes.</summary>
    public static readonly string[] Sprites = ["fireball.spr", "blood1.spr", "smoke2.spr", "smoke1.spr"];

    /// <summary>The transparency its "Fade Mode" picks.</summary>
    public static readonly int[] FadeModeTransparency = [0x1080000, 0x1000008, 0x1000010];

    /// <summary>How far away its particles are still drawn, world units.</summary>
    public const float Range = 409600f;

    /// <summary>What a colour channel of its parameters is scaled by.</summary>
    public const float ColourScale = 63f;

    /// <summary>The sprite a sprite index names, the last smoke past the end.</summary>
    /// <param name="index">The index.</param>
    public static string SpriteFor(int index) => Sprites[Math.Clamp(index, 0, Sprites.Length - 1)];

    /// <summary>The transparency a fade mode names; an unknown mode is the first, where the original took what its stack held.</summary>
    /// <param name="mode">The mode.</param>
    public static int TransparencyFor(int mode) =>
        (uint)mode < (uint)FadeModeTransparency.Length ? FadeModeTransparency[mode] : FadeModeTransparency[0];
}

/// <summary>
/// A plume's emitter, <c>Smoke_Run</c> in both games (Project I.G.I.'s <c>0x437DB0</c>): its generate factor is added up each
/// tick, and every thirty of it throws one particle from a random point of its disc, up its axis within its cone.
/// </summary>
/// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
/// <remarks>
/// Each particle takes three draws of the plume's stream in this order: the angle round the disc, how far out, and the
/// spin. The arithmetic stores where Project I.G.I.'s does; at the sequel's precision a store changes nothing.
/// </remarks>
public sealed class SmokePlumeEmitter<TFpu>
    where TFpu : struct, IX87<TFpu>
{
    /// <summary>Gets or sets the generate factor added up and not yet spent, <c>+0xA0</c>.</summary>
    public float Count { get; set; }

    /// <summary>Emits a tick of particles.</summary>
    /// <param name="system">The plume's particle system.</param>
    /// <param name="random">The plume's stream.</param>
    /// <param name="generateFactor">This tick's generate factor; thirty makes a particle.</param>
    /// <param name="centre">The disc's centre, world units.</param>
    /// <param name="radius">The disc's radius, metres.</param>
    /// <param name="direction">Its axis.</param>
    /// <param name="maxRandomAngle">Its cone's angle.</param>
    /// <param name="minVelocity">The slowest a particle leaves, metres a second over 30 by 4.53.</param>
    /// <param name="maxVelocity">The fastest.</param>
    /// <param name="intensity">A particle's intensity.</param>
    /// <param name="minRotation">The slowest spin, radians a second.</param>
    /// <param name="maxRotation">The fastest.</param>
    /// <param name="lifeTicks">The ticks a particle lives before it fades.</param>
    /// <param name="fadeTicks">The ticks it fades over.</param>
    public void Emit(
        Particle2DSystem system,
        IgiRandom random,
        double generateFactor,
        Vector3d centre,
        float radius,
        Vector3f direction,
        float maxRandomAngle,
        float minVelocity,
        float maxVelocity,
        float intensity,
        float minRotation,
        float maxRotation,
        int lifeTicks,
        int fadeTicks)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(random);
        Count = ((TFpu)generateFactor + Count).F;
        while (Count > 30f)
        {
            float angle = ((TFpu)Draw(random) * 6.2831854820251465).F;
            float reach = ((TFpu)Draw(random) * radius * 4096.0).F;
            TFpu spin = (((TFpu)Draw(random) * ((TFpu)maxRotation - minRotation)) + minRotation) * 0.03333333507180214;
            Vector3d at = new(
                ((TFpu)Math.Cos(angle) * reach + centre.X).Value,
                ((TFpu)Math.Sin(angle) * reach + centre.Y).Value,
                centre.Z);
            system.AddConeParticle(
                at,
                direction,
                maxRandomAngle,
                ((TFpu)maxVelocity * 136.0).F,
                ((TFpu)minVelocity * 136.0).F,
                intensity,
                spin.F,
                0f,
                1f,
                lifeTicks,
                fadeTicks);
            Count = ((TFpu)Count - 30.0).F;
        }
    }

    // Random_Float: the draw's low sixteen bits over 65536.
    private static double Draw(IgiRandom random) => (ushort)random.NextRaw() * 0.0000152587890625;
}

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>An explosion's fireball, <c>FireBall</c> (<c>Explosion0_FireBallRunHandler</c>, <c>igi2 0x5098B0</c>): a model that waits, then shows its life out.</summary>
/// <param name="position">Where it is.</param>
/// <param name="size">How big, <c>vRadius</c>.</param>
/// <param name="lifetime">The ticks it shows.</param>
/// <param name="fadeout">The ticks it fades over.</param>
/// <param name="delay">The ticks before it shows.</param>
/// <param name="animationDuration">The ticks its animation runs.</param>
public sealed class Igi2FireBall(Vector3d position, float size, int lifetime, int fadeout, int delay, int animationDuration) : IIgi2DynamicTask
{
    /// <summary>The fireball's model, <c>1006_01_1</c>.</summary>
    public const string Model = "1006_01_1";

    /// <summary>Gets where it is.</summary>
    public Vector3d Position { get; } = position;

    /// <summary>Gets how big it is.</summary>
    public float Size { get; } = size;

    /// <summary>Gets the ticks it shows for, <c>nLifetime</c>.</summary>
    public int TotalLifetime { get; } = lifetime;

    /// <summary>Gets the ticks it has left.</summary>
    public int Lifetime { get; private set; } = lifetime;

    /// <summary>Gets the ticks it fades over.</summary>
    public int Fadeout { get; } = fadeout;

    /// <summary>Gets the ticks before it shows.</summary>
    public int Delay { get; private set; } = delay;

    /// <summary>Gets the ticks its animation runs.</summary>
    public int AnimationDuration { get; } = animationDuration;

    /// <inheritdoc/>
    public bool Run()
    {
        if (Delay > 0)
        {
            Delay--;
            return true;
        }

        if (Lifetime == 0)
        {
            return false;
        }

        Lifetime--;
        return true;
    }
}

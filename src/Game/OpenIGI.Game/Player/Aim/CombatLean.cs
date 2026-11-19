using OpenIGI.Game.World;

namespace OpenIGI.Game.Player.Aim;

/// <summary>What a lean does to the eye this tick: a slide to the side and a dip, in world units, and a roll in radians.</summary>
/// <param name="Across">The slide to the eye's right; negative to the left.</param>
/// <param name="Up">The rise on world Z; negative drops the eye.</param>
/// <param name="Roll">The roll about the line of sight; positive tips the top of the view to the right.</param>
public readonly record struct EyeLean(float Across, float Up, float Roll)
{
    /// <summary>Gets the eye unmoved.</summary>
    public static EyeLean None => default;

    /// <summary>Gets a value indicating whether the eye is moved at all.</summary>
    public bool Moves => Across != 0f || Up != 0f || Roll != 0f;

    /// <summary>Two leans together.</summary>
    /// <param name="left">One lean.</param>
    /// <param name="right">The other.</param>
    /// <returns>Their sum.</returns>
    public static EyeLean operator +(EyeLean left, EyeLean right) =>
        new(left.Across + right.Across, left.Up + right.Up, left.Roll + right.Roll);
}

/// <summary>
/// Modern aim's lean: the eye out to one side with the weapon still up, so a player can see, aim and fire round a corner.
/// <b>Not retail</b> -- the original's lean is the peek (<see cref="PlayerInputSlot.Peek"/>), which lowers the weapon.
/// </summary>
/// <remarks>
/// Stepped once a tick from the two lean keys, linearly over <see cref="Seconds"/>; what it does to the eye is eased at both
/// ends, and the eye it moves is swept against the world like the original's (<see cref="HumanLeanEye"/>), so a lean along a wall
/// stops short of it. The view tips into the lean and the weapon, hung off the view, tips with it.
/// </remarks>
public sealed class CombatLean
{
    /// <summary>How far a full lean slides the eye, in metres.</summary>
    public const float SideMetres = 0.32f;

    /// <summary>How far a full lean drops the eye, in metres: the shoulder going down as the head goes out.</summary>
    public const float DropMetres = 0.04f;

    /// <summary>How far a full lean rolls the view, in degrees.</summary>
    public const float RollDegrees = 12f;

    /// <summary>How long a lean takes from upright to full, in seconds.</summary>
    public const float Seconds = 0.22f;

    /// <summary>Gets the lean as of the last tick: minus one (full left) to one (full right).</summary>
    public float Amount { get; private set; }

    /// <summary>Gets <see cref="Amount"/> as of the tick before.</summary>
    public float PreviousAmount { get; private set; }

    /// <summary>Stands the eye upright at once.</summary>
    public void Reset()
    {
        Amount = 0f;
        PreviousAmount = 0f;
    }

    /// <summary>Steps the lean one tick.</summary>
    /// <param name="target">The lean wanted: minus one (left), nought, or one (right).</param>
    public void Tick(float target)
    {
        PreviousAmount = Amount;
        float goal = Math.Clamp(target, -1f, 1f);
        float step = AimDownSights.TickSeconds / Seconds;
        Amount = Amount < goal ? MathF.Min(goal, Amount + step) : MathF.Max(goal, Amount - step);
    }

    /// <summary>The lean wanted from the two keys: holding both stands upright.</summary>
    /// <param name="left">Whether lean-left is held.</param>
    /// <param name="right">Whether lean-right is held.</param>
    /// <returns>Minus one, nought or one.</returns>
    public static float Target(bool left, bool right) => (right ? 1f : 0f) - (left ? 1f : 0f);

    /// <summary>What a lean amount does to the eye.</summary>
    /// <param name="amount">The lean, minus one to one.</param>
    /// <returns>The eye's slide, dip and roll, eased.</returns>
    public static EyeLean Eye(float amount)
    {
        float shaped = MathF.CopySign(AimDownSights.Shape(MathF.Abs(amount)), amount);
        float unitsPerMetre = (float)WorldScene.UnitsPerMetre;
        return new EyeLean(
            shaped * SideMetres * unitsPerMetre,
            -MathF.Abs(shaped) * DropMetres * unitsPerMetre,
            shaped * RollDegrees * (MathF.PI / 180f));
    }
}

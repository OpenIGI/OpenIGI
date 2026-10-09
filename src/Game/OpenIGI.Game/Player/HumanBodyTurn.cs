namespace OpenIGI.Game.Player;

/// <summary>
/// How a human on foot turns on the input frame: the body's yaw (<c>0x4610C0</c>, float 0) or the view's pitch
/// (<c>0x464800</c>, float 1).
/// </summary>
/// <remarks>
/// <para>
/// The frame carries the angle as a fraction of a half-turn. A small value is divided by <c>2 - 16.67|s|</c>, so
/// once the input is under 0.06 (about 10.8 degrees) each tick takes roughly half of what is left.
/// </para>
/// <para>
/// Both halves average this tick's value with the last tick's when the view's scale (<c>view+484</c>) equals
/// <c>tan(pi/4)</c> -- and only the pitch ever does. <c>0x464800</c> rounds the tangent to a float, 1.0, which an
/// unzoomed view's scale is; <c>0x4610C0</c> compares against the FPU's own <c>fptan</c> of the float-rounded
/// pi/4, 1.0000000437, which no float equals. Probed on mission 4: patrol 411 turned 62 degrees whole on its
/// first walking tick, and an 8.5-degree error took 7.08 (8.5 / 1.21) with no half-step before it.
/// </para>
/// <para>
/// An AI clamps the angle before it writes it: to pi for walking and combat, which leaves only the easing,
/// and to 0.0259 rad for a patrol's LookAtNode, which turns it at about 24 degrees a second.
/// </para>
/// </remarks>
/// <param name="averages">Whether this half averages with the last tick: the pitch, not the yaw.</param>
public sealed class HumanBodyTurn(bool averages)
{
    /// <summary>The input below which a turn is slowed — <c>0x533938</c>.</summary>
    public const float SmallInput = 0.059999999f;

    private float _previousInput;

    /// <summary>Turns for one tick.</summary>
    /// <param name="input">This tick's frame float 0: the turn over pi.</param>
    /// <returns>The yaw to add this tick, in radians, in the direction of the input.</returns>
    public double Step(float input)
    {
        float averaged = averages ? (input + _previousInput) * 0.5f : input;
        _previousInput = input;

        double turn = averaged * Math.PI;
        float magnitude = Math.Abs(averaged);
        if (magnitude < SmallInput)
        {
            turn /= 2.0 - (magnitude * 16.666666666666668);
        }

        return turn;
    }

    /// <summary>Forgets the last tick's input, as when the body is attached to an emplacement.</summary>
    public void Reset() => _previousInput = 0f;
}

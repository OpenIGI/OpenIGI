namespace OpenIGI.Game.Player;

/// <summary>
/// How a human on foot turns on the input frame's turn — the yaw half of <c>0x4610C0</c>, the same for
/// the player's mouse and an AI's steering.
/// </summary>
/// <remarks>
/// <para>
/// The frame carries the turn as a fraction of a half-turn (float 0, the angle over pi). With the view
/// unzoomed the body averages this tick's value with the last tick's, and a small value is divided by
/// <c>2 - 16.67|s|</c>. A turn therefore eases in — its first tick takes half — and eases out: once the
/// input is under 0.06 (about 10.8 degrees) each tick takes roughly half of what is left.
/// </para>
/// <para>
/// An AI clamps the angle before it writes it: to pi for walking and combat, which leaves only the easing,
/// and to 0.0259 rad for a patrol's LookAtNode, which turns it at about 24 degrees a second.
/// </para>
/// </remarks>
public sealed class HumanBodyTurn
{
    /// <summary>The input below which a turn is slowed — <c>0x533938</c>.</summary>
    public const float SmallInput = 0.059999999f;

    private float _previousInput;

    /// <summary>Turns for one tick.</summary>
    /// <param name="input">This tick's frame float 0: the turn over pi.</param>
    /// <returns>The yaw to add this tick, in radians, in the direction of the input.</returns>
    public double Step(float input)
    {
        float averaged = (input + _previousInput) * 0.5f;
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

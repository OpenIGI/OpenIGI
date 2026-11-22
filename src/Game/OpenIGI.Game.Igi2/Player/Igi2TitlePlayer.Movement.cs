using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Whose movement and look the player has, taken from the settings each tick.</content>
public sealed partial class Igi2TitlePlayer
{
    private Igi2MovementChoice? _movementShown;
    private Igi2MovementCompare _compareShown;

    /// <summary>Gives the player the tick's movement and look, and tells it when a comparison has changed them.</summary>
    /// <param name="choice">The settings' choice this tick (<see cref="Igi2MovementChoice.Current"/>).</param>
    /// <remarks>
    /// The first tick only notes it. After that, a step of the session's comparison is said on the screen, so the key that steps
    /// it shows what it stepped to -- back to the settings' own choice too.
    /// </remarks>
    private void TakeMovementChoice(Igi2MovementChoice choice)
    {
        Human.Movement = choice.Movement;
        Human.Igi1Look = choice.Look;
        Human.Igi1Weight = choice.Weight;
        Igi2MovementCompare compare = GameplayCVars.Igi2MovementCompare.Value;
        if (_movementShown is not null && compare != _compareShown)
        {
            ShowStatusText(compare == Igi2MovementCompare.Off ? $"{choice.Describe()} (settings)" : choice.Describe());
        }

        _movementShown = choice;
        _compareShown = compare;
    }

    /// <summary>The tick's look input for the channel reader: the original's analog motion, or Project I.G.I.'s original look's.</summary>
    /// <param name="input">The tick's input.</param>
    /// <returns>The horizontal and vertical motion the reader scales by its sensitivity.</returns>
    /// <remarks>
    /// Turning as Project I.G.I.'s original turns, the motion is taken at the profile's own rate (<see cref="MouseLookSettings.RetailInput"/>)
    /// and the reader's sensitivity is one, so its eased turn goes as fast as the linear look and only the easing tells them apart.
    /// </remarks>
    private (float X, float Y) LookInput(in TitlePlayerInput input)
    {
        if (!Human.TurnsAsIgi1Retail)
        {
            return (Analog(input.MouseX, Human.UnfilteredMouse), Analog(input.MouseY, Human.UnfilteredMouse));
        }

        MouseLookSettings rate = MouseLookSettings.FromSensitivity(input.MouseSensitivity, invertPitch: false);
        float turnPerInput = (float)(Math.PI * Humans.Igi2HumanView.DefaultFieldOfView);
        _reader.MouseSensitivity = 1f;
        return (rate.RetailInput(input.MouseX, turnPerInput), rate.RetailInput(input.MouseY, turnPerInput));
    }
}

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
        Human.Igi1Weight = choice.Weight;
        Igi2MovementCompare compare = GameplayCVars.Igi2MovementCompare.Value;
        if (_movementShown is not null && compare != _compareShown)
        {
            ShowStatusText(compare == Igi2MovementCompare.Off ? $"{choice.Describe()} (settings)" : choice.Describe());
        }

        _movementShown = choice;
        _compareShown = compare;
    }
}

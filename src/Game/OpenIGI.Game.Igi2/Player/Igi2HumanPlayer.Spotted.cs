namespace OpenIGI.Game.Igi2.Player;

/// <content>How often the player has been spotted, the mission's statistic.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets how many times a squad first spotted the player, <c>HumanPlayer+0x1E0</c>.</summary>
    public int TimesSpotted { get; private set; }

    /// <summary>
    /// Gets or sets the level's <c>DisableSpottedIncrease</c> expression as the player task holds it, evaluated: while it holds, a
    /// sighting is not counted.
    /// </summary>
    public Func<bool>? IsSpottedIncreaseDisabled { get; set; }

    /// <summary>
    /// A squad first spots the player, when no other squad already has (<c>sub_4C9E00</c>): counted unless the level's expression
    /// holds.
    /// </summary>
    public void NoteDetected()
    {
        if (IsSpottedIncreaseDisabled?.Invoke() != true)
        {
            TimesSpotted++;
        }
    }
}

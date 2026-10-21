using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>How often the player has been spotted, the mission's statistic, and how it keeps low from the soldiers looking for it.</content>
public sealed partial class Igi2HumanPlayer : IAiPlayer
{
    /// <inheritdoc/>
    /// <remarks>
    /// <c>HumanAI_UpdateDetection</c>'s test: crouched and standing crouched, or flat and lying or crawling flat; a player still
    /// going down or getting up does not keep low.
    /// </remarks>
    public bool IsKeepingLow =>
        (Pose == Igi2HumanPose.Crouching && Locomotion.CurrentState == Igi2PlayerState.Crouch)
        || (Pose == Igi2HumanPose.LyingFlat && Locomotion.CurrentState is Igi2PlayerState.Flat or Igi2PlayerState.FlatCrawl);

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

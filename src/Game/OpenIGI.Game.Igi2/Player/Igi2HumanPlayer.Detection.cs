using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The player as detectors see it.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets or sets how many times an alarm has started, <c>nTimesAlarmTriggered</c> (<c>Human_s + 0x200</c>).</summary>
    /// <remarks>An <c>AlarmControl</c> counts it on the level's task 0, the player (<c>AlarmControl_RunHandler</c>, <c>igi2 0x4D0433</c>).</remarks>
    public int TimesAlarmTriggered { get; set; }

    /// <summary>Gets or sets the player's task, whose visibility parameters the factor is shaped by, or <see langword="null"/>.</summary>
    public Igi2HumanPlayerTask? Task { get; set; }

    /// <inheritdoc/>
    /// <remarks>The factor <see cref="UpdateVisibilityFactor"/> works out each tick.</remarks>
    protected override float DetectionVisibility => VisibilityFactor;

    /// <summary>
    /// How well detectors see the player, <c>humanplayer.c HumanPlayer_UpdateVisibilityFactor</c>: fully while a shot has lit it;
    /// otherwise half the light it stands in, to the task's visibility gamma; less crouched (four fifths) or lying (three fifths);
    /// more while turning on the spot or crawling (by a twentieth), walking (a tenth) or running (three twentieths); and kept
    /// between the task's minimum and maximum.
    /// </summary>
    private void UpdateVisibilityFactor()
    {
        double? posture = Pose switch
        {
            Igi2HumanPose.Crouching => Igi2VisibilityFactor.Crouched,
            Igi2HumanPose.LyingFlat => Igi2VisibilityFactor.Lying,
            _ => null,
        };
        float movement = Locomotion.CurrentState switch
        {
            Igi2PlayerState.StandTurn or Igi2PlayerState.CrouchTurn or Igi2PlayerState.FlatCrawl => Igi2VisibilityFactor.Turning,
            >= 3 and <= 10 => Igi2VisibilityFactor.Running,
            (>= 11 and <= 18) or (>= 26 and <= 33) => Igi2VisibilityFactor.Walking,
            _ => 0f,
        };

        // With no task, nothing bounds the factor.
        VisibilityFactor = Igi2VisibilityFactor.Measure(
            IgnoreLightEffectOnVisibilityTimer != 0,
            Lightmap,
            Task?.VisibilityGamma ?? 1f,
            posture,
            movement,
            Task?.VisibilityMinimum ?? float.NegativeInfinity,
            Task?.VisibilityMaximum ?? float.PositiveInfinity);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The player's bump is heard within three metres of its view: event 9, flags 1090, radius 12288
    /// (<c>Human_HumanCollision</c>).
    /// </remarks>
    protected override void OnBlockedByHuman() =>
        Surroundings.SendAiEvent(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.Walk, Team, 1090, 12288f, View.Position, default, this, this));
}

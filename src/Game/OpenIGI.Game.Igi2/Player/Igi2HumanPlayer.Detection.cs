using OpenIGI.Core.Numerics;
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
        float factor = 1f;
        if (IgnoreLightEffectOnVisibilityTimer == 0)
        {
            Fpu half = (Fpu)Lightmap * 0.5;
            double light = half.Value >= 0.0 ? (half.Value <= 1.0 ? half.Value : 1.0) : 0.0;
            factor = (float)Math.Pow(light, Task?.VisibilityGamma ?? 1f);
        }

        if (Pose == Igi2HumanPose.Crouching)
        {
            factor = ((Fpu)factor * 0.800000011920929).F;
        }
        else if (Pose == Igi2HumanPose.LyingFlat)
        {
            factor = ((Fpu)factor * 0.6000000238418579).F;
        }

        int state = Locomotion.CurrentState;
        float lift = state switch
        {
            Igi2PlayerState.StandTurn or Igi2PlayerState.CrouchTurn or Igi2PlayerState.FlatCrawl => 0.05000000074505806f,
            >= 3 and <= 10 => 0.15000000596046448f,
            (>= 11 and <= 18) or (>= 26 and <= 33) => 0.10000000149011612f,
            _ => 0f,
        };
        if (lift != 0f)
        {
            factor = ((Fpu)factor + lift).F;
        }

        if (Task is { } task)
        {
            factor = task.VisibilityMinimum <= factor
                ? (task.VisibilityMaximum >= factor ? factor : task.VisibilityMaximum)
                : task.VisibilityMinimum;
        }

        VisibilityFactor = factor;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The player's bump is heard within three metres of its view: event 9, flags 1090, radius 12288
    /// (<c>Human_HumanCollision</c>).
    /// </remarks>
    protected override void OnBlockedByHuman() =>
        Surroundings.SendAiEvent(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.Walk, Team, 1090, 12288f, View.Position, default, this, this));
}

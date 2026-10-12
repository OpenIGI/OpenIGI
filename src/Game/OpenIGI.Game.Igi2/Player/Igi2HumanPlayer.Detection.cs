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

    /// <inheritdoc/>
    /// <remarks>
    /// The player's bump is heard within three metres of its view: event 9, flags 1090, radius 12288
    /// (<c>Human_HumanCollision</c>).
    /// </remarks>
    protected override void OnBlockedByHuman() =>
        Surroundings.SendAiEvent(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.HumanBump, Team, 1090, 12288f, View.Position, default, this, this));
}

using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>
/// A mission's objectives and status messages as the host shows them: what the computer lists, what is typed on the screen,
/// and how the level has decided the mission.
/// </summary>
public interface IMissionObjectives : IMissionProgress, IComputerObjectives
{
    /// <summary>Gets the status messages on the screen now, in the order they are drawn.</summary>
    IEnumerable<StatusMessageDisplay> DisplayedMessages { get; }

    /// <summary>Shows a message of the host's own among the mission's, as a save or a pickup notice.</summary>
    /// <param name="text">The text.</param>
    /// <param name="durationSeconds">How long it is held once typed.</param>
    /// <param name="portrait">The portrait shown beside it, or empty for none.</param>
    /// <returns>Whether a slot was free to show it in.</returns>
    bool ShowTransientMessage(string text, float durationSeconds = 2f, string portrait = "");

    /// <summary>Removes the subtitles of a cut-scene the player skipped.</summary>
    void ClearCutsceneMessages();
}

using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Host.Services;

/// <summary>The mission being played, if one is: which it is, the level tree it runs in, and how far it has got to being decided.</summary>
/// <remarks>
/// Title-neutral bookkeeping only. How a title builds its level and ticks its gameplay is the title's; what is kept
/// here is what the host's loop asks of any mission -- is one up, is it paused under a menu, has it failed long enough
/// for the failure page, has its success been acted on.
/// </remarks>
internal sealed class MissionSession : IHostService
{
    /// <summary>How many ticks a failed mission is held before the failure page opens.</summary>
    /// <remarks>MainMenu state 9 polls LevelFlow_LevelFailed and opens the in-game menu after its counter passes 150 (0x4181E5..0x41821A).</remarks>
    private const int FailureGraceTicks = 151;

    /// <inheritdoc/>
    public string Name => "Mission session";

    /// <summary>Gets or sets the mission being played, or <see langword="null"/> on the front end.</summary>
    public MissionDescriptor? Mission { get; set; }

    /// <summary>Gets or sets the level task the mission's tree hangs off, until it is deleted.</summary>
    public LevelTask? LevelRoot { get; set; }

    /// <summary>Gets or sets whether the mission's success has been acted on, so it is acted on once.</summary>
    public bool ResultHandled { get; set; }

    /// <summary>Gets how many ticks in a row the mission has stood failed.</summary>
    public int FailureTicks { get; private set; }

    /// <summary>Gets or sets whether the failure page is up over the level.</summary>
    public bool FailedMenu { get; set; }

    /// <summary>Starts a mission's session in a level tree.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="levelRoot">The level task its tree hangs off.</param>
    public void Begin(MissionDescriptor mission, LevelTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);
        LevelRoot = levelRoot;
        Mission = mission;
        ResultHandled = false;
    }

    /// <summary>Forgets how the last mission was settling, as a mission being let go of does.</summary>
    public void ResetOutcome()
    {
        FailureTicks = 0;
        FailedMenu = false;
        ResultHandled = false;
    }

    /// <summary>Counts one more tick of the mission standing failed.</summary>
    /// <returns>Whether the failure page is due: it is not up yet and the retail grace period has passed.</returns>
    public bool CountFailure() => !FailedMenu && ++FailureTicks > FailureGraceTicks;

    /// <summary>Clears the count when the mission is no longer standing failed.</summary>
    public void ClearFailure() => FailureTicks = 0;

    /// <inheritdoc/>
    /// <remarks>Nothing of its own to free: the level tree is the loop's.</remarks>
    public void Dispose()
    {
    }
}

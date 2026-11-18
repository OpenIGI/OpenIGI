using OpenIGI.Core.Titles;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.TestSupport;

namespace OpenIGI.Parity.Tests;

/// <summary>Two I.G.I. 2 games playing one mission together (<see cref="CoopPair{TLevel}"/>), each level its own player's.</summary>
internal sealed class Igi2CoopPair : CoopPair<Igi2TitlePlayer>
{
    /// <summary>The campaign's first mission, as the menu starts it.</summary>
    public const int FirstMission = 11;

    /// <summary>Starts both games on a mission, the host's intro skipped; the joiner connects once the host's level is up.</summary>
    /// <param name="mission">The mission both play.</param>
    /// <param name="frames">How many frames each may run.</param>
    /// <param name="hostArgs">More of the host's command line.</param>
    public Igi2CoopPair(int mission = FirstMission, int frames = 20000, params string[] hostArgs)
        : base(GameDataSource.Igi2, GameTitle.Igi2, "igi2", null, null, mission, frames, hostArgs, joinerArgs: [], joinLater: false)
    {
    }

    /// <summary>
    /// Starts the host on a mission, and the joiner either alongside or not until <see cref="CoopPair{TLevel}.StartJoiner"/>.
    /// </summary>
    /// <param name="hostArgs">More of the host's command line.</param>
    /// <param name="joinerArgs">The joiner's extras, in place of the host's.</param>
    /// <param name="joinLater">Whether the joiner waits.</param>
    /// <param name="mission">The mission both play.</param>
    public Igi2CoopPair(string[] hostArgs, string[] joinerArgs, bool joinLater, int mission = FirstMission)
        : base(GameDataSource.Igi2, GameTitle.Igi2, "igi2", null, null, mission, 20000, hostArgs, joinerArgs, joinLater)
    {
    }

    /// <summary>Starts both games on a mission, each playing in a look of its own.</summary>
    /// <param name="hostLook">The host's look, a <see cref="OpenIGI.Game.Multiplayer.PlayerSkins"/> id.</param>
    /// <param name="joinerLook">The joiner's look.</param>
    /// <param name="mission">The mission both play.</param>
    public Igi2CoopPair(string hostLook, string joinerLook, int mission = FirstMission)
        : base(GameDataSource.Igi2, GameTitle.Igi2, "igi2", null, null, mission, 20000, [], [], joinLater: false, joinerLook: joinerLook, hostLook: hostLook)
    {
    }
}

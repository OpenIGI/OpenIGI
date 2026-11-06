using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>Everything a title's mission asks of whatever shows and plays it.</summary>
public interface IMissionHostServices : IMissionTickHost, IPlayerSetupHost, IPlayerHarmHost, IMissionSetupHost, IBlastEffectsHost, IMissionSoundHost, IImpactEffectsHost
{
    /// <summary>Tells the host the level was put back to where the match began: what it draws of the old round goes.</summary>
    void LevelWasReset()
    {
    }
}

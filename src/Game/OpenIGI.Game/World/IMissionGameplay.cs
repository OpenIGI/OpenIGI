using OpenIGI.Game.Player;

namespace OpenIGI.Game.World;

/// <summary>A title's own mission, as the host holds it: the thing its presenters are built over.</summary>
/// <remarks>
/// The host hands it to the title's presentation untouched; each title's presenters know their own mission's type and read
/// it through that.
/// </remarks>
public interface IMissionGameplay
{
    /// <summary>Gets the player the host steers, or <see langword="null"/> while none stands in the level.</summary>
    IHostedPlayer? Player { get; }
}

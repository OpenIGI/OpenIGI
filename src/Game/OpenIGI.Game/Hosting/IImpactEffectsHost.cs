using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Hosting;

/// <summary>What a title's own impact effects ask of the host: whether the player wants blood.</summary>
public interface IImpactEffectsHost
{
    /// <summary>Gets a value indicating whether the player's profile wants blood.</summary>
    bool BloodWanted { get; }
}

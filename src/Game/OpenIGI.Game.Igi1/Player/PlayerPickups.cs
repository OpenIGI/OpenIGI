using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The level's automatic pickups as the player walks into them: the weapons and the authored generic items,
/// each family asked every tick from the human's origin.
/// </summary>
internal sealed class PlayerPickups
{
    private readonly PlayerBody _body;
    private GunPickupRegistry? _gunPickups;
    private GenericPickupRegistry? _genericPickups;

    /// <summary>Initializes the pickups for one body.</summary>
    /// <param name="body">The body whose origin reaches the items.</param>
    internal PlayerPickups(PlayerBody body)
    {
        _body = body;
    }

    /// <summary>Raised for each weapon collected from the world this tick.</summary>
    internal event Action<GunPickup>? GunPickedUp;

    /// <summary>Raised for each authored generic item collected from the world this tick.</summary>
    internal event Action<GenericPickup>? GenericPickedUp;

    /// <summary>Gives the player the level's automatic weapon pickups.</summary>
    internal void UseGuns(GunPickupRegistry pickups) => _gunPickups = pickups;

    /// <summary>Gives the player the level's automatic generic item pickups.</summary>
    internal void UseGeneric(GenericPickupRegistry pickups) => _genericPickups = pickups;

    /// <summary>Collects whatever this tick's origin reaches.</summary>
    /// <param name="weapons">The weapon machine a weapon joins, or <see langword="null"/> before one exists.</param>
    internal void Collect(PlayerWeapons? weapons)
    {
        // Every pickup family answers the message 0x410F70 sends each tick with the Human's origin (+32), not the
        // feet: from the feet, an item on a crate a metre up was only in reach mid-jump.
        if (weapons is not null && _gunPickups is not null)
        {
            foreach (GunPickup pickup in _gunPickups.CollectAt(_body.Origin, weapons))
            {
                GunPickedUp?.Invoke(pickup);
            }
        }

        if (_genericPickups is { } genericPickups)
        {
            foreach (GenericPickup pickup in genericPickups.CollectAt(_body.Origin))
            {
                GenericPickedUp?.Invoke(pickup);
            }
        }
    }
}

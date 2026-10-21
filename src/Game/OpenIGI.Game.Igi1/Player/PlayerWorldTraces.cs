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
/// The world casts the player's own states make that are neither the body's sweep nor a round's: the plant
/// trace and the view model's wall probe, both over placed objects and the terrain together.
/// </summary>
internal static class PlayerWorldTraces
{
    // The world cast state 36 plants along (0x4CC2A0): objects and the ground, nearest first.
    internal static WorldTraceCast? Plant(ObjectCollisionIndex? walls, WorldTraceCast? terrain) => (walls, terrain) switch
    {
        ({ } objects, { } ground) => WorldTrace.Nearest(objects.Tracer(), ground),
        ({ } objects, null) => objects.Tracer(),
        (null, { } ground) => ground,
        _ => null,
    };

    // The cast the view model's forward clearance probe sweeps: the normal placed-object level and the terrain.
    internal static WorldRayCast ViewModel(ObjectCollisionIndex? walls, WorldTraceCast? terrain)
    {
        WorldTraceCast trace = (walls, terrain) switch
        {
            ({ } objects, { } ground) => WorldTrace.Nearest(objects.Tracer(), ground),
            ({ } objects, null) => objects.Tracer(),
            (null, { } ground) => ground,
            _ => throw new InvalidOperationException("The player has neither terrain nor object collision."),
        };
        return WorldTrace.AsRayCast(trace);
    }
}

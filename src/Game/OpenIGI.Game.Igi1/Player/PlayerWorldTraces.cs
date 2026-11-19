using OpenIGI.Game.World;

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

    // The cast a lean sweeps the eye along (0x464BD0): the same world as the view model's probe, or none when there is none.
    internal static WorldRayCast? Eye(ObjectCollisionIndex? walls, WorldTraceCast? terrain) =>
        walls is null && terrain is null ? null : ViewModel(walls, terrain);

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

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>The object-collision evaluate event (<c>GameFunctions_eObjectCollisionEvaluateQTaskEvent</c>) as the placed types answer it.</summary>
public static class Igi2ObjectCollision
{
    /// <summary>
    /// The types whose objects an object-collision trace passes through: <c>Wire</c> (<c>Wire_Open</c>, <c>igi2 0x514EF0</c>) and
    /// the three pickups (<c>GunPickup</c> <c>0x46BC60</c>, <c>AmmoPickup</c> <c>0x5276C0</c>, <c>GenericPickup</c> <c>0x4F7380</c>)
    /// answer the event with no.
    /// </summary>
    private static readonly HashSet<string> Through = new(StringComparer.Ordinal)
    {
        "Wire", "GunPickup", "AmmoPickup", "GenericPickup",
    };

    /// <summary>Gets whether an object-collision trace passes through what an owner places.</summary>
    /// <param name="owner">The struck object's owner.</param>
    /// <returns>Whether its type answers the evaluate event with no.</returns>
    public static bool PassesThrough(object owner) =>
        owner is Igi2CollisionOwner { Depth: 0 } placed && Through.Contains(placed.TypeName);
}

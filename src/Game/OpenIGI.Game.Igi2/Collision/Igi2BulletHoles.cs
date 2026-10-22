using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>Which solids take a mark where they are struck: the types that answer <c>CreateBulletHole</c>.</summary>
/// <remarks>
/// <c>BulletHole_Open</c> links the event to <c>RigidDynCubeObj</c>, <c>SplineObjSegment</c> and <c>BoneDynCubeObj</c>, and every type
/// deriving from them inherits it, except four that link it to nothing: <c>Switch</c>, <c>Wire</c>, <c>Rotor</c> and
/// <c>ElevatorFloorIndicator</c>. The ground and the trees are none of these, so they take no marks. A caller that draws its mark's size or
/// texture from the run stream draws them whether or not anything answers; only the mark itself depends on this.
/// </remarks>
public static class Igi2BulletHoles
{
    private static readonly HashSet<string> Unmarked = new(StringComparer.Ordinal)
    {
        "Switch", "Wire", "Rotor", "ElevatorFloorIndicator",
    };

    /// <summary>Whether a struck solid takes a mark.</summary>
    /// <param name="struck">What was struck.</param>
    /// <returns><see langword="true"/> when it answers <c>CreateBulletHole</c>.</returns>
    public static bool Takes(IWorldCollider struck) => struck switch
    {
        Igi2RigidObject { Group: Igi2CollisionWorld.ForestGroup } => false,
        Igi2RigidObject { Owner: Igi2CollisionOwner owner } => !Unmarked.Contains(owner.TypeName),
        Igi2RigidObject => true,
        Igi2SplineSegment => true,
        Igi2BoneCollider => true,
        _ => false,
    };
}

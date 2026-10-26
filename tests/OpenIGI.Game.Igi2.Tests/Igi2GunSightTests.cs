using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A mounted gun and the AI's lines of sight, <c>StationaryGun_AILineOfSightHandler</c>.</summary>
public sealed class Igi2GunSightTests
{
    private static readonly Igi2CollisionOwner GunSolid = new(Igi2StationaryGunTask.TypeName, 40, "gun_holder", 0);

    [Fact]
    public void AGunStandsAsideOnlyForTheOneWorkingIt()
    {
        object gunner = new();
        object other = new();
        StandIn sight = new();
        Igi2DetectionWorld world = new(sight, new Igi2GameMaterials())
        {
            GunOperatorOf = owner => ReferenceEquals(owner, GunSolid) ? gunner : null,
        };

        // A line from or to the gunner passes the gun; one between two others meets it.
        Assert.True(world.LineOfSight(Vector3d.Zero, new Vector3d(4096, 0, 0), 1u, out _, passes: gunner));
        Assert.False(world.LineOfSight(Vector3d.Zero, new Vector3d(4096, 0, 0), 1u, out _, passes: other));
    }

    [Fact]
    public void AnUnworkedGunStopsEveryLine()
    {
        object anyone = new();
        Igi2DetectionWorld world = new(new StandIn(), new Igi2GameMaterials())
        {
            GunOperatorOf = static _ => null,
        };

        Assert.False(world.LineOfSight(Vector3d.Zero, new Vector3d(4096, 0, 0), 1u, out _, passes: anyone));
    }

    // A world holding the one gun solid across every line: the line is clear when the line does not include it.
    private sealed class StandIn : IIgi2SightQuery
    {
        public bool LineOfSight(WorldLineOfSight sight, uint groups)
        {
            if (sight.Includes is { } includes && !includes(GunSolid))
            {
                return true;
            }

            sight.IsHit = true;
            sight.Transparency = 0f;
            return false;
        }
    }
}

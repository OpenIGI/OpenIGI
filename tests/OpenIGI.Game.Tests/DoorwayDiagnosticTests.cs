using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Portal faces of a collision mesh — the doorway and window openings — are non-solid, so a body walks
/// through them.
/// </summary>
/// <remarks>
/// A doorway is closed by a <b>portal polygon</b> in the collision mesh (for the room/visibility system),
/// whose material carries an <c>iPortal</c> index. The original's collision skips it; treating it as a
/// wall walled off the door. This pins the fix against level 1's <c>Building_1118</c> / model
/// <c>400_20_1</c>, whose doorway is a two-triangle portal at walking height — the exact spot the
/// in-game log reported blocked at world <c>(24501169, -55966720)</c>.
/// </remarks>
public sealed class DoorwayDiagnosticTests
{
    private const double U = 4096.0;

    // The player's feet at the doorway, from the "BLOCKED by 400_20_1" in-game log.
    private static readonly Vector3d Player = new(24501169, -55966720, 174413291);

    [RequiresGameDataFact]
    public void ABuildingDoorwayIsPassableAtWalkingHeight()
    {
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);

        // Walking into the door (+Y) at the grounded wall-sweep heights — waist and head — must be clear:
        // the doorway is a portal, not a wall. The ankle probe is skipped on the ground, so the low
        // threshold below it does not matter.
        foreach (double heightMetres in new[] { 0.4, 0.9, 1.6 })
        {
            Vector3d origin = new(Player.X, Player.Y, Player.Z + (heightMetres * U));
            Vector3d end = new(origin.X, origin.Y + (3.0 * U), origin.Z);

            Assert.False(
                index.TryCast(origin, end, out _),
                $"the doorway is walled off at {heightMetres:0.0} m — a portal face is being treated as solid");
        }
    }

    [RequiresGameDataFact]
    public void TheWallBesideTheDoorwayStillBlocks()
    {
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);

        // Skipping portals must not dissolve the building: a metre and a half to the right of the door,
        // head height, is solid wall and has to stop a body.
        Vector3d origin = new(Player.X + (1.5 * U), Player.Y, Player.Z + (1.6 * U));
        Vector3d end = new(origin.X, origin.Y + (3.0 * U), origin.Z);

        Assert.True(index.TryCast(origin, end, out _), "the wall beside the door should still be solid");
    }

    [RequiresGameDataFact]
    public void TheDoorwayBuildingCarriesPortalMaterials()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        WorldPlacement door = scene.Placements.First(p => p.Instance.Name == "400_20_1");

        Assert.True(MefCollisionMesh.TryRead(door.Model.Container, out MefCollisionMesh? mesh));

        // Its doorway/window openings are portals (material 7 is the door the log hit), and a body walks
        // through their faces.
        Assert.True(mesh!.IsPortal(7), "material 7 is the doorway portal");
        Assert.Contains(mesh.Faces.ToArray(), f => f.Material == 7 && mesh.IsPortal(f.Material));

        // A plain wall material stays solid.
        Assert.False(mesh.IsPortal(1), "material 1 is solid wall, not a portal");
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Rounds fired at level 1's own walls must leave marks through the full rule set: the struck object,
/// the material, and the four-corner test probing the same collision the round struck.
/// </summary>
public sealed class RetailWallMarkTests
{
    [RequiresGameDataFact]
    public void RoundsIntoLevelOnesWallsLeaveMarks()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(scene.Placements);
        WorldTraceCast rounds = index.Tracer();
        WorldRayCast probe = index.Caster();
        GameMaterialRegistry materials = RetailBulletImpactMaterialTests.LevelOneMaterials();

        int struck = 0;
        int marked = 0;
        foreach (WorldPlacement placement in scene.Placements.Where(p => !p.IsAttached).Take(200))
        {
            Vector3d centre = placement.Position * WorldScene.UnitsPerMetre;
            foreach ((double dx, double dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
            {
                Vector3d from = new(centre.X + (dx * 40000), centre.Y + (dy * 40000), centre.Z + 6000);
                Vector3d to = new(centre.X, centre.Y, centre.Z + 6000);
                if (!rounds(new WorldTraceRequest(from, to, WorldTraceMask.World), out WorldTraceHit hit)
                    || !materials.TryGet(hit.Material, out GameMaterial material)
                    || !material.RecordsImpacts)
                {
                    continue;
                }

                struck++;
                BulletMarks marks = new();
                if (marks.TryAdd(hit.RayHit, BulletMarks.SurfaceOf(hit), materials, probe: probe))
                {
                    marked++;
                }
            }
        }

        // Edges and small objects legitimately drop a mark, but a wall is mostly wall.
        Assert.True(struck > 100, $"only {struck} rounds met a markable wall");
        Assert.True(marked * 2 > struck, $"{marked} of {struck} rounds into walls left a mark");
    }
}

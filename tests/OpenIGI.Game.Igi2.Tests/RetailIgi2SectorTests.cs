using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The sectors an I.G.I. 2 level places, and what a portal leads to.</summary>
public sealed class RetailIgi2SectorTests
{
    /// <summary>Every sector's portal records index within the model that holds them.</summary>
    /// <remarks>
    /// The layouts are the sequel's own symbols — <c>Mesh3D_Portal_t</c> at 20 bytes,
    /// <c>Mesh3D_PortalFace_t</c> and <c>Mesh3D_PortalVertex_t</c> at 12 — so the question is not what the
    /// fields are but whether they are read in the right order. Every span closing inside its list, across
    /// the corpus, answers that: a transposed pair would put a count where an index goes and fail at once.
    /// <para>
    /// It also pins the shapes, because the obvious assumptions about them are both false.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_portal_indexes_within_its_own_model()
    {
        int sectors = 0;
        int portals = 0;
        int quads = 0;
        int strips = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2Sector sector in Graph(level).Sectors)
            {
                sectors++;
                foreach (MefPortal portal in sector.Portals.Portals)
                {
                    portals++;
                    Assert.InRange(portal.VertexCount, 1, sector.Portals.Vertices.Count);
                    Assert.InRange(portal.FaceCount, 1, sector.Portals.Faces.Count);
                    Assert.InRange(portal.Slot, 0, 63);

                    if (portal.VertexCount == 4 && portal.FaceCount == 2)
                    {
                        quads++;
                    }

                    if (portal.FaceCount != portal.VertexCount - 2)
                    {
                        strips++;
                    }

                    // The facing comes from the first face alone, so a degenerate one would leave a
                    // renderer with no plane to cull against.
                    Assert.NotEqual(default, sector.Portals.NormalOf(portal));
                }
            }
        }

        Assert.Equal(242, sectors);
        Assert.Equal(794, portals);

        // A portal is NOT always a quad, and NOT always a triangle fan. The quad is merely the commonest
        // shape; the rest run to 28 vertices and 22 triangles, and 41 have a face count that is not
        // `vertices - 2` -- v28 f14 is seven separate quads. Pinned in both directions because assuming
        // either would be wrong on a quarter of the corpus, and silently so: the portal would still
        // occlude, just with the wrong outline.
        Assert.Equal(602, quads);
        Assert.Equal(41, strips);
    }

    /// <summary>Sectors do not meet portal to portal, so linkage cannot be geometric.</summary>
    /// <remarks>
    /// <para>
    /// The load-bearing negative result. A portal is an opening in one sector's own shell; the room
    /// beyond has an opening of its own nearby, not in the same place. Of the 180 portals belonging to
    /// sectors placed without rotation — the ones whose world position needs no orientation convention to
    /// compute — <b>4 coincide with a portal of another sector</b>, and those are two meshes each placed
    /// twice.
    /// </para>
    /// <para>
    /// So <c>SectorObject_TraverseSector</c>'s slot matching is not an optimisation standing in for a
    /// geometric test. It is the only rule there is, and an implementation that "improved" on it by
    /// pairing portals in space would connect almost nothing.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Portals_of_different_sectors_do_not_coincide_in_space()
    {
        int upright = 0;
        int coincident = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            List<(int Id, Vector3d Centre)> centres = [];
            foreach (Igi2Sector sector in Graph(level).Sectors)
            {
                Igi2Placement placement = sector.Placement;
                if (Math.Abs(placement.Orientation.X) > 1e-6f
                    || Math.Abs(placement.Orientation.Y) > 1e-6f
                    || Math.Abs(placement.Orientation.Z) > 1e-6f)
                {
                    continue;
                }

                foreach (MefPortal portal in sector.Portals.Portals)
                {
                    upright++;
                    centres.Add((placement.LevelId, Centre(sector.Portals, portal, placement.Position)));
                }
            }

            for (int i = 0; i < centres.Count; i++)
            {
                for (int j = 0; j < centres.Count; j++)
                {
                    if (i != j && centres[i].Id != centres[j].Id && Near(centres[i].Centre, centres[j].Centre))
                    {
                        coincident++;
                        break;
                    }
                }
            }
        }

        Assert.Equal(199, upright);
        Assert.Equal(4, coincident);
    }

    /// <summary>A sector hangs under a container-shaped task, which is what groups the linkage.</summary>
    /// <remarks>
    /// The evidence for reading <c>ptSectorContainer</c> as the parent task. It is not proof — the
    /// field's assignment was not found in the binary — so this pins the observation instead, and will
    /// fail loudly if a level ever disagrees.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_sector_hangs_under_a_container()
    {
        Dictionary<string, int> parents = [];

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2Sector sector in Graph(level).Sectors)
            {
                parents.TryGetValue(sector.ContainerType, out int count);
                parents[sector.ContainerType] = count + 1;
            }
        }

        Assert.Equal(145, parents["Container"]);
        Assert.Equal(93, parents["ConditionalContainer"]);
        Assert.Equal(3, parents["Building"]);
        Assert.Equal(1, parents["Dynamic"]);
        Assert.Equal(4, parents.Count);
    }

    /// <summary>Stepping through a portal leads somewhere, and never back to where it started.</summary>
    /// <remarks>
    /// Exercises the traversal rule end to end. A sector with no neighbour through a given slot is
    /// ordinary — an outside wall's window has nowhere to lead — so the claim is that the links which do
    /// exist are well formed, and that enough of them exist for the rule to connect anything at all.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_portal_leads_to_other_sectors_and_never_to_itself()
    {
        int links = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2SectorGraph graph = Graph(level);
            foreach (Igi2Sector sector in graph.Sectors)
            {
                foreach (MefPortal portal in sector.Portals.Portals)
                {
                    foreach (Igi2Sector beyond in graph.Beyond(sector, portal.Slot))
                    {
                        links++;
                        Assert.NotEqual(sector.Placement.LevelId, beyond.Placement.LevelId);
                        Assert.Equal(sector.ContainerId, beyond.ContainerId);
                        Assert.Contains(beyond.Portals.Portals, p => p.Slot == portal.Slot);
                    }
                }
            }
        }

        Assert.True(links > 100, $"The slot rule produced only {links} links across the corpus.");
    }

    private static bool Near(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) < 1.0;
    }

    private static Vector3d Centre(MefPortalSet set, MefPortal portal, Vector3d origin)
    {
        double x = 0;
        double y = 0;
        double z = 0;
        for (int i = 0; i < portal.VertexCount; i++)
        {
            Vector3f vertex = set.Vertices[portal.FirstVertex + i];
            x += vertex.X;
            y += vertex.Y;
            z += vertex.Z;
        }

        return new Vector3d(
            origin.X + (x / portal.VertexCount),
            origin.Y + (y / portal.VertexCount),
            origin.Z + (z / portal.VertexCount));
    }

    private static Igi2SectorGraph Graph(Igi2Level level) =>
        Igi2SectorGraph.Build(
            level.Root,
            model => level.Table.TryResolveMesh(model, out string? mesh) && level.TryReadModel(mesh, out MefModel? read)
                ? read.Portals
                : null);
}

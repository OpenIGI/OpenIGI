using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The sector objects an I.G.I. 2 level places -- each building's shell and its rooms -- and what a portal leads to.</summary>
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
        List<string> degenerate = [];

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

                    // The facing comes from the first face alone, so a degenerate one leaves the walk no plane to
                    // test against, and the port refuses that portal.
                    if (sector.Portals.NormalOf(portal) == default)
                    {
                        degenerate.Add($"{sector.Model} slot {portal.Slot}");
                    }
                }
            }
        }

        // 491 shells and 375 rooms.
        Assert.Equal(866, sectors);
        Assert.Equal(1881, portals);

        // A portal is NOT always a quad, and NOT always a triangle fan. The quad is merely the commonest
        // shape; the rest run to 28 vertices, and some have a face count that is not `vertices - 2` --
        // v28 f14 is seven separate quads. Pinned in both directions because assuming either would be
        // wrong on a good part of the corpus, and silently so: the portal would still occlude, just with
        // the wrong outline.
        Assert.Equal(1391, quads);
        Assert.Equal(83, strips);

        // One portal in the corpus has a first face with no area: a room of the multiplayer map sandstorm.
        Assert.Equal(["439_02_1 slot 0"], degenerate);
    }

    /// <summary>A building is a shell and the rooms its model attaches; nothing else is a sector object.</summary>
    /// <remarks>
    /// <c>SectorObject_CreateAttachObjects</c> moves every attach object derived from <c>Sector</c> into the building's
    /// <c>ptSectorContainer</c>; the port's reading -- an attach point the magic table does not hold, whose model has portals --
    /// finds 375 of them in 230 buildings. A room never comes without a shell, and every shell that has portals has rooms to
    /// lead into, bar the handful whose openings give onto nothing the building carries.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_building_is_a_shell_and_its_rooms()
    {
        int buildings = 0;
        int withRooms = 0;
        int rooms = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2SectorGraph graph = Graph(level);
            foreach (Igi2SectorObject building in graph.Objects)
            {
                buildings++;
                Assert.True(building.Shell.IsShell);
                Assert.Same(building.Shell, graph.Sectors[building.Shell.Index]);
                for (int i = 0; i < building.Rooms.Count; i++)
                {
                    Igi2Sector room = building.Rooms[i];
                    Assert.Equal(i, room.Room);
                    Assert.Equal(building.Index, room.ObjectIndex);
                    Assert.NotEmpty(room.Portals.Portals);
                }

                rooms += building.Rooms.Count;
                withRooms += building.Rooms.Count > 0 ? 1 : 0;
            }
        }

        Assert.Equal(491, buildings);
        Assert.Equal(230, withRooms);
        Assert.Equal(375, rooms);
    }

    /// <summary>A room's opening and its shell's opening of the same slot are one doorway, though not one polygon.</summary>
    /// <remarks>
    /// The slot is the whole link, so the claim measured here is only the reading behind it: a room's portal and its shell's
    /// portal numbered alike stand at the same opening, nearly always. Their centres are compared in the world, within a metre.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_room_and_its_shell_number_one_opening_alike()
    {
        int paired = 0;
        int together = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2SectorObject building in Graph(level).Objects)
            {
                foreach (Igi2Sector room in building.Rooms)
                {
                    foreach (MefPortal portal in room.Portals.Portals)
                    {
                        int outside = building.Shell.Portals.Portals.ToList().FindIndex(p => p.Slot == portal.Slot);
                        if (outside < 0)
                        {
                            continue;
                        }

                        paired++;
                        Vector3d a = Centre(room, portal);
                        Vector3d b = Centre(building.Shell, building.Shell.Portals.Portals[outside]);
                        if ((a - b).Length < 4096.0)
                        {
                            together++;
                        }
                    }
                }
            }
        }

        // 783 room portals have a shell portal of the same slot, and 766 of those stand within a metre of it; the other 17 are
        // openings the author numbered alike a little further apart -- a slot is a name, not a place.
        Assert.Equal(783, paired);
        Assert.Equal(766, together);
    }

    /// <summary>Stepping through a portal leads somewhere in the same building, and never back to where it started.</summary>
    /// <remarks>
    /// Exercises the traversal rule end to end. A sector with no neighbour through a given slot is
    /// ordinary — a room's window onto a wall the shell has no opening in has nowhere to lead — so the claim is that the links
    /// which do exist are well formed, and that enough of them exist for the rule to connect anything at all.
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
                        Assert.NotEqual(sector.Index, beyond.Index);
                        Assert.Equal(sector.ObjectIndex, beyond.ObjectIndex);
                        Assert.Contains(beyond.Portals.Portals, p => p.Slot == portal.Slot);
                    }
                }
            }
        }

        Assert.True(links > 1000, $"The slot rule produced only {links} links across the corpus.");
    }

    // A portal's centre in the world: the mean of its vertices, turned by the sector's rows and placed at it.
    private static Vector3d Centre(Igi2Sector sector, MefPortal portal)
    {
        Vector3f sum = Vector3f.Zero;
        for (int i = 0; i < portal.VertexCount; i++)
        {
            sum += sector.Portals.Vertices[portal.FirstVertex + i];
        }

        Vector3f local = sector.Orientation.Transposed().Transform(sum / portal.VertexCount);
        return new Vector3d(sector.Position.X + local.X, sector.Position.Y + local.Y, sector.Position.Z + local.Z);
    }

    private static Igi2SectorGraph Graph(Igi2Level level) => Igi2SectorGraph.Build(level.Scene, level.ModelFor);
}

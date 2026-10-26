using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Sectors;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// I.G.I. 2's portal culling, run over the shipped levels.
/// </summary>
/// <remarks>
/// The questions these answer are the ones a port of <c>Sector_IsPortalVisible</c>
/// (<c>igi2.exe 0x587D10</c>) can get silently wrong: whether the index arithmetic on a portal's faces
/// holds on real data, whether the back-face relief is the distance the binary's constant says it is and
/// is measured to the thing it is measured to, and whether the traversal stays inside the building
/// it started in.
/// </remarks>
public sealed class RetailIgi2PortalCullingTests
{
    private const float TangentHalfFovX = 0.75f;

    private static readonly SectorScreenRect Viewport = new(0f, 0f, 639f, 479f);

    /// <summary>A portal face names a vertex of its own portal, which is what lets the index arithmetic work.</summary>
    /// <remarks>
    /// <c>Sector_IsPortalVisible</c> transforms only the portal's own vertex slice into a scratch array
    /// and then indexes it with <c>face.iPortalVertex − portal.iStartVertex</c> (<c>igi2.exe 0x58836F</c>),
    /// with no bound check. That only works if every face of a portal names vertices inside that portal's
    /// slice — the file format merely guarantees they are inside the <em>model's</em> whole list. So the
    /// stronger claim is measured here rather than assumed, and the port's skip for an out-of-slice face
    /// is dead code on shipped data.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_portal_face_names_a_vertex_of_its_own_portal()
    {
        int faces = 0;
        int outside = 0;
        int widest = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2Sector sector in Graph(level).Sectors)
            {
                foreach (MefPortal portal in sector.Portals.Portals)
                {
                    widest = Math.Max(widest, portal.VertexCount);
                    for (int f = 0; f < portal.FaceCount; f++)
                    {
                        MefPortalFace face = sector.Portals.Faces[portal.FirstFace + f];
                        faces++;
                        if (Outside(face.Vertex0, portal)
                            || Outside(face.Vertex1, portal)
                            || Outside(face.Vertex2, portal))
                        {
                            outside++;
                        }
                    }
                }
            }
        }

        // 6,802 portal triangles across the 24 shipped levels' shells and rooms, and not one of them
        // reaches outside the portal that owns it.
        Assert.Equal(6802, faces);
        Assert.Equal(0, outside);

        // The scratch array the original transforms into holds 100 vertices with no bound check. The
        // widest portal shipped is well inside that, which is why the port records the limit instead of
        // enforcing it.
        Assert.Equal(28, widest);
        Assert.True(widest < Igi2PortalCulling.VertexScratchCapacity);
    }

    /// <summary>The back-face relief is 409.6 units, and it is a distance to the plane.</summary>
    /// <remarks>
    /// <para>
    /// The constant is <c>0x4823D70B</c> — <c>167772.16f</c>, whose square root is <c>409.6</c> exactly —
    /// compared at <c>igi2.exe 0x5880C8</c> against a squared distance. This puts a camera on the side
    /// the portal's normal points at, square on to its centre, at 409.0 units and at 410.2, and the
    /// answer has to flip.
    /// </para>
    /// <para>
    /// Only portals whose normal is within 60° of horizontal are used, because the camera basis is built
    /// from a yaw and a pitch and a straight-down normal has no yaw. That is a limit of the test's
    /// camera, not of the code under test.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_back_face_relief_admits_a_camera_at_409_and_refuses_one_at_410()
    {
        int tested = 0;
        int admittedInside = 0;
        int refusedOutside = 0;

        foreach (PlacedPortal portal in HorizontalPortals())
        {
            tested++;

            if (Visible(portal, 409.0f, 0f))
            {
                admittedInside++;
            }

            if (!Visible(portal, 410.2f, 0f))
            {
                refusedOutside++;
            }
        }

        // 1,725 of the corpus's 1,881 portals stand within 60 degrees of upright. Every one of them behaves
        // the same way, which is what makes 409.6 a constant of the algorithm rather than of one mesh.
        Assert.Equal(1725, tested);
        Assert.Equal(tested, admittedInside);
        Assert.Equal(tested, refusedOutside);
    }

    /// <summary>Sliding along the portal's plane does not use up the relief.</summary>
    /// <remarks>
    /// The load-bearing consequence of what <c>Real32x3_ProjectPointToPlane</c> (<c>igi2.exe 0x561A70</c>)
    /// collapses to. The original projects the camera-relative plane point onto the plane and squares the
    /// distance to the projection, which is <c>dot(portalPoint − camera, planeNormal)²</c> — a
    /// perpendicular distance to the <em>plane</em>. A camera 400 units in front of a portal and 2,000
    /// units to one side is 2,040 units from the portal itself and still well inside the relief, and this
    /// pins that: read as a distance to the portal, every one of these would be refused.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_relief_measures_to_the_plane_and_not_to_the_portal()
    {
        int tested = 0;
        int admitted = 0;

        foreach (PlacedPortal portal in HorizontalPortals())
        {
            tested++;
            if (Visible(portal, 400f, 2000f))
            {
                admitted++;
            }
        }

        Assert.Equal(1725, tested);
        Assert.Equal(tested, admitted);
    }

    /// <summary>A camera looking through a portal reaches what is beyond it, through a narrowed rectangle.</summary>
    /// <remarks>
    /// <para>
    /// The traversal end to end, driven from a view that is actually looking at something: for every
    /// portal in the corpus the camera stands five units back from its plane, on the side the plane test
    /// admits without needing the relief, aimed at the portal's centre. That is the case the algorithm
    /// exists for, and it is the one an origin-and-a-fixed-heading sweep almost never produces.
    /// </para>
    /// <para>
    /// The totals are pinned because they are the only thing that would notice a portal that stopped
    /// occluding or a link that stopped being followed. The invariants checked on every result are the
    /// three the port's structure has to guarantee: the camera's own sector comes first with the whole
    /// display area, nothing outside the starting sector's building is ever reached, and no clip window
    /// leaves the display area.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_camera_looking_through_a_portal_reaches_what_is_beyond_it()
    {
        SectorScreenProjection projection = SectorScreenProjection.FromViewport(Viewport, TangentHalfFovX);
        int views = 0;
        int reached = 0;
        int windows = 0;
        int withNeighbours = 0;
        int narrowed = 0;

        foreach (PlacedPortal portal in HorizontalPortals())
        {
            views++;
            Igi2PortalCulling culling = new(portal.Graph);
            IReadOnlyList<Igi2VisibleSector> visible =
                culling.Visit(LookingThrough(portal), projection, portal.Index);

            // SectorObject_EnterHandler seeds the first request with the whole display area, so the
            // sector the camera stands in is always first and always sees all of it.
            Assert.Equal(portal.Index, visible[0].Index);
            Assert.Equal(projection.DisplayArea, visible[0].ClipWindows[0]);

            reached += visible.Count;
            if (visible.Count > 1)
            {
                withNeighbours++;
            }

            foreach (Igi2VisibleSector entry in visible)
            {
                // The candidate set is the starting sector's building for the whole walk, so nothing
                // outside it can be reached however many portal slots line up.
                Assert.Equal(portal.Sector.ObjectIndex, entry.Sector.ObjectIndex);

                windows += entry.ClipWindows.Count;
                foreach (SectorScreenRect window in entry.ClipWindows)
                {
                    // Every rectangle is a portal's clipped box intersected with the one it was seen
                    // through, so it can never leave the display area.
                    Assert.True(
                        window.MinX >= projection.DisplayArea.MinX
                        && window.MinY >= projection.DisplayArea.MinY
                        && window.MaxX <= projection.DisplayArea.MaxX
                        && window.MaxY <= projection.DisplayArea.MaxY,
                        $"{entry} escaped the display area.");

                    if (entry.Index != portal.Index && window != projection.DisplayArea)
                    {
                        narrowed++;
                    }
                }
            }
        }

        // 1,725 views, one per portal of a shell or a room that stands within 60 degrees of upright.
        // `reached` counts sector requests including the camera's own -- 3,731 over 1,725 walks, a shade
        // over two sectors per view, because a walk never leaves its building and most buildings are a
        // shell and one room. `windows` counts the rectangles those requests carry once a sector holding
        // more than four is left with their union, as SectorObject_EnterHandler leaves it. 1,722 of the
        // views see past their own sector: a room's opening and its shell's are numbered alike, so a
        // portal leads somewhere almost without exception.
        Assert.Equal(1725, views);
        //
        // The counts fell (from 3,915 and 4,789) when the near clip stopped dropping its own interpolated point at the screen's
        // corner: SectorObject_CheckTriangle reprojects that point with no near test, and the stray corner had widened boxes.
        Assert.Equal(3731, reached);
        Assert.Equal(4438, windows);
        Assert.Equal(1722, withNeighbours);

        // The point of the whole algorithm: what is beyond a portal is drawn through less than the whole
        // screen. The camera's own sector holds exactly one window each -- it is on the discard list for
        // its entire walk, so nothing can re-request it -- leaving 2,713 windows that belong to a sector
        // reached through a portal. All but 33 of them are strictly smaller than the display area; those
        // 33 are portals the camera stands close enough to that their outline covers it.
        Assert.Equal(2680, narrowed);
    }

    /// <summary>The traversal's answer does not depend on the port's own recursion budget.</summary>
    /// <remarks>
    /// The budget is a port-only guard — <c>SectorObject_RequestSector</c> recurses unconditionally and
    /// is bounded only by the discard list, so the original explores every simple path. This runs the
    /// same sweep at an eighth of the budget and checks the totals are unchanged, which is the evidence
    /// that the pinned numbers above are the traversal's own and not the cap's.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_traversal_budget_never_binds_on_the_shipped_levels()
    {
        SectorScreenProjection projection = SectorScreenProjection.FromViewport(Viewport, TangentHalfFovX);
        int reached = 0;
        int windows = 0;

        foreach (PlacedPortal portal in HorizontalPortals())
        {
            Igi2PortalCulling culling = new(portal.Graph);
            IReadOnlyList<Igi2VisibleSector> visible = culling.Visit(
                LookingThrough(portal),
                projection,
                portal.Index,
                isPortalOpen: null,
                traversalBudget: Igi2PortalCulling.DefaultTraversalBudget / 8);

            reached += visible.Count;
            windows += visible.Sum(static v => v.ClipWindows.Count);
        }

        Assert.Equal(3731, reached);
        Assert.Equal(4438, windows);
    }

    /// <summary>A closed portal admits nothing, which is the whole of what a door drives.</summary>
    /// <remarks>
    /// Step one of <c>Sector_IsPortalVisible</c> is a byte per slot on the object
    /// (<c>igi2.exe 0x587D56</c>; <c>Sector_SetPortalOpen</c> at <c>igi2ed.exe 0x42AFD0</c> writes it).
    /// The port takes that state through a delegate, so this checks the delegate is honoured rather than
    /// the byte's storage: closing everything must leave every walk with nothing but its own sector.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Closing_every_portal_leaves_a_camera_with_only_its_own_sector()
    {
        SectorScreenProjection projection = SectorScreenProjection.FromViewport(Viewport, TangentHalfFovX);
        int starts = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2SectorGraph graph = Graph(level);
            Igi2PortalCulling culling = new(graph);

            for (int i = 0; i < graph.Sectors.Count; i++)
            {
                starts++;
                Camera3D camera = new(graph.Sectors[i].Position, Camera3D.LookRotation(0f, 0f));
                IReadOnlyList<Igi2VisibleSector> visible =
                    culling.Visit(camera, projection, i, static (_, _) => false);

                Igi2VisibleSector only = Assert.Single(visible);
                Assert.Equal(i, only.Index);
            }
        }

        Assert.Equal(866, starts);
    }

    private static bool Outside(int vertex, MefPortal portal) =>
        vertex < portal.FirstVertex || vertex >= portal.FirstVertex + portal.VertexCount;

    /// <summary>Places a camera at a chosen distance from a portal's plane, facing it, and asks.</summary>
    /// <param name="portal">The portal, with the sector that supplies its world transform.</param>
    /// <param name="ahead">
    /// How far along the normal to stand, measured from the plane point the original itself uses — the
    /// first face's vertex 0 — so that this is exactly the perpendicular distance the relief compares.
    /// Measuring from the portal's centre instead would be wrong on the portals that are several
    /// separate quads and so are not planar.
    /// </param>
    /// <param name="sideways">How far along the plane to slide, which changes no perpendicular distance.</param>
    /// <returns>Whether anything can be seen through the portal.</returns>
    private static bool Visible(PlacedPortal portal, float ahead, float sideways) =>
        Igi2PortalCulling.TryGetPortalBounds(
            Facing(portal, ahead, sideways),
            SectorScreenProjection.FromViewport(Viewport, TangentHalfFovX),
            portal.Sector,
            portal.Record,
            Viewport,
            out _);

    /// <summary>Stands five units back on the side the plane test admits, looking at the portal.</summary>
    /// <remarks>
    /// The negated normal, because <c>Sector_IsPortalVisible</c> only reaches its distance test when the
    /// camera is on the side the normal points at (<c>igi2.exe 0x588031</c>). Standing on the other side
    /// is the ordinary case, and the one a room's own doorway presents.
    /// </remarks>
    private static Camera3D LookingThrough(PlacedPortal portal) => Facing(portal, -5f, 0f);

    private static Camera3D Facing(PlacedPortal portal, float ahead, float sideways)
    {
        Vector3f along = Vector3f.Cross(portal.Normal, new Vector3f(0f, 0f, 1f)).Normalized();
        Vector3d eye = new(
            portal.Plane.X + (portal.Normal.X * ahead) + (along.X * sideways),
            portal.Plane.Y + (portal.Normal.Y * ahead) + (along.Y * sideways),
            portal.Plane.Z + (portal.Normal.Z * ahead) + (along.Z * sideways));

        Vector3f forward = new Vector3f(
            (float)(portal.Centre.X - eye.X),
            (float)(portal.Centre.Y - eye.Y),
            (float)(portal.Centre.Z - eye.Z)).Normalized();

        return new Camera3D(
            eye,
            Camera3D.LookRotation(MathF.Atan2(forward.X, forward.Y), MathF.Asin(Math.Clamp(forward.Z, -1f, 1f))));
    }

    /// <summary>One portal of one placed sector, with everything these tests need of it.</summary>
    /// <param name="Graph">The level's sectors, so a traversal can start here.</param>
    /// <param name="Index">The owning sector's index in that graph.</param>
    /// <param name="Sector">The sector that places it.</param>
    /// <param name="Record">The portal record.</param>
    /// <param name="Centre">The mean of its vertices in the world, which the camera aims at.</param>
    /// <param name="Plane">Its plane point in the world: the first face's vertex 0, as the original uses.</param>
    /// <param name="Normal">Its unit world normal, crossed the way the original crosses it.</param>
    private readonly record struct PlacedPortal(
        Igi2SectorGraph Graph,
        int Index,
        Igi2Sector Sector,
        MefPortal Record,
        Vector3d Centre,
        Vector3d Plane,
        Vector3f Normal);

    /// <summary>Every portal in the corpus whose normal stands within 60 degrees of horizontal.</summary>
    private static IEnumerable<PlacedPortal> HorizontalPortals()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2SectorGraph graph = Graph(level);
            for (int index = 0; index < graph.Sectors.Count; index++)
            {
                Igi2Sector sector = graph.Sectors[index];

                // The same rotation the culling applies: Matrix3f.Transform is the row-vector product, so
                // transposing first gives the `orientation · v` the original's object matrix is applied as.
                Matrix3f rotate = sector.Orientation.Transposed();

                foreach (MefPortal portal in sector.Portals.Portals)
                {
                    // The first face alone, crossed from its vertex 0 -- NOT MefPortalSet.NormalOf, which
                    // crosses from vertex 1 and so comes out with the opposite sign. The sign decides
                    // which side of the portal the relief applies on, so it has to be this one.
                    MefPortalFace first = sector.Portals.Faces[portal.FirstFace];
                    Vector3f p0 = sector.Portals.Vertices[first.Vertex0];
                    Vector3f normal = rotate.Transform(Vector3f.Cross(
                        sector.Portals.Vertices[first.Vertex1] - p0,
                        sector.Portals.Vertices[first.Vertex2] - p0)).Normalized();

                    if (normal == Vector3f.Zero || MathF.Abs(normal.Z) > 0.5f)
                    {
                        continue;
                    }

                    Vector3f mean = Vector3f.Zero;
                    for (int i = 0; i < portal.VertexCount; i++)
                    {
                        mean += sector.Portals.Vertices[portal.FirstVertex + i];
                    }

                    Vector3f local = rotate.Transform(mean / portal.VertexCount);
                    Vector3f planePoint = rotate.Transform(p0);

                    yield return new PlacedPortal(
                        graph,
                        index,
                        sector,
                        portal,
                        World(sector.Position, local),
                        World(sector.Position, planePoint),
                        normal);
                }
            }
        }
    }

    private static Vector3d World(Vector3d origin, Vector3f offset) =>
        new(origin.X + offset.X, origin.Y + offset.Y, origin.Z + offset.Z);

    private static Igi2SectorGraph Graph(Igi2Level level) => Igi2SectorGraph.Build(level.Scene, level.ModelFor);
}

using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World.Sectors;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One sector a traversal reached, and the screen rectangles it was reached through.</summary>
/// <remarks>
/// The original's request record: <c>SectorObject_RequestSector</c> (<c>igi2.exe 0x588470</c>) keeps a
/// pool entry per sector holding the union of every rectangle that reached it, and pushes each
/// individual rectangle onto the sector's own <c>ptClipWindowsList</c>. Both are kept here because both
/// are used — <c>SectorObject_DrawSector</c> hands the list to the clipper and the union to the
/// enclosing window.
/// </remarks>
public sealed class Igi2VisibleSector
{
    private readonly List<SectorScreenRect> _windows = [];

    internal Igi2VisibleSector(int index, Igi2Sector sector, SectorScreenRect first)
    {
        Index = index;
        Sector = sector;
        Bounds = first;
        _windows.Add(first);
    }

    /// <summary>Gets the sector's index in <see cref="Igi2SectorGraph.Sectors"/>.</summary>
    public int Index { get; }

    /// <summary>Gets the sector itself.</summary>
    public Igi2Sector Sector { get; }

    /// <summary>Gets the union of every rectangle this sector was reached through.</summary>
    public SectorScreenRect Bounds { get; private set; }

    /// <summary>Gets each rectangle this sector was reached through, in the order it was reached.</summary>
    /// <remarks>
    /// At most <see cref="Igi2PortalCulling.MaxClipWindows"/> once the walk is over: a sector reached through more is left with the
    /// one rectangle that encloses them all, as <c>SectorObject_EnterHandler</c> leaves it.
    /// </remarks>
    public IReadOnlyList<SectorScreenRect> ClipWindows => _windows;

    /// <inheritdoc/>
    public override string ToString() =>
        $"sector {Index} ({Sector.Model}), {_windows.Count} windows, {Bounds}";

    internal void Add(SectorScreenRect window)
    {
        _windows.Add(window);
        Bounds = Bounds.Union(window);
    }

    // SectorObject_EnterHandler, once every request is in: a list longer than four is emptied and given the request's union.
    internal void Collapse()
    {
        if (_windows.Count > Igi2PortalCulling.MaxClipWindows)
        {
            _windows.Clear();
            _windows.Add(Bounds);
        }
    }
}

/// <summary>
/// I.G.I. 2's portal culling: which sectors a camera can see from the one it stands in, and through
/// what part of the screen.
/// </summary>
/// <remarks>
/// <para>
/// The runtime half of <see cref="Igi2SectorGraph"/>, ported from three functions of the shipped
/// binaries: <c>Sector_IsPortalVisible</c> (<c>igi2.exe 0x587D10</c>, <c>igi2ed.exe 0x428850</c>),
/// <c>SectorObject_TraverseSector</c> (<c>0x588830</c>) and <c>SectorObject_RequestSector</c>
/// (<c>0x588470</c>), with the entry point <c>SectorObject_EnterHandler</c> (<c>0x588A70</c>) supplying
/// the starting rectangle. The dedicated server and the editor carry the same code; the editor's copy
/// differs only by a call to <c>Sector_DrawPortal</c> that draws the outline it just computed.
/// </para>
/// <para>
/// <b>This is rectangle narrowing, not frustum clipping.</b> A portal is projected, its triangles are
/// clipped to the screen, their bounding box is taken, and that box is intersected with the rectangle
/// the portal was itself seen through. Nothing carries a plane set forward; the whole state passed
/// between sectors is four floats.
/// </para>
/// <para>
/// <b>What is deliberately not modelled.</b> The original picks a level of detail per candidate
/// (<c>ModelObj_GetLOD</c>) and skips one that returns <c>-1</c>; it also skips a portal whose
/// per-object status byte is zero, which is what a door drives
/// (<c>Sector_SetPortalOpen</c>, <c>igi2ed.exe 0x42AFD0</c>, writes <c>ptSector + iPortal + 352</c>).
/// LOD is ignored, and the open/closed state arrives through the <c>isPortalOpen</c> delegate, which
/// <see cref="Igi2SectorPortals.IsOpen"/> answers from the doors.
/// </para>
/// </remarks>
public sealed class Igi2PortalCulling
{
    /// <summary>The view-space depth below which a vertex counts as behind the near plane: <see cref="SectorPortalProjection.NearPlaneZ"/>.</summary>
    public const float NearPlaneZ = SectorPortalProjection.NearPlaneZ;

    /// <summary>How far behind a portal's plane the camera may stand and still see through it: <see cref="SectorPortalProjection.BackFaceRelief"/>.</summary>
    public const float BackFaceRelief = SectorPortalProjection.BackFaceRelief;

    /// <summary>The most vertices the original can transform for one portal: <see cref="SectorPortalProjection.VertexScratchCapacity"/>.</summary>
    public const int VertexScratchCapacity = SectorPortalProjection.VertexScratchCapacity;

    /// <summary>How many sector traversals one <c>Visit</c> will run before it stops.</summary>
    /// <remarks>
    /// <b>A port-only guard; the original has none.</b> <c>SectorObject_RequestSector</c> recurses
    /// unconditionally even when the sector already has a request, so the only thing bounding the
    /// original is the discard list, which forbids a sector from re-entering itself on the path it is
    /// already on. That makes the traversal explore every simple path through a container's sectors,
    /// which is factorial in the worst case and merely fine on the shipped levels. This cap keeps a
    /// pathological level from hanging a frame.
    /// </remarks>
    public const int DefaultTraversalBudget = 4096;

    /// <summary>The most clip windows a sector keeps once a walk is over.</summary>
    /// <remarks>
    /// <c>SectorObject_EnterHandler</c> empties any requested sector's <c>ptClipWindowsList</c> holding more than four and pushes
    /// the request's union in their place, before it draws.
    /// </remarks>
    public const int MaxClipWindows = 4;


    private readonly Igi2SectorGraph _graph;

    /// <summary>Prepares to cull against one level's sectors.</summary>
    /// <param name="graph">The level's sectors and their portals.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public Igi2PortalCulling(Igi2SectorGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    /// <summary>Gets the graph this culls against.</summary>
    public Igi2SectorGraph Graph => _graph;

    /// <summary>Walks the sectors a camera can see from the one it stands in.</summary>
    /// <param name="camera">Where the viewer is and which way it faces.</param>
    /// <param name="projection">How view space becomes pixels, and the screen rectangle to start from.</param>
    /// <param name="startSector">The index into <see cref="Igi2SectorGraph.Sectors"/> of the sector the camera is in.</param>
    /// <param name="isPortalOpen">
    /// Answers whether a sector's portal slot is open, given the sector's index and the slot. The
    /// original reads a byte per slot on the object and refuses a closed one outright
    /// (<c>igi2.exe 0x587D56</c>). <see langword="null"/> treats every portal as open.
    /// </param>
    /// <param name="traversalBudget">
    /// How many sector traversals to run before stopping; see <see cref="DefaultTraversalBudget"/>.
    /// </param>
    /// <returns>
    /// Every sector reached, in the order the original's request pool would hold them — which is the
    /// order <c>SectorObject_EnterHandler</c> then draws them in.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startSector"/> is not a sector of this level.</exception>
    /// <remarks>
    /// The starting rectangle is the whole display area, which is what
    /// <c>SectorObject_EnterHandler</c> seeds its first request with (<c>igi2.exe 0x588C27</c>).
    /// </remarks>
    public IReadOnlyList<Igi2VisibleSector> Visit(
        Camera3D camera,
        SectorScreenProjection projection,
        int startSector,
        Func<int, int, bool>? isPortalOpen = null,
        int traversalBudget = DefaultTraversalBudget) =>
        Visit(camera, projection, startSector, [projection.DisplayArea], isPortalOpen, traversalBudget);

    /// <summary>Walks the sectors a camera can see from one sector, starting through each of several rectangles.</summary>
    /// <param name="camera">Where the viewer is and which way it faces.</param>
    /// <param name="projection">How view space becomes pixels.</param>
    /// <param name="startSector">The index into <see cref="Igi2SectorGraph.Sectors"/> of the sector the walk starts in.</param>
    /// <param name="startWindows">
    /// The rectangles the starting sector is seen through. The whole display area for a building drawn from outside or from
    /// within; the outside's own windows for a building seen out of another one's.
    /// </param>
    /// <param name="isPortalOpen">Answers whether a sector's portal slot is open; see the other overload.</param>
    /// <param name="traversalBudget">How many sector traversals to run before stopping.</param>
    /// <returns>Every sector reached, in request order, each with the rectangles it was reached through.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startSector"/> is not a sector of this level.</exception>
    /// <remarks>
    /// <para>
    /// <c>SectorObject_EnterHandler</c> (<c>igi2.exe 0x588A70</c>): the building the camera stands in is started from the camera's
    /// room through the whole display area, and so is any building while the camera stands in none. A building seen while the
    /// camera stands in another one is started from its shell once per clip window the outside was reached through, all into one
    /// request pool. A sector left holding more than <see cref="MaxClipWindows"/> windows is given their union instead.
    /// </para>
    /// <para>
    /// <b>The candidate set is the starting sector's building, for the whole walk.</b> <c>SectorObject_TraverseSector</c> begins
    /// its candidate loop at <c>SectorObject_ptActiveSectorObject</c> -- the building being drawn, whose shell is a sector in its
    /// own right -- and then walks that building's <c>ptSectorContainer</c>, its rooms, however deep the recursion has gone. So
    /// no walk leaves the building it started in.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Igi2VisibleSector> Visit(
        Camera3D camera,
        SectorScreenProjection projection,
        int startSector,
        IReadOnlyList<SectorScreenRect> startWindows,
        Func<int, int, bool>? isPortalOpen = null,
        int traversalBudget = DefaultTraversalBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startSector);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startSector, _graph.Sectors.Count);
        ArgumentNullException.ThrowIfNull(startWindows);

        Walk walk = new(_graph, camera, projection, isPortalOpen, traversalBudget)
        {
            Building = _graph.Objects[_graph.Sectors[startSector].ObjectIndex],
        };

        foreach (SectorScreenRect window in startWindows)
        {
            walk.Request(startSector, window);
        }

        foreach (Igi2VisibleSector visible in walk.Requests)
        {
            visible.Collapse();
        }

        return walk.Requests;
    }

    /// <summary>Projects one portal of a sector and returns the screen box it covers, or refuses it.</summary>
    /// <param name="camera">Where the viewer is and which way it faces.</param>
    /// <param name="projection">How view space becomes pixels.</param>
    /// <param name="sector">The sector the portal belongs to, which supplies its world transform.</param>
    /// <param name="portal">The portal.</param>
    /// <param name="through">The rectangle the sector itself is being seen through.</param>
    /// <param name="bounds">Receives the portal's own screen box; see <see cref="SectorPortalProjection.TryGetPortalBounds"/>.</param>
    /// <returns><see langword="false"/> when nothing can be seen through this portal.</returns>
    public static bool TryGetPortalBounds(
        Camera3D camera,
        SectorScreenProjection projection,
        Igi2Sector sector,
        MefPortal portal,
        SectorScreenRect through,
        out SectorScreenRect bounds)
    {
        ArgumentNullException.ThrowIfNull(sector);
        return SectorPortalProjection.TryGetPortalBounds(
            camera, projection, sector.Position, sector.Orientation, sector.Portals, portal, through, out bounds);
    }

    /// <summary>One run of the traversal, holding the state the original keeps in globals.</summary>
    private sealed class Walk(
        Igi2SectorGraph graph,
        Camera3D camera,
        SectorScreenProjection projection,
        Func<int, int, bool>? isPortalOpen,
        int budget)
    {
        private readonly Dictionary<int, Igi2VisibleSector> _byIndex = [];
        private readonly List<Igi2VisibleSector> _requests = [];
        private readonly List<int> _discarded = [];
        private int _traversals;

        /// <summary>Gets the building being drawn, whose shell and rooms are every candidate the walk offers.</summary>
        public required Igi2SectorObject Building { get; init; }

        /// <summary>Gets the sectors reached, in the order they were first requested.</summary>
        public IReadOnlyList<Igi2VisibleSector> Requests => _requests;

        /// <summary>Records a rectangle a sector was reached through, then walks out of it.</summary>
        /// <remarks><c>SectorObject_RequestSector</c>, <c>igi2.exe 0x588470</c>.</remarks>
        public void Request(int index, SectorScreenRect window)
        {
            if (_byIndex.TryGetValue(index, out Igi2VisibleSector? existing))
            {
                existing.Add(window);
            }
            else
            {
                Igi2VisibleSector visible = new(index, graph.Sectors[index], window);
                _byIndex.Add(index, visible);
                _requests.Add(visible);
            }

            // Unconditional, even for a sector already in the pool: the original recurses whether or not
            // it found a request. A second route into a room is a second, differently shaped window, and
            // what is visible through it is not what was visible through the first.
            Traverse(index, window);
        }

        private void Traverse(int index, SectorScreenRect window)
        {
            if (++_traversals > budget)
            {
                return;
            }

            _discarded.Add(index);
            Igi2Sector sector = graph.Sectors[index];

            foreach (MefPortal portal in sector.Portals.PortalSpan)
            {
                if (isPortalOpen is not null && !isPortalOpen(index, portal.Slot))
                {
                    continue;
                }

                if (!TryGetPortalBounds(camera, projection, sector, portal, window, out SectorScreenRect box))
                {
                    continue;
                }

                SectorScreenRect beyond = box.Intersect(window);
                Admit(portal.Slot, beyond);
            }

            _discarded.RemoveAt(_discarded.Count - 1);
        }

        private void Admit(int slot, SectorScreenRect beyond)
        {
            // The shell first, then the rooms in their container's order -- the order the original's candidate loop runs.
            foreach (Igi2Sector candidate in Building.Sectors)
            {
                if (_discarded.Contains(candidate.Index))
                {
                    continue;
                }

                foreach (MefPortal other in candidate.Portals.PortalSpan)
                {
                    if (other.Slot == slot)
                    {
                        // Once per matching portal, not once per sector: the original calls
                        // SectorObject_RequestSector inside the portal loop (0x5889D2) and does not break
                        // out of it, so a sector with two portals of one slot is requested twice.
                        Request(candidate.Index, beyond);
                    }
                }
            }
        }
    }
}

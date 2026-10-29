using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Sectors;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Project I.G.I.'s sector cull, decided once a frame from the eye: which buildings and rooms are drawn, and which objects
/// outside the building the camera stands in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the camera stands decides the frame.</b> <c>World_Draw</c> (<c>IGI.exe 0x4D2650</c>) asks every building near the eye
/// which of its sectors the eye is over, and draws the one that answers first (<c>SectorObject_EnterHandler</c>,
/// <c>0x4DD310</c>): walked from that sector through the whole screen, its shell drawn only if the walk reaches it. Reaching the
/// shell is seeing out, and <c>SectorObject_DrawSector</c> (<c>0x4DD5D0</c>) then records the rectangle the shell was reached
/// through as the outside's window.
/// </para>
/// <para>
/// <b>Then the queue is emptied with that answer</b> (<c>QCube_CallHandlers</c>, <c>0x4D12C0</c>). An object whose origin stands
/// within the building's optimal inside sphere (<see cref="SectorInsideSphere"/>) is drawn as ever. One beyond it is outside:
/// drawn only once the walk has seen out, and only through the outside's window, which narrows the display area its own
/// exclusion test measures against. That is the whole gate -- the ground is drawn outside it, unconditionally, and Project
/// I.G.I. has no per-room test for the objects inside.
/// </para>
/// <para>
/// With the camera over no building, every building is walked from its shell through the whole screen, so its rooms are drawn
/// only where its openings show them.
/// </para>
/// <para>
/// <b>How Project I.G.I.'s walk differs from the sequel's.</b> A sector already requested only widens its one rectangle and is
/// not walked again (<c>SectorObject_RequestSector</c>, <c>0x4DD4B0</c>), and a walk offers every sector of the building but the
/// one it is looking out of (<c>SectorObject_TraverseSector</c>, <c>0x4DD650</c>) -- there is no discard list, and no list of
/// windows. Every portal is open: the building's sixty-four portal bytes are set to one as it is made (<c>0x4DDF60</c>) and
/// nothing writes them again. <c>Sector_IsPortalVisible</c> is the sequel's (<see cref="SectorPortalProjection"/>).
/// </para>
/// <para>
/// <b>Clipped as well as culled.</b> The display area the queue narrows for an outside object is what the rigid mesh's triangles
/// are clipped against (<c>0x4A0340</c>, called from the mesh draw <c>0x49BE10</c>), so such an object is drawn only within the
/// outside's window (<see cref="ClipOf"/>). The ground is drawn before the queue, through the whole display area, and is not.
/// </para>
/// <para>
/// <b>The one type always outside never is.</b> The queue also treats as outside any object of the type in <c>word_C30064</c>.
/// Nothing in the binary writes it: it lies in the zero-filled tail of <c>.data</c>, and its only reference is that compare. So it
/// holds type id nought, <c>QTask</c> itself (the first type <c>0x4010D0</c> registers), which no drawn object is, and the
/// distance is the whole test.
/// </para>
/// <para>
/// <b>Not reproduced.</b> A building's level of detail is not consulted.
/// </para>
/// </remarks>
public sealed class Igi1SectorVisibility : IPlacementVisibility
{
    /// <summary>The task type whose objects are sector objects.</summary>
    public const string SectorObjectType = "Building";

    private readonly ObjectCollisionIndex _solids;
    private readonly List<Building> _buildings = [];
    private readonly List<Sector> _sectors = [];
    private readonly Dictionary<GameTask, Owner> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WorldPlacement, Sector> _parts = new(ReferenceEqualityComparer.Instance);

    // Per frame: each sector's request rectangle, null when it was not reached.
    private readonly SectorScreenRect?[] _requested;
    private Building? _inside;
    private Sector? _eyeSector;
    private Vector3d _sphereCentre;
    private float _sphereRadius;
    private SectorScreenRect? _outsideWindow;
    private SectorScreenProjection _projection;
    private Camera3D _eye = Camera3D.Default;

    private Igi1SectorVisibility(ObjectCollisionIndex solids, int sectorCount)
    {
        _solids = solids;
        _requested = new SectorScreenRect?[sectorCount];
    }

    /// <summary>Gets how many buildings the level places.</summary>
    public int BuildingCount => _buildings.Count;

    /// <summary>Gets how many rooms those buildings carry between them.</summary>
    public int RoomCount => _sectors.Count - _buildings.Count;

    /// <summary>Gets the building the eye stood in when the frame was decided, as its task, or <see langword="null"/> for none.</summary>
    public GameTask? EyeBuilding => _inside?.Task;

    /// <summary>Gets a value indicating whether the last frame's walk saw out of the building the eye stands in.</summary>
    public bool SeesOut => _inside is null || _outsideWindow is not null;

    /// <inheritdoc/>
    public string DescribeView() =>
        _inside is null
            ? "sector: none"
            : FormattableString.Invariant(
                $"sector: {_inside.Task.Name} {(_eyeSector is { IsShell: false } room ? "room " + room.Room : "shell")}, {(_outsideWindow is null ? "sees no way out" : "sees out")}, inside sphere r {_sphereRadius / WorldScene.UnitsPerMetre:0.0} m");

    /// <inheritdoc/>
    /// <remarks>Always: Project I.G.I. draws its ground whatever the camera stands in.</remarks>
    public bool IsOutsideVisible => true;

    /// <summary>Builds the cull for a level's scene.</summary>
    /// <param name="scene">The scene the level's walk produced.</param>
    /// <param name="models">Resolves a model name, as the walk did.</param>
    /// <param name="magic">The magic object types the walk expanded attach points through.</param>
    /// <param name="solids">The collision the scene was indexed into, which answers which sector the eye is over.</param>
    /// <returns>The cull, or <see langword="null"/> when the level places no building with a model.</returns>
    public static Igi1SectorVisibility? Build(
        WorldScene scene,
        ModelResolver models,
        MagicObjectRegistry magic,
        ObjectCollisionIndex solids)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(solids);

        // Every owner's root placement -- its own model, not an attach object -- with the building ones' trees beside them.
        Dictionary<GameTask, List<WorldPlacement>> byOwner = new(ReferenceEqualityComparer.Instance);
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!byOwner.TryGetValue(placement.Owner, out List<WorldPlacement>? list))
            {
                byOwner.Add(placement.Owner, list = []);
            }

            list.Add(placement);
        }

        List<(GameTask Task, WorldPlacement Root, WorldObject Tree, List<WorldPlacement> Parts)> buildings = [];
        foreach ((GameTask task, List<WorldPlacement> parts) in byOwner)
        {
            if (string.Equals(task.Type.Name, SectorObjectType, StringComparison.Ordinal)
                && parts.Find(static p => !p.IsAttached) is { } root
                && WorldObject.TryBuild(root.Instance.Name, models, magic, out WorldObject? tree)
                && tree is not null)
            {
                buildings.Add((task, root, tree, parts));
            }
        }

        if (buildings.Count == 0)
        {
            return null;
        }

        int sectors = buildings.Sum(static b => 1 + b.Tree.Children.Count(static c => c.Models.Highest.IsSector));
        Igi1SectorVisibility visibility = new(solids, sectors);
        foreach ((GameTask task, WorldPlacement root, WorldObject tree, List<WorldPlacement> parts) in buildings)
        {
            visibility.AddBuilding(task, root, tree, parts);
        }

        foreach ((GameTask task, List<WorldPlacement> parts) in byOwner)
        {
            WorldPlacement root = parts.Find(static p => !p.IsAttached) ?? parts[0];
            Vector3d at = WorldScene.ToUnits(root.Position);

            // A spline segment -- a length of rail or road -- is its model bent between two waypoints and laid about their middle
            // (0x4E4530): it is measured by the bent mesh's own bound, not the straight model's at the first waypoint, which took a
            // rail out of the outside's window whenever that one end was.
            if (root.BentGeometry is { } bent)
            {
                Vector3f middle = root.Orientation.Transposed().Transform(bent.Bounds.Centre * (float)WorldScene.UnitsPerMetre);
                Vector3d laid = new(at.X + middle.X, at.Y + middle.Y, at.Z + middle.Z);
                visibility._owners.TryAdd(task, new Owner(laid, laid, (float)(bent.Bounds.Radius * WorldScene.UnitsPerMetre)));
                continue;
            }

            Vector3f c = root.Orientation.Transposed().Transform(root.Model.RenderSphereCentre);
            visibility._owners.TryAdd(task, new Owner(at, new Vector3d(at.X + c.X, at.Y + c.Y, at.Z + c.Z), root.Model.RenderSphereRadius));
        }

        return visibility;
    }

    /// <inheritdoc/>
    public void BeginView(in Camera3D eye, float tangentHalfFovX, float tangentHalfFovY)
    {
        if (tangentHalfFovX > 0f && tangentHalfFovY > 0f)
        {
            Decide(new Camera3D(WorldScene.ToUnits(eye.Position), eye.Orientation), SectorScreenProjection.ForView(tangentHalfFovX, tangentHalfFovY));
        }
    }

    /// <summary>Decides the frame from an eye in world units.</summary>
    /// <param name="eye">The eye, in world units.</param>
    /// <param name="projection">How view space becomes the rectangles' pixels.</param>
    public void Decide(Camera3D eye, SectorScreenProjection projection)
    {
        _eye = eye;
        _projection = projection;
        Array.Clear(_requested);
        _inside = null;
        _outsideWindow = null;

        // SectorObject_GetSectorHandler, asked by every building near the eye; each one that answers overwrites the last.
        Sector? start = null;
        _eyeSector = null;
        foreach (Building building in _buildings)
        {
            if (building.SectorAt(_solids, eye.Position) is { } sector)
            {
                _inside = building;
                start = sector;
                _eyeSector = sector;
            }
        }

        SectorScreenRect display = projection.DisplayArea;
        if (_inside is not null && start is not null)
        {
            Walk(_inside, start, display);
            _outsideWindow = _requested[_inside.Shell.Index];

            // The inside sphere is placed by the building's rows and the position of the sector the eye is over -- not the
            // building's own position -- exactly as the handler copies it into the sector context.
            Vector3f turned = _inside.Shell.Orientation.Transposed().Transform(_inside.InsideCentre);
            _sphereCentre = new Vector3d(start.Position.X + turned.X, start.Position.Y + turned.Y, start.Position.Z + turned.Z);
            _sphereRadius = _inside.InsideRadius;
        }

        foreach (Building building in _buildings)
        {
            if (ReferenceEquals(building, _inside))
            {
                continue;
            }

            // A building out of view has nothing to reach; the cube tree never offers it. The bound is about the model's origin
            // rather than its centre, so twice it is what is sure to hold the whole building.
            if (!SectorPortalProjection.SphereInAnyWindow(eye, projection, building.Shell.Position, 2f * building.Radius, [display]))
            {
                continue;
            }

            // Inside a building, another one beyond the inside sphere is drawn only once the walk has seen out, and is
            // started through the outside's window -- the display area the queue narrowed before calling it.
            if (_inside is not null && IsOutside(building.Task))
            {
                if (_outsideWindow is { } window)
                {
                    Walk(building, building.Shell, window);
                }

                continue;
            }

            Walk(building, building.Shell, display);
        }
    }

    /// <inheritdoc/>
    public bool IsVisible(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (_parts.TryGetValue(placement, out Sector? sector))
        {
            if (_requested[sector.Index] is not null)
            {
                return true;
            }

            // A building's shell stands whenever the building is drawn from outside; only the ones beyond the camera's sphere
            // leave it out. The camera's own building keeps it too, though DrawSector (0x4DD5D0) draws only what the walk
            // reached: the shell is the far side of every room's walls, hidden by them but for the sub-pixel seams between
            // a room and what it carries, which a temporal resolve turns into lines of sky. Nothing else is seen through it.
            return sector.IsShell && (ReferenceEquals(sector.Building, _inside) || !IsGated(sector.Building));
        }

        if (_inside is null || !_owners.TryGetValue(placement.Owner, out Owner owner) || !IsOutside(owner))
        {
            return true;
        }

        return _outsideWindow is { } window
            && SectorPortalProjection.SphereInAnyWindow(_eye, _projection, owner.Centre, owner.Radius, [window]);
    }

    /// <inheritdoc/>
    public bool EnclosesEye(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return !_parts.TryGetValue(placement, out Sector? sector) || ReferenceEquals(sector.Building, _inside);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The outside's window, for every placement beyond the inside sphere while the camera stands in a building that sees out:
    /// <c>QCube_CallHandlers</c> narrows the display area to it before calling such an object, and the rigid mesh's triangles are
    /// clipped against the display area (<c>0x4A0340</c>).
    /// </remarks>
    public ViewClip? ClipOf(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (_inside is null || _outsideWindow is not { } window || !_owners.TryGetValue(placement.Owner, out Owner owner))
        {
            return null;
        }

        // The building the camera stands in is drawn before the queue is emptied, through the whole screen.
        if (_parts.TryGetValue(placement, out Sector? sector) && ReferenceEquals(sector.Building, _inside))
        {
            return null;
        }

        return IsOutside(owner) ? _projection.ToViewClip(window) : null;
    }

    /// <summary>Gets a value indicating whether a camera is the eye the frame was decided from, rather than a light's.</summary>
    /// <param name="camera">The camera, in metres, as the renderer draws from it.</param>
    /// <returns><see langword="true"/> for the frame's own eye.</returns>
    public bool IsEye(in Camera3D camera) =>
        WorldScene.ToUnits(camera.Position) == _eye.Position && camera.Orientation == _eye.Orientation;

    /// <summary>Gets the part of the view an object drawn outside the scene -- a soldier -- is clipped to this frame.</summary>
    /// <param name="origin">The object's own position, in world units.</param>
    /// <returns>The outside's window for an object beyond the inside sphere while the walk sees out, otherwise <see langword="null"/>.</returns>
    public ViewClip? ClipOfObject(Vector3d origin) =>
        _inside is not null && _outsideWindow is { } window && IsOutside(new Owner(origin, origin, 0f))
            ? _projection.ToViewClip(window)
            : null;

    /// <summary>
    /// Gets a value indicating whether an object drawn outside the scene -- a soldier -- is drawn this frame, by the same gate
    /// <c>QCube_CallHandlers</c> puts every queued object through.
    /// </summary>
    /// <param name="origin">The object's own position, which the gate measures from the inside sphere, in world units.</param>
    /// <param name="centre">The centre of the sphere its own exclusion test uses, in world units.</param>
    /// <param name="radius">That sphere's radius, in world units.</param>
    /// <returns><see langword="false"/> when the cull leaves it out.</returns>
    public bool IsObjectVisible(Vector3d origin, Vector3d centre, float radius)
    {
        if (_inside is null || !IsOutside(new Owner(origin, centre, radius)))
        {
            return true;
        }

        return _outsideWindow is { } window && SectorPortalProjection.SphereInAnyWindow(_eye, _projection, centre, radius, [window]);
    }

    private bool IsGated(Building building) =>
        _inside is not null && (ReferenceEquals(building, _inside) || IsOutside(building.Task));

    private bool IsOutside(GameTask task) => _owners.TryGetValue(task, out Owner owner) && IsOutside(owner);

    // QCube_CallHandlers: farther than the inside sphere's radius from its centre, by the object's own origin.
    private bool IsOutside(Owner owner)
    {
        double dx = owner.Origin.X - _sphereCentre.X, dy = owner.Origin.Y - _sphereCentre.Y, dz = owner.Origin.Z - _sphereCentre.Z;
        return (double)_sphereRadius * _sphereRadius < (dx * dx) + (dy * dy) + (dz * dz);
    }

    private void AddBuilding(GameTask task, WorldPlacement root, WorldObject tree, List<WorldPlacement> parts)
    {
        Building building = new(task, tree.Models.Highest.NativeMarkerBound);
        building.Shell = AddSector(building, root, Sector.ShellRoom);

        // The rooms are the tree's sector children, which it keeps after the ordinary ones as the original keeps them on their
        // own list; each is the attached placement standing where the walk put it.
        List<(Vector3f Centre, float Radius)> spheres = [];
        foreach (WorldObject child in tree.Children)
        {
            if (!child.Models.Highest.IsSector)
            {
                continue;
            }

            Vector3f offset = root.Orientation.Transposed().Transform(child.Position);
            Vector3d at = new(
                root.Position.X + (offset.X / WorldScene.UnitsPerMetre),
                root.Position.Y + (offset.Y / WorldScene.UnitsPerMetre),
                root.Position.Z + (offset.Z / WorldScene.UnitsPerMetre));
            if (parts.Find(p => p.IsAttached && p.Position == at
                && string.Equals(p.Instance.Name, child.Name, StringComparison.OrdinalIgnoreCase)) is not { } placed)
            {
                continue;
            }

            Sector room = AddSector(building, placed, building.Rooms.Count);
            building.Rooms.Add(room);
            Collect(child, spheres);
        }

        // Every part of the building is drawn by the sector it hangs under: a room's own subtree follows the room, which the
        // walk emitted straight after it, and everything else follows the shell.
        Sector owner = building.Shell;
        foreach (WorldPlacement part in parts)
        {
            if (building.Rooms.Find(r => ReferenceEquals(r.Placement, part)) is { } room)
            {
                owner = room;
            }
            else if (!part.IsAttached)
            {
                owner = building.Shell;
            }

            _parts.TryAdd(part, owner);
        }

        (Vector3f Centre, float Radius) inside = SectorInsideSphere.Merge(spheres)
            ?? (tree.Models.Highest.BoundingSphereCentre, tree.Models.Highest.BoundingSphereRadius);
        building.InsideCentre = inside.Centre;
        building.InsideRadius = inside.Radius;
        building.TraceTargets = [building.Shell.Placement, .. building.Rooms.Select(static r => r.Placement)];
        _buildings.Add(building);

        // SectorObject_AddSphere: a room's sphere, then every model it carries, depth first, each in its own space.
        static void Collect(WorldObject node, List<(Vector3f Centre, float Radius)> into)
        {
            into.Add((node.Models.Highest.BoundingSphereCentre, node.Models.Highest.BoundingSphereRadius));
            foreach (WorldObject child in node.Children)
            {
                Collect(child, into);
            }
        }
    }

    private Sector AddSector(Building building, WorldPlacement placement, int room)
    {
        Sector sector = new(_sectors.Count, building, room, placement, WorldScene.ToUnits(placement.Position), placement.Orientation, placement.Model.Portals);
        _sectors.Add(sector);
        return sector;
    }

    // SectorObject_RequestSector and SectorObject_TraverseSector (0x4DD4B0, 0x4DD650): a sector met again only widens its
    // rectangle; one met for the first time is walked out of, every portal tried against every other sector of the building.
    private void Walk(Building building, Sector start, SectorScreenRect window) => Request(building, start, window);

    private void Request(Building building, Sector sector, SectorScreenRect window)
    {
        if (_requested[sector.Index] is { } existing)
        {
            _requested[sector.Index] = existing.Union(window);
            return;
        }

        _requested[sector.Index] = window;
        foreach (MefPortal portal in sector.Portals.PortalSpan)
        {
            if (!SectorPortalProjection.TryGetPortalBounds(
                    _eye, _projection, sector.Position, sector.Orientation, sector.Portals, portal, window, out SectorScreenRect box))
            {
                continue;
            }

            SectorScreenRect beyond = box.Intersect(window);
            foreach (Sector candidate in building.Sectors)
            {
                if (ReferenceEquals(candidate, sector))
                {
                    continue;
                }

                foreach (MefPortal other in candidate.Portals.PortalSpan)
                {
                    if (other.Slot == portal.Slot)
                    {
                        Request(building, candidate, beyond);
                    }
                }
            }
        }
    }

    private readonly record struct Owner(Vector3d Origin, Vector3d Centre, float Radius);

    private sealed class Building(GameTask task, float radius)
    {
        public GameTask Task { get; } = task;

        // The radius the building is registered with, its model's bound; the eye's line reaches twice it down.
        public float Radius { get; } = radius;

        public Sector Shell { get; set; } = null!;

        public List<Sector> Rooms { get; } = [];

        public Vector3f InsideCentre { get; set; }

        public float InsideRadius { get; set; }

        public IReadOnlyList<WorldPlacement> TraceTargets { get; set; } = [];

        public IEnumerable<Sector> Sectors
        {
            get
            {
                yield return Shell;
                foreach (Sector room in Rooms)
                {
                    yield return room;
                }
            }
        }

        // SectorObject_GetSectorHandler (0x4DBCC0): one line straight down from the eye, twice the radius long, against the
        // shell and its rooms with their portals as surfaces; whichever is struck nearest is the sector the eye is over.
        public Sector? SectorAt(ObjectCollisionIndex solids, Vector3d eye)
        {
            double reach = 2.0 * Radius;
            Vector3d centre = Shell.Position;
            double dx = eye.X - centre.X, dy = eye.Y - centre.Y, dz = eye.Z - centre.Z;
            if ((dx * dx) + (dy * dy) + (dz * dz) > reach * reach)
            {
                return null;
            }

            return solids.TryCastPlacements(eye, new Vector3d(eye.X, eye.Y, eye.Z - reach), TraceTargets, out int struck, out _)
                ? (struck == 0 ? Shell : Rooms[struck - 1])
                : null;
        }
    }

    private sealed record Sector(
        int Index,
        Building Building,
        int Room,
        WorldPlacement Placement,
        Vector3d Position,
        Matrix3f Orientation,
        MefPortalSet Portals)
    {
        public const int ShellRoom = -1;

        public bool IsShell => Room == ShellRoom;
    }
}

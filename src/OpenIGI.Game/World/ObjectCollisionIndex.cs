using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// The placed objects a level is solid with, and the ray cast against them.
/// </summary>
/// <remarks>
/// <para>
/// The terrain octree says where the ground is; this says where the walls are. A model's collision
/// geometry is its <c>HSMC</c> chunk group (<see cref="MefCollisionMesh"/>) — a separate, far coarser
/// mesh than the one that draws, which is why a crate can be a box to walk into and a hundred triangles
/// to look at.
/// </para>
/// <para>
/// <b>Units.</b> A <see cref="WorldPlacement"/>'s position is in <b>metres</b> — the scene builder
/// divides by <see cref="WorldScene.UnitsPerMetre"/> for the renderer — while a collision mesh's
/// vertices and the player's own position are in <b>world units</b>. The conversion happens once, here,
/// at build time. Getting this wrong puts every object 4096 times too far away, which reads as no
/// collision at all rather than as an error.
/// </para>
/// <para>
/// <b>What this is not.</b> The original reaches its objects through the broad-phase octree that
/// <c>0x4C6940</c> commits positions into, and through <c>0x4CDB20</c>'s shape dispatch. Neither is
/// read. This walks a flat list, rejecting on each object's bounding sphere first. It finds the same
/// surfaces; it does more work to do it.
/// </para>
/// </remarks>
public sealed class ObjectCollisionIndex
{
    private readonly Entry[] _entries;
    private readonly Entry[] _playerEntries;
    private readonly Dictionary<GameTask, Entry[]> _entriesByOwner;
    private readonly Dictionary<WorldPlacement, Entry[]> _entriesByPlacement;

    /// <summary>The broad phase for the hot main-thread casts; see <see cref="StaticGrid"/>.</summary>
    private readonly StaticGrid _defaultGrid;
    private readonly StaticGrid _playerGrid;

    /// <summary>Entries that have ever moved, walked linearly because the grid holds their build pose.</summary>
    /// <remarks>Doors, cameras and cutscene vehicles — tens of entries against a level's hundreds.</remarks>
    private readonly List<Entry> _dynamicDefault = [];
    private readonly List<Entry> _dynamicPlayer = [];

    // Legacy/manual passability override. Mission doors no longer use it: their collision mesh follows
    // the live task transform through UpdateTransform, including every half-open pose.
    private readonly HashSet<GameTask> _passable = [];

    private ObjectCollisionIndex(Entry[] entries, Entry[] playerEntries)
    {
        _entries = entries;
        _playerEntries = playerEntries;
        _defaultGrid = new StaticGrid(entries);
        _playerGrid = new StaticGrid(playerEntries);
        _entriesByOwner = new Dictionary<GameTask, Entry[]>(ReferenceEqualityComparer.Instance);
        _entriesByPlacement = new Dictionary<WorldPlacement, Entry[]>(ReferenceEqualityComparer.Instance);

        Dictionary<GameTask, List<Entry>> byOwner = new(ReferenceEqualityComparer.Instance);
        Dictionary<WorldPlacement, List<Entry>> byPlacement = new(ReferenceEqualityComparer.Instance);
        foreach (Entry entry in entries)
        {
            AddEntry(entry, byOwner, byPlacement);
        }

        foreach (Entry entry in playerEntries)
        {
            AddEntry(entry, byOwner, byPlacement);
        }

        foreach ((GameTask owner, List<Entry> owned) in byOwner)
        {
            _entriesByOwner.Add(owner, [.. owned]);
        }

        foreach ((WorldPlacement placement, List<Entry> placed) in byPlacement)
        {
            _entriesByPlacement.Add(placement, [.. placed]);
        }
    }

    private static void AddEntry(
        Entry entry,
        Dictionary<GameTask, List<Entry>> byOwner,
        Dictionary<WorldPlacement, List<Entry>> byPlacement)
    {
        if (!byOwner.TryGetValue(entry.Owner, out List<Entry>? owned))
        {
            owned = [];
            byOwner.Add(entry.Owner, owned);
        }

        owned.Add(entry);
        if (!byPlacement.TryGetValue(entry.Placement, out List<Entry>? placed))
        {
            placed = [];
            byPlacement.Add(entry.Placement, placed);
        }

        placed.Add(entry);
    }

    /// <summary>Moves a moved entry from the static broad phase to the linear dynamic list.</summary>
    /// <remarks>
    /// The grid buckets an entry where it stood at build time, so the first move retires it from the
    /// grid for good: its stale buckets are skipped by the walk's <see cref="Entry.IsDynamic"/> check,
    /// and the entry is tested linearly with its live transform from here on.
    /// </remarks>
    private void MarkDynamic(Entry entry)
    {
        if (entry.InDynamicList)
        {
            return;
        }

        entry.InDynamicList = true;
        (entry.IsPlayerSet ? _dynamicPlayer : _dynamicDefault).Add(entry);
    }

    /// <summary>Switches a placing task's collision off or back on — an opening or closing door.</summary>
    /// <param name="owner">The exact placing task instance.</param>
    /// <param name="passable">Whether rays should now pass through it.</param>
    public void SetPassable(GameTask owner, bool passable)
    {
        if (passable)
        {
            _passable.Add(owner);
        }
        else
        {
            _passable.Remove(owner);
        }
    }

    /// <summary>Updates the live transform of a moving placed object.</summary>
    /// <param name="owner">The exact placing task instance.</param>
    /// <param name="positionMetres">Its current world position in metres.</param>
    /// <param name="orientation">Its current local-to-world orientation.</param>
    /// <returns><see langword="true"/> when at least one indexed mesh belonged to the task.</returns>
    /// <remarks>
    /// Doors in IGI keep colliding while they swing or slide; only their transform changes. This replaces
    /// the old all-or-nothing passable approximation for live callers while retaining
    /// <see cref="SetPassable"/> for compatibility and diagnostic bakes.
    /// </remarks>
    public bool UpdateTransform(GameTask owner, Vector3d positionMetres, Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_entriesByOwner.TryGetValue(owner, out Entry[]? entries))
        {
            return false;
        }

        foreach (Entry entry in entries)
        {
            entry.UpdateTransform(positionMetres, orientation);
            MarkDynamic(entry);
        }

        return true;
    }

    /// <summary>Translates every indexed child of a moving parent task.</summary>
    /// <param name="parent">The parent task whose children are moving.</param>
    /// <param name="displacementMetres">The incremental world translation in metres.</param>
    /// <returns>The number of collision entries translated.</returns>
    public int UpdateDescendantTranslation(GameTask parent, Vector3d displacementMetres) =>
        UpdateDescendantTranslation(parent, displacementMetres, skip: null);

    /// <summary>Translates indexed children of a moving parent, optionally skipping some owners.</summary>
    /// <remarks>
    /// Elevator-owned doors keep an absolute owner transform from their live swing plus the
    /// car's total offset. Incremental translation of those same doors would write the ride
    /// twice (IGI.exe parents the door mesh; it does not add the car step on top).
    /// </remarks>
    public int UpdateDescendantTranslation(
        GameTask parent,
        Vector3d displacementMetres,
        Func<GameTask, bool>? skip)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Vector3d units = displacementMetres * WorldScene.UnitsPerMetre;
        int updated = 0;

        foreach (Entry[] set in new[] { _entries, _playerEntries })
        {
            foreach (Entry entry in set)
            {
                if (skip?.Invoke(entry.Owner) == true)
                {
                    continue;
                }

                for (GameTask? current = entry.Owner.Parent; current is not null; current = current.Parent)
                {
                    if (!ReferenceEquals(current, parent))
                    {
                        continue;
                    }

                    entry.Translate(units);
                    MarkDynamic(entry);
                    updated++;
                    break;
                }
            }
        }

        return updated;
    }

    /// <summary>
    /// True when <paramref name="position"/> sits in a collision volume owned by
    /// <paramref name="ancestor"/> or one of its descendants.
    /// </summary>
    /// <remarks>
    /// Retail Human_SetParent (0x463310) takes the BoneDynCubeObj / ground-hit owner.
    /// </remarks>
    public bool IsPointInTaskVolume(Vector3d position, GameTask ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        foreach (Entry[] set in new[] { _entries, _playerEntries })
        {
            foreach (Entry entry in set)
            {
                if (!IsSelfOrDescendant(entry.Owner, ancestor)
                    || _passable.Contains(entry.Owner)
                    || !ConditionalContainerGate.IsTaskActive(entry.Owner)
                    || entry.Radius <= 0.0)
                {
                    continue;
                }

                double dx = position.X - entry.Centre.X;
                double dy = position.Y - entry.Centre.Y;
                double dz = position.Z - entry.Centre.Z;
                if (((dx * dx) + (dy * dy) + (dz * dz)) <= (entry.Radius * entry.Radius))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSelfOrDescendant(GameTask owner, GameTask ancestor)
    {
        for (GameTask? current = owner; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Updates one model placement owned by a multi-model task.</summary>
    /// <param name="owner">The task owning the placement.</param>
    /// <param name="instanceName">The exact pack-instance name to move.</param>
    /// <param name="positionMetres">Its current origin in metres.</param>
    /// <param name="orientation">Its current orientation.</param>
    /// <returns><see langword="true"/> when a matching collision mesh was indexed.</returns>
    /// <remarks>
    /// An <c>SCamera</c> owns a fixed holder and an articulated head. Updating by owner alone would
    /// incorrectly move the holder onto the head, so camera ticks use this placement-specific form.
    /// </remarks>
    public bool UpdateTransform(
        GameTask owner,
        string instanceName,
        Vector3d positionMetres,
        Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrEmpty(instanceName);
        if (!_entriesByOwner.TryGetValue(owner, out Entry[]? entries))
        {
            return false;
        }

        bool updated = false;
        foreach (Entry entry in entries)
        {
            if (!string.Equals(entry.InstanceName, instanceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.UpdateTransform(positionMetres, orientation);
            MarkDynamic(entry);
            updated = true;
        }

        return updated;
    }

    /// <summary>Updates one exact placement, including duplicate attached instance names.</summary>
    public bool UpdateTransform(WorldPlacement placement, Vector3d positionMetres, Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!_entriesByPlacement.TryGetValue(placement, out Entry[]? entries))
        {
            return false;
        }

        foreach (Entry entry in entries)
        {
            entry.UpdateTransform(positionMetres, orientation);
            MarkDynamic(entry);
        }

        return true;
    }

    /// <summary>Gets how many placed objects carry collision geometry.</summary>
    public int Count => _entries.Length;

    /// <summary>
    /// Finds the moving collision object whose broad-phase volume already contains a point.
    /// </summary>
    /// <param name="position">The point in world units.</param>
    /// <param name="includeInactiveOwners">Whether dormant conditional-container objects participate.</param>
    /// <param name="owner">Receives the most specific containing moving owner.</param>
    /// <returns><see langword="true"/> when a moving-object volume contains the point.</returns>
    /// <remarks>
    /// Retail staged humans acquire their vehicle from BoneDynCubeObj's existing contact before
    /// Human_GroundCollision considers its fallback GetNextZ query. The collision sphere is this
    /// index's broad-phase equivalent of that existing dynamic-cube contact.
    /// </remarks>
    public bool TryFindContainingDynamicOwner(
        Vector3d position,
        bool includeInactiveOwners,
        out GameTask? owner)
    {
        owner = null;
        double bestScore = double.MaxValue;

        foreach (Entry entry in _entries)
        {
            if (!entry.IsDynamic
                || _passable.Contains(entry.Owner)
                || (!includeInactiveOwners && !ConditionalContainerGate.IsTaskActive(entry.Owner))
                || entry.Radius <= 0.0)
            {
                continue;
            }

            double dx = position.X - entry.Centre.X;
            double dy = position.Y - entry.Centre.Y;
            double dz = position.Z - entry.Centre.Z;
            double distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
            double radiusSquared = entry.Radius * entry.Radius;
            if (distanceSquared > radiusSquared)
            {
                continue;
            }

            // Prefer the tightest containing volume. Equal volumes deliberately select the later
            // placement: retail World_RegisterObj inserts every dynamic object at the HEAD of its
            // spatial list, and the rigid-object trace preserves the first equal-distance front-face
            // hit. WorldScene emits placements in task creation order, so the later entry is the same
            // object retail visits first. Mission 1 relies on this: the opening train and landing train
            // start with identical freight cars at the same transform, and Human 1666 must acquire the
            // later-created landing car 1888 rather than the opening train's anonymous car.
            double score = distanceSquared / radiusSquared;
            if (score <= bestScore)
            {
                bestScore = score;
                owner = entry.Owner;
            }
        }

        return owner is not null;
    }

    /// <summary>Gets how many triangles the index holds in total.</summary>
    public int TriangleCount
    {
        get
        {
            int total = 0;
            foreach (Entry entry in _entries)
            {
                total += entry.Mesh.Faces.Count;
            }

            return total;
        }
    }

    /// <summary>
    /// Gets whether a placement is an invisible elevator-shaft collision volume (instance name
    /// <c>colbox*</c>, task class <c>EditRigidObj</c>). These authored boxes sit at every elevator
    /// doorway. Retail does not register them for the human trace — the player boards a lift with its
    /// door open and the door's own leaf provides the closed-state block — so keeping them solid to
    /// the player blocks boarding (observed as the "can't enter the cab" bug on M3/M8). They remain in
    /// the default world collision (level zero) so bullets and visibility still respect them.
    /// </summary>
    /// <param name="instanceName">The placement's instance name.</param>
    /// <returns><see langword="true"/> when the object is an elevator-shaft collision volume.</returns>
    private static bool IsElevatorShaftVolume(string instanceName) =>
        instanceName.StartsWith("colbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether any placement is owned by a task descended from an <c>Elevator</c> task.
    /// </summary>
    /// <remarks>
    /// The engine excludes elevator-shaft collision boxes from the human trace so the player can board
    /// a lift, but only on levels that actually contain elevator machinery. Levels without elevators
    /// (M9/M10) author <c>colbox*</c> objects as ordinary static collision the player must hit, so this
    /// level-wide check keeps those solid rather than letting the player walk through them.
    /// </remarks>
    private static bool HasElevatorTask(List<WorldPlacement> placements)
    {
        foreach (WorldPlacement placement in placements)
        {
            for (GameTask? current = placement.Owner; current is not null; current = current.Parent)
            {
                if (current is DeclaredTask declared
                    && string.Equals(declared.Type.Name, "Elevator", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Builds an index over a scene's placements.</summary>
    /// <param name="placements">The level's placed objects.</param>
    /// <returns>The index; empty when nothing placed carries collision geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="placements"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Placements whose model has no <c>HSMC</c> group are skipped rather than treated as solid. Most
    /// scenery is like that — a model is solid only if its author gave it a collision mesh.
    /// </remarks>
    public static ObjectCollisionIndex Build(IEnumerable<WorldPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);

        List<WorldPlacement> all = placements.ToList();
        bool hasElevator = HasElevatorTask(all);

        List<Entry> entries = [];
        List<Entry> playerEntries = [];
        Dictionary<MefModel, (MefCollisionMesh? Default, MefCollisionMesh? Player)> cache = [];

        foreach (WorldPlacement placement in all)
        {
            if (!cache.TryGetValue(placement.Model, out var meshes))
            {
                meshes = (
                    MefCollisionMesh.TryRead(placement.Model.Container, meshIndex: 0, out MefCollisionMesh? readDefaultMesh)
                        ? readDefaultMesh
                        : null,
                    MefCollisionMesh.TryRead(placement.Model.Container, meshIndex: 1, out MefCollisionMesh? readPlayerMesh)
                        ? readPlayerMesh
                        : null);
                cache[placement.Model] = meshes;
            }

            Vector3d origin = new(
                placement.Position.X * WorldScene.UnitsPerMetre,
                placement.Position.Y * WorldScene.UnitsPerMetre,
                placement.Position.Z * WorldScene.UnitsPerMetre);

            string describedModel = ModelNameRegistry.DescribeModel(placement.Instance.Name);
            string label = string.IsNullOrEmpty(placement.TaskName)
                ? describedModel
                : $"{describedModel} [{placement.TaskName}]";

            if (meshes.Default is { Faces.Count: > 0 } defaultMesh)
            {
                entries.Add(Entry.Create(
                    defaultMesh, origin, placement.Orientation, placement.Instance.Name, label, placement.TaskName,
                    placement.Owner, placement));
            }

            MefCollisionMesh? playerMesh = meshes.Player;
            if (playerMesh is { Faces.Count: > 0 } && !(hasElevator && IsElevatorShaftVolume(placement.Instance.Name)))
            {
                playerEntries.Add(Entry.Create(
                    playerMesh, origin, placement.Orientation, placement.Instance.Name, label, placement.TaskName,
                    placement.Owner, placement, isPlayerSet: true));
            }
        }

        return new ObjectCollisionIndex([.. entries], [.. playerEntries]);
    }

    /// <summary>Casts a ray at the placed objects.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    /// <remarks>
    /// Main thread only, like every live cast here: besides <see cref="_passable"/>, the broad-phase
    /// grid keeps per-query visit marks. The bake's <see cref="TryCastWithDoorsShut"/> stays off the
    /// grid for exactly that reason.
    /// </remarks>
    public bool TryCast(Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        CastIndexed(_entries, _defaultGrid, _dynamicDefault, origin, end, out hit);

    /// <summary>Casts the retail-human object trace.</summary>
    /// <remarks>
    /// IGI1's <c>0x461A70</c> calls <c>0x4CDB20</c> with <c>iMesh = 1</c> at <c>0x461D71</c>.
    /// Human movement therefore follows collision mesh level one, not the default level-zero geometry
    /// that visibility and ordinary world traces use.
    /// </remarks>
    public bool TryCastForPlayer(Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        CastIndexed(_playerEntries, _playerGrid, _dynamicPlayer, origin, end, out hit);

    /// <summary>
    /// Gets whether the human object trace still contains an entry for the named placed instance.
    /// </summary>
    /// <remarks>
    /// Lets tests assert the elevator-shaft <c>colbox*</c> exclusion is scoped to levels that actually
    /// have elevators: an elevator level must <em>not</em> contain the shaft volumes in its player
    /// entries, while a level without elevator machinery must still keep its <c>colbox*</c> solid.
    /// </remarks>
    public bool ContainsPlayerEntry(string instanceName) =>
        _playerEntries.Any(entry =>
            string.Equals(entry.InstanceName, instanceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Casts a rich request at the indexed placed objects.</summary>
    /// <remarks>
    /// Through the same broad phase as <see cref="TryCast"/>. This walked the flat list, which for the
    /// storm's ground grid -- a hundred and more vertical probes a tick -- and every shot fired was a
    /// sphere test against every placed object in the level per ray. Main thread only, like every live
    /// cast here: the grid keeps per-query visit marks.
    /// </remarks>
    public bool TryTrace(in WorldTraceRequest request, out WorldTraceHit hit)
    {
        TraceTest test = new(this, request);
        bool found = WalkIndexed(_entries, _defaultGrid, _dynamicDefault, request.Origin, request.End, ref test);
        hit = test.Hit;
        return found;
    }

    /// <summary>Casts a ray with every door treated as shut, whatever it is doing right now.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    /// <remarks>
    /// <para>
    /// For the probe bake, which wants both halves of that sentence. A door standing open when the bake
    /// runs would be traced as a hole in the wall it is part of, and the field is static, so the hole
    /// would still be there once the door shut.
    /// </para>
    /// <para>
    /// It is also the only cast safe to run off the main thread. <see cref="TryCast"/> reads
    /// <see cref="_passable"/>, which <see cref="SetPassable"/> mutates whenever a door moves; this
    /// touches nothing but the entry array, which never changes after
    /// <see cref="Build"/>. Ordering the bake before the doors open would answer the first point and
    /// not the second.
    /// </para>
    /// </remarks>
    public bool TryCastWithDoorsShut(Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        Cast(_entries, origin, end, honourPassable: false, out hit);

    /// <summary>The grid-accelerated cast behind <see cref="TryCast"/> and <see cref="TryCastForPlayer"/>.</summary>
    /// <remarks>
    /// The moved entries are tested linearly — there are tens of them — and the static rest through
    /// the cells the ray actually passes. The filters are exactly <see cref="Cast"/>'s with
    /// <c>honourPassable</c> true; only the visiting order differs, which the nearest-hit fold makes
    /// irrelevant.
    /// </remarks>
    private bool CastIndexed(
        Entry[] set,
        StaticGrid grid,
        List<Entry> dynamics,
        Vector3d origin,
        Vector3d end,
        out WorldRayHit hit)
    {
        RayTest test = new(this);
        bool found = WalkIndexed(set, grid, dynamics, origin, end, ref test);
        hit = test.Hit;
        return found;
    }

    /// <summary>The broad-phase walk itself, with the per-entry test supplied by the caller.</summary>
    /// <remarks>
    /// A struct visitor rather than a delegate so the two callers -- the legacy ray and the rich
    /// request -- share one walk without paying an indirect call per entry on the hottest path in the
    /// game. The visitor owns the hit it accumulates; the walk only needs to know the nearest distance
    /// so far, to stop at the first cell that starts beyond it.
    /// </remarks>
    private static bool WalkIndexed<TTest>(
        Entry[] set,
        StaticGrid grid,
        List<Entry> dynamics,
        Vector3d origin,
        Vector3d end,
        ref TTest test)
        where TTest : struct, IEntryTest
    {
        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;

        foreach (Entry entry in dynamics)
        {
            found |= test.Test(entry, origin, direction, ref nearest);
        }

        foreach (int index in grid.Oversized)
        {
            Entry entry = set[index];
            if (!entry.IsDynamic)
            {
                found |= test.Test(entry, origin, direction, ref nearest);
            }
        }

        grid.BeginQuery();

        // A 2D walk over the cells the segment passes (Amanatides & Woo). Two dimensions suffice:
        // levels extend kilometres across and metres up, and a vertical probe degenerates to a single
        // cell, which is precisely the ground-snap case this exists for.
        int cellX = StaticGrid.CellOf(origin.X);
        int cellY = StaticGrid.CellOf(origin.Y);
        int endX = StaticGrid.CellOf(end.X);
        int endY = StaticGrid.CellOf(end.Y);

        int stepX = direction.X > 0.0 ? 1 : direction.X < 0.0 ? -1 : 0;
        int stepY = direction.Y > 0.0 ? 1 : direction.Y < 0.0 ? -1 : 0;

        double cell = StaticGrid.CellUnits;
        double tMaxX = stepX == 0
            ? double.MaxValue
            : (((cellX + (stepX > 0 ? 1 : 0)) * cell) - origin.X) / direction.X;
        double tMaxY = stepY == 0
            ? double.MaxValue
            : (((cellY + (stepY > 0 ? 1 : 0)) * cell) - origin.Y) / direction.Y;
        double tDeltaX = stepX == 0 ? double.MaxValue : cell / Math.Abs(direction.X);
        double tDeltaY = stepY == 0 ? double.MaxValue : cell / Math.Abs(direction.Y);

        // The walk is finite by construction; the guard only insures the arithmetic against a
        // degenerate ray, and 1 << 14 cells is four times any level's span.
        for (int guard = 0; guard < (1 << 14); guard++)
        {
            if (grid.TryGetCell(cellX, cellY, out int[] indices))
            {
                foreach (int index in indices)
                {
                    if (grid.AlreadyVisited(index))
                    {
                        continue;
                    }

                    Entry entry = set[index];
                    if (!entry.IsDynamic)
                    {
                        found |= test.Test(entry, origin, direction, ref nearest);
                    }
                }
            }

            if (cellX == endX && cellY == endY)
            {
                break;
            }

            double next = Math.Min(tMaxX, tMaxY);

            // Every cell after this one starts past the segment's end, or past a hit already found —
            // and cells are visited in ray order, so nothing beyond can beat it. Without the second
            // test a sight line that meets the nearest wall still walked the ray's whole length, and
            // without the first a rounding slip at the end cell walked to the guard.
            if (next >= 1.0 || (found && nearest <= next))
            {
                break;
            }

            if (tMaxX < tMaxY)
            {
                cellX += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                cellY += stepY;
                tMaxY += tDeltaY;
            }
        }

        return found;
    }

    /// <summary>What <see cref="WalkIndexed{TTest}"/> asks of each entry the ray's cells hold.</summary>
    private interface IEntryTest
    {
        /// <returns><see langword="true"/> when the entry is the nearest contact so far.</returns>
        bool Test(Entry entry, Vector3d origin, Vector3d direction, ref double nearest);
    }

    /// <summary>The legacy ray's test: <see cref="TestEntry"/>, accumulating a <see cref="WorldRayHit"/>.</summary>
    private struct RayTest(ObjectCollisionIndex index) : IEntryTest
    {
        public WorldRayHit Hit;

        public bool Test(Entry entry, Vector3d origin, Vector3d direction, ref double nearest) =>
            index.TestEntry(entry, origin, direction, ref nearest, ref Hit);
    }

    /// <summary>The rich request's test: its ignore list and mask on top of the live filter.</summary>
    private struct TraceTest(ObjectCollisionIndex index, WorldTraceRequest request) : IEntryTest
    {
        public WorldTraceHit Hit;

        public bool Test(Entry entry, Vector3d origin, Vector3d direction, ref double nearest)
        {
            if ((request.IgnoredOwner is not null && ReferenceEquals(entry.Owner, request.IgnoredOwner))
                || (request.IgnoredSubject is not null && ReferenceEquals(entry.Owner, request.IgnoredSubject))
                || index.GenericPickups?.IsCollected(entry.Owner) == true
                || index._passable.Contains(entry.Owner)
                || (!request.IncludeInactiveOwners && !ConditionalContainerGate.IsTaskActive(entry.Owner)))
            {
                return false;
            }

            WorldTraceHitKind kind = entry.IsDynamic
                ? WorldTraceHitKind.DynamicObject
                : WorldTraceHitKind.StaticObject;
            WorldTraceMask category = entry.IsDynamic
                ? WorldTraceMask.DynamicObjects
                : WorldTraceMask.StaticObjects;
            if ((request.Mask & category) == 0
                || !MeetsSphere(origin, direction, entry.Centre, entry.Radius)
                || !entry.TryCast(origin, direction, ref nearest, useClosedTransform: false, out WorldRayHit candidate))
            {
                return false;
            }

            Hit = WorldTraceHit.FromRayHit(request, candidate, kind, entry.Owner, entry.Label);
            return true;
        }
    }

    /// <summary>One entry's live-cast test: the filter, the sphere, then the triangles.</summary>
    private bool TestEntry(
        Entry entry,
        Vector3d origin,
        Vector3d direction,
        ref double nearest,
        ref WorldRayHit hit)
    {
        if (GenericPickups?.IsCollected(entry.Owner) == true
            || _passable.Contains(entry.Owner)
            || !ConditionalContainerGate.IsTaskActive(entry.Owner))
        {
            return false;
        }

        if (!MeetsSphere(origin, direction, entry.Centre, entry.Radius))
        {
            return false;
        }

        if (!entry.TryCast(origin, direction, ref nearest, useClosedTransform: false, out WorldRayHit candidate))
        {
            return false;
        }

        hit = candidate;
        return true;
    }

    private bool Cast(Entry[] entries, Vector3d origin, Vector3d end, bool honourPassable, out WorldRayHit hit)
    {
        hit = default;

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;

        foreach (Entry entry in entries)
        {
            if (GenericPickups?.IsCollected(entry.Owner) == true
                || (honourPassable
                    && (_passable.Contains(entry.Owner) || !ConditionalContainerGate.IsTaskActive(entry.Owner))))
            {
                continue;
            }

            // The root sphere encloses every vertex - checked against all 254 of level 1's meshes - so
            // a ray that misses it cannot touch a triangle.
            Vector3d broadCentre = honourPassable ? entry.Centre : entry.ClosedCentre;
            if (!MeetsSphere(origin, direction, broadCentre, entry.Radius))
            {
                continue;
            }

            if (entry.TryCast(
                origin,
                direction,
                ref nearest,
                useClosedTransform: !honourPassable,
                out WorldRayHit candidate))
            {
                hit = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>The bounding sphere of one indexed object, in world units.</summary>
    /// <param name="index">Its position in the index.</param>
    /// <returns>The sphere's centre and radius.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the index.</exception>
    public (Vector3d Centre, double Radius) Bounds(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _entries.Length);

        Entry entry = _entries[index];
        return (entry.Centre, entry.Radius);
    }

    /// <summary>The index of the object with the largest bounding sphere.</summary>
    /// <returns>Its position in the index, or −1 when nothing is indexed.</returns>
    /// <remarks>A building rather than a doorknob — the least ambiguous thing to aim a test at.</remarks>
    public int LargestIndex()
    {
        int best = -1;
        double radius = -1.0;

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Radius > radius)
            {
                radius = _entries[i].Radius;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Gets a cast delegate bound to this index.</summary>
    /// <returns>The delegate the sweep takes.</returns>
    public WorldRayCast Caster() => TryCast;

    /// <summary>Gets the collision-level-one cast retail player movement uses.</summary>
    public WorldRayCast PlayerCaster() => TryCastForPlayer;

    /// <summary>Gets the player's collision cast over the registration-time world, including dormant containers.</summary>
    /// <remarks>
    /// A mission constructs its player before an opening cutscene hands control to the gameplay
    /// conditional container. The authored spawn still has to settle onto that container's floor, so
    /// initial placement uses this immutable, doors-shut view; ordinary movement uses
    /// <see cref="PlayerCaster"/> and therefore observes live container and door state.
    /// </remarks>
    public WorldRayCast PlayerRegistrationCaster() => TryCastForPlayerAtRegistration;

    private bool TryCastForPlayerAtRegistration(Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        Cast(_playerEntries, origin, end, honourPassable: false, out hit);

    /// <summary>Gets or sets the live generic pickups removed from the collision world after collection.</summary>
    public GenericPickupRegistry? GenericPickups { get; set; }
    /// <summary>Gets the rich trace delegate bound to this index.</summary>
    public WorldTraceCast Tracer() => TryTrace;

    /// <summary>Gets a cast delegate that treats every door as shut and reads no mutable state.</summary>
    /// <returns>The delegate the probe bake takes.</returns>
    /// <remarks>See <see cref="TryCastWithDoorsShut"/> for why the bake wants both properties.</remarks>
    public WorldRayCast CasterWithDoorsShut() => TryCastWithDoorsShut;

    /// <summary>Casts a ray and names the object it meets — a diagnostic for "what am I walking into".</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="label">Receives the struck object's instance name (and placing task).</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    /// <remarks>
    /// Unlike <see cref="TryCast"/> this reports <i>which</i> placement was hit, so a blocked doorway can
    /// be traced back to the exact model and task that walled it off. It walks the same flat list.
    /// </remarks>
    public bool TryIdentify(Vector3d origin, Vector3d end, out string label, out WorldRayHit hit)
    {
        hit = default;
        label = string.Empty;

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;

        foreach (Entry entry in _entries)
        {
            if (!ConditionalContainerGate.IsTaskActive(entry.Owner))
            {
                continue;
            }

            if (!MeetsSphere(origin, direction, entry.Centre, entry.Radius))
            {
                continue;
            }

            if (entry.TryCast(
                origin,
                direction,
                ref nearest,
                useClosedTransform: false,
                out WorldRayHit candidate))
            {
                hit = candidate;
                label = entry.Label;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Casts a ray along the human collision trace and names the object it meets.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="label">Receives the struck object's instance name (and placing task).</param>
    /// <param name="owner">Receives the struck object's placing task.</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    /// <remarks>
    /// The player trace (collision mesh level one) can contain volumes invisible to the default world
    /// trace — elevator-shaft boxes, door leaves — so this reports who blocks a human body the way
    /// <see cref="TryIdentify"/> reports who blocks a world ray.
    /// </remarks>
    public bool TryIdentifyForPlayer(Vector3d origin, Vector3d end, out string label, out GameTask? owner, out WorldRayHit hit)
    {
        hit = default;
        label = string.Empty;
        owner = null;

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;

        foreach (Entry entry in _playerEntries)
        {
            if (_passable.Contains(entry.Owner) || !ConditionalContainerGate.IsTaskActive(entry.Owner))
            {
                continue;
            }

            if (!MeetsSphere(origin, direction, entry.Centre, entry.Radius))
            {
                continue;
            }

            if (entry.TryCast(
                origin,
                direction,
                ref nearest,
                useClosedTransform: false,
                out WorldRayHit candidate))
            {
                hit = candidate;
                label = entry.Label;
                owner = entry.Owner;
                found = true;
            }
        }

        return found;
    }

    private static bool MeetsSphere(Vector3d origin, Vector3d direction, Vector3d centre, double radius)
    {
        double ox = origin.X - centre.X;
        double oy = origin.Y - centre.Y;
        double oz = origin.Z - centre.Z;

        double a = (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z);
        double b = 2.0 * ((ox * direction.X) + (oy * direction.Y) + (oz * direction.Z));
        double c = (ox * ox) + (oy * oy) + (oz * oz) - (radius * radius);

        if (c <= 0.0)
        {
            return true;
        }

        double discriminant = (b * b) - (4.0 * a * c);
        if (discriminant < 0.0)
        {
            return false;
        }

        // Only the segment counts, not the whole line.
        double root = Math.Sqrt(discriminant);
        double t0 = (-b - root) / (2.0 * a);
        double t1 = (-b + root) / (2.0 * a);

        return (t0 >= 0.0 && t0 <= 1.0) || (t1 >= 0.0 && t1 <= 1.0) || (t0 < 0.0 && t1 > 1.0);
    }

    /// <summary>The broad phase: a sparse 2D grid over the entries' build-time bounding spheres.</summary>
    /// <remarks>
    /// <para>
    /// The flat scan this replaces touched every placed object's sphere for every ray — the AI's
    /// lines of sight, every soldier's ground snap, every dropped gun's contact probe, all against
    /// the whole level. A ray now touches the entries of the cells it passes, which for the vertical
    /// ground probe is one cell.
    /// </para>
    /// <para>
    /// Two-dimensional on purpose: a level is kilometres across and metres tall, so a Z axis would
    /// cut nothing while tripling the buckets. An entry whose sphere spans an unreasonable number of
    /// cells goes to the <see cref="Oversized"/> list and is tested every query instead of being
    /// written into hundreds of buckets.
    /// </para>
    /// </remarks>
    private sealed class StaticGrid
    {
        /// <summary>The cell edge, chosen against typical building footprints.</summary>
        public static readonly double CellUnits = 32.0 * WorldScene.UnitsPerMetre;

        /// <summary>A sphere spanning more cells than this per axis is not worth bucketing.</summary>
        private const int OversizedCellSpan = 32;

        private readonly Dictionary<long, int[]> _cells = [];
        private readonly int[] _stamps;
        private int _stamp;

        public StaticGrid(Entry[] entries)
        {
            _stamps = new int[entries.Length];
            Dictionary<long, List<int>> cells = [];
            List<int> oversized = [];

            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                int minX = CellOf(entry.Centre.X - entry.Radius);
                int maxX = CellOf(entry.Centre.X + entry.Radius);
                int minY = CellOf(entry.Centre.Y - entry.Radius);
                int maxY = CellOf(entry.Centre.Y + entry.Radius);

                if (maxX - minX >= OversizedCellSpan || maxY - minY >= OversizedCellSpan)
                {
                    oversized.Add(i);
                    continue;
                }

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        long key = Key(x, y);
                        if (!cells.TryGetValue(key, out List<int>? bucket))
                        {
                            bucket = [];
                            cells.Add(key, bucket);
                        }

                        bucket.Add(i);
                    }
                }
            }

            foreach ((long key, List<int> bucket) in cells)
            {
                _cells.Add(key, [.. bucket]);
            }

            Oversized = [.. oversized];
        }

        /// <summary>Gets the entries too large to bucket, tested on every query.</summary>
        public int[] Oversized { get; }

        public static int CellOf(double coordinate) => (int)Math.Floor(coordinate / CellUnits);

        /// <summary>Starts a query, invalidating the previous one's visit marks in O(1).</summary>
        public void BeginQuery() => _stamp++;

        /// <summary>Marks an entry visited this query; true when it already was.</summary>
        /// <remarks>
        /// An entry sits in every cell its sphere touches, so a ray crossing several of them would
        /// test its triangles once per cell without this.
        /// </remarks>
        public bool AlreadyVisited(int index)
        {
            if (_stamps[index] == _stamp)
            {
                return true;
            }

            _stamps[index] = _stamp;
            return false;
        }

        public bool TryGetCell(int x, int y, out int[] indices) =>
            _cells.TryGetValue(Key(x, y), out indices!);

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    }

    private sealed class Entry
    {
        private readonly Vector3f[] _localVertices;
        private readonly Vector3d[] _closedVertices;
        private readonly Vector3f _rootCentre;
        private readonly Vector3d[] _vertices;

        private Entry(
            MefCollisionMesh mesh,
            Vector3f[] localVertices,
            Vector3d[] vertices,
            Vector3d centre,
            double radius,
            Vector3f rootCentre,
            string instanceName,
            string label,
            string taskName,
            GameTask owner,
            WorldPlacement placement)
        {
            Mesh = mesh;
            _localVertices = localVertices;
            _vertices = vertices;
            _closedVertices = (Vector3d[])vertices.Clone();
            _rootCentre = rootCentre;
            Centre = centre;
            ClosedCentre = centre;
            Radius = radius;
            InstanceName = instanceName;
            Label = label;
            TaskName = taskName;
            Owner = owner;
            Placement = placement;
        }

        public MefCollisionMesh Mesh { get; }

        public Vector3d Centre { get; private set; }

        public Vector3d ClosedCentre { get; private set; }

        public double Radius { get; }

        public string InstanceName { get; }

        public string Label { get; }

        public string TaskName { get; }

        public GameTask Owner { get; }

        public WorldPlacement Placement { get; }

        public bool IsDynamic { get; private set; }

        /// <summary>Gets whether this entry belongs to the human (level-one) trace set.</summary>
        public bool IsPlayerSet { get; init; }

        /// <summary>Gets or sets whether the entry has been moved to its set's dynamic list.</summary>
        public bool InDynamicList { get; set; }

        public static Entry Create(
            MefCollisionMesh mesh,
            Vector3d origin,
            Matrix3f orientation,
            string instanceName,
            string label,
            string taskName,
            GameTask owner,
            WorldPlacement placement,
            bool isPlayerSet = false)
        {
            // Local-to-world is the COLUMN-vector product M·v — the renderer's `uModelView * v`, and
            // the convention the original itself uses for a placement's Real32x9 (it reads elements
            // [0],[3],[6], the first column, as the local X axis in world space). Matrix3f.Transform
            // is the row-vector product v·M, the inverse rotation: using it here spun every rotated
            // object's collision the wrong way round its origin, so fences could be walked through
            // and doorways sat behind solid wall. The transpose makes Transform compute M·v.
            Matrix3f rotation = orientation.Transposed();

            // The vertices are placed once at build time. A level's collision geometry does not move,
            // and transforming a ray into every model's own frame instead would cost more than it saves.
            Vector3f[] localVertices = new Vector3f[mesh.Vertices.Count];
            Vector3d[] vertices = new Vector3d[mesh.Vertices.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                localVertices[i] = mesh.Vertices[i];
                Vector3f local = rotation.Transform(localVertices[i]);
                vertices[i] = new Vector3d(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);
            }

            Vector3d centre = origin;
            double radius = 0.0;
            Vector3f rootCentre = default;

            if (mesh.Spheres.Count > 0)
            {
                MefCollisionSphere root = mesh.Spheres[0];
                rootCentre = root.Centre;
                Vector3f placed = rotation.Transform(root.Centre);
                centre = new Vector3d(origin.X + placed.X, origin.Y + placed.Y, origin.Z + placed.Z);
                radius = root.Radius;
            }
            else
            {
                foreach (Vector3d vertex in vertices)
                {
                    double dx = vertex.X - centre.X;
                    double dy = vertex.Y - centre.Y;
                    double dz = vertex.Z - centre.Z;
                    radius = Math.Max(radius, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
                }
            }

            return new Entry(
                mesh, localVertices, vertices, centre, radius, rootCentre, instanceName, label, taskName, owner,
                placement)
            {
                IsPlayerSet = isPlayerSet,
            };
        }

        public void UpdateTransform(Vector3d positionMetres, Matrix3f orientation)
        {
            Vector3d origin = new(
                positionMetres.X * WorldScene.UnitsPerMetre,
                positionMetres.Y * WorldScene.UnitsPerMetre,
                positionMetres.Z * WorldScene.UnitsPerMetre);
            Matrix3f rotation = orientation.Transposed();

            // This entry is single-thread-owned during live simulation. Reuse its transformed storage:
            // doors, trains and cutscene objects can move every tick, and allocating one array per pose
            // creates a GC feedback loop precisely while their animations are playing.
            Vector3d[] vertices = _vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3f placed = rotation.Transform(_localVertices[i]);
                vertices[i] = new Vector3d(origin.X + placed.X, origin.Y + placed.Y, origin.Z + placed.Z);
            }

            Vector3f centre = rotation.Transform(_rootCentre);
            Centre = new Vector3d(origin.X + centre.X, origin.Y + centre.Y, origin.Z + centre.Z);
            IsDynamic = true;
        }

        public void Translate(Vector3d displacementUnits)
        {
            for (int i = 0; i < _vertices.Length; i++)
            {
                _vertices[i] += displacementUnits;
                _closedVertices[i] += displacementUnits;
            }

            Centre += displacementUnits;
            ClosedCentre += displacementUnits;
            IsDynamic = true;
        }

        public bool TryCast(
            Vector3d origin,
            Vector3d direction,
            ref double nearest,
            bool useClosedTransform,
            out WorldRayHit hit)
        {
            hit = default;
            bool found = false;
            Vector3d[] vertices = useClosedTransform ? _closedVertices : _vertices;

            foreach (MefCollisionFace face in Mesh.Faces)
            {
                // Portal polygons are visibility boundaries, not solid geometry. IGI1's human trace
                // selects mesh level one but still leaves isTracePortals clear.
                if (Mesh.IsPortal(face.Material))
                {
                    continue;
                }

                Vector3d a = vertices[face.I0];
                Vector3d b = vertices[face.I1];
                Vector3d c = vertices[face.I2];

                if (!RayTriangle.Intersect(origin, direction, a, b, c, out double t) || t >= nearest)
                {
                    continue;
                }

                Vector3d normal = RayTriangle.Normal(a, b, c);
                if (normal is { X: 0.0, Y: 0.0, Z: 0.0 })
                {
                    continue;
                }

                // Face the ray. The winding of a collision mesh is not established, and a normal
                // pointing away would push the body the wrong side of the wall.
                double facing = (normal.X * direction.X) + (normal.Y * direction.Y) + (normal.Z * direction.Z);
                if (facing > 0.0)
                {
                    normal = new Vector3d(-normal.X, -normal.Y, -normal.Z);
                }

                nearest = t;
                found = true;

                double length = Math.Sqrt(
                    (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

                hit = new WorldRayHit(
                    new Vector3d(
                        origin.X + (direction.X * t),
                        origin.Y + (direction.Y * t),
                        origin.Z + (direction.Z * t)),
                    new Vector3f((float)normal.X, (float)normal.Y, (float)normal.Z),
                    t * length,
                    // Report the global game-material row, not the per-mesh face index, so callers can
                    // name the surface (glass, fence, concrete) against the shared material table.
                    Mesh.GlobalMaterial(face.Material));
            }

            return found;
        }
    }
}

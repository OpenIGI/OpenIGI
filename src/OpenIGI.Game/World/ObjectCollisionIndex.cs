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

    // Living human bodies, re-supplied by the host every frame. They are cast separately from the
    // static mesh entries because a soldier has no collision mesh of its own in the port — the body is
    // the analytic capsule the renderer uses to keep humans apart.
    private HumanBody[] _humanBodies = [];

    // Legacy/manual passability override. Mission doors no longer use it: their collision mesh follows
    // the live task transform through UpdateTransform, including every half-open pose.
    private readonly HashSet<GameTask> _passable = [];

    // Closed-pose bounds of doors that are no longer fully shut. Sight traces that include portal
    // faces skip a portal whose centroid sits in one of these boxes, so an open doorway is a hole
    // again while a closed room stays opaque.
    private readonly Dictionary<GameTask, (Vector3d Min, Vector3d Max)> _sightOpenings = new(
        ReferenceEqualityComparer.Instance);

    private ObjectCollisionIndex(Entry[] entries, Entry[] playerEntries)
    {
        _entries = entries;
        _playerEntries = playerEntries;
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

    /// <summary>
    /// Opens or closes the authored doorway a door occupies for AI sight.
    /// </summary>
    /// <param name="owner">The exact <c>Door</c> task.</param>
    /// <param name="open">Whether the door is no longer fully closed.</param>
    /// <param name="displacementMetres">World translation of a carrier such as an elevator.</param>
    /// <remarks>
    /// Walking always skips portal faces. Sight does not, so a closed room is cover. Once the door
    /// leaves its shut pose the portal has to become a hole again or a guard standing in an open
    /// doorway cannot see through it.
    /// </remarks>
    public void SetSightOpening(GameTask owner, bool open, Vector3d displacementMetres = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!open)
        {
            _sightOpenings.Remove(owner);
            return;
        }

        if (!_entriesByOwner.TryGetValue(owner, out Entry[]? entries) || entries.Length == 0)
        {
            return;
        }

        Vector3d min = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Vector3d max = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        Vector3d displacementUnits = displacementMetres * WorldScene.UnitsPerMetre;
        foreach (Entry entry in entries)
        {
            (Vector3d lo, Vector3d hi) = entry.ClosedBounds;
            lo += displacementUnits;
            hi += displacementUnits;
            min = new(Math.Min(min.X, lo.X), Math.Min(min.Y, lo.Y), Math.Min(min.Z, lo.Z));
            max = new(Math.Max(max.X, hi.X), Math.Max(max.Y, hi.Y), Math.Max(max.Z, hi.Z));
        }

        // A personnel door is thinner than the building portal it fills. Grow the hole so the
        // portal triangles around the slab are skipped once the leaf is open.
        const double pad = 0.75 * WorldScene.UnitsPerMetre;
        _sightOpenings[owner] = (
            new Vector3d(min.X - pad, min.Y - pad, min.Z - pad),
            new Vector3d(max.X + pad, max.Y + pad, max.Z + pad));
    }

    private bool PortalIsSightOpening(Vector3d point)
    {
        foreach ((Vector3d min, Vector3d max) in _sightOpenings.Values)
        {
            if (point.X >= min.X && point.X <= max.X
                && point.Y >= min.Y && point.Y <= max.Y
                && point.Z >= min.Z && point.Z <= max.Z)
            {
                return true;
            }
        }

        return false;
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

            // Character HSMC meshes stay at the authored spawn. World traces (AI sight, bullets)
            // would hit that leftover hull before the live detection mark and treat the player as
            // cover. Live humans are not static world; skip them on the world index.
            if (meshes.Default is { Faces.Count: > 0 } defaultMesh
                && !IsCharacterPlacement(placement))
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
                    placement.Owner, placement));
            }
        }

        return new ObjectCollisionIndex([.. entries], [.. playerEntries]);
    }

    private static bool IsCharacterPlacement(WorldPlacement placement)
    {
        string? type = placement.Owner.Type.Name;
        return string.Equals(type, "HumanPlayer", StringComparison.Ordinal)
            || string.Equals(type, "HumanSoldier", StringComparison.Ordinal)
            || string.Equals(type, "HumanSoldierFemale", StringComparison.Ordinal);
    }

    /// <summary>Casts a ray at the placed objects.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    public bool TryCast(Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        Cast(_entries, origin, end, honourPassable: true, out hit);

    /// <summary>Casts the retail-human object trace.</summary>
    /// <remarks>
    /// IGI1's <c>0x461A70</c> calls <c>0x4CDB20</c> with <c>iMesh = 1</c> at <c>0x461D71</c>.
    /// Human movement therefore follows collision mesh level one, not the default level-zero geometry
    /// that visibility and ordinary world traces use.
    /// </remarks>
    /// <summary>Replaces the set of living human collision bodies.</summary>
    /// <param name="bodies">The capsule bodies, in world units, for every solid human.</param>
    /// <remarks>
    /// A mission re-supplies this on every tick after the soldiers move. Each body's
    /// <see cref="RayCapsule"/> test is folded into the player cast below, so the existing human wall
    /// sweep treats a living soldier as a wall and the player cannot walk through one.
    /// </remarks>
    public void SetHumanBodies(IReadOnlyList<HumanBody> bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        if (bodies.Count == 0)
        {
            _humanBodies = [];
            return;
        }

        HumanBody[] copy = new HumanBody[bodies.Count];
        for (int i = 0; i < bodies.Count; i++)
        {
            copy[i] = bodies[i];
        }

        _humanBodies = copy;
    }

    /// <summary>Casts the retail-human object trace, plus every living human body.</summary>
    /// <remarks>
    /// IGI1's <c>0x461A70</c> calls <c>0x4CDB20</c> with <c>iMesh = 1</c> at <c>0x461D71</c>.
    /// Human movement therefore follows collision mesh level one, not the default level-zero geometry
    /// that visibility and ordinary world traces use. Living human bodies join that trace so a soldier
    /// presents the same solid wall retail got from its registered mesh.
    /// </remarks>
    public bool TryCastForPlayer(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        bool found = Cast(_playerEntries, origin, end, honourPassable: true, out hit);
        if (CastHumanBodies(origin, end, out WorldRayHit body) && (!found || body.Distance < hit.Distance))
        {
            hit = body;
            found = true;
        }

        return found;
    }

    private bool CastHumanBodies(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        hit = default;
        if (_humanBodies.Length == 0)
        {
            return false;
        }

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        Vector3d position = default;
        Vector3f normal = default;
        foreach (HumanBody body in _humanBodies)
        {
            if (!RayCapsule.Intersect(
                    origin,
                    direction,
                    body.AxisA,
                    body.AxisB,
                    body.Radius,
                    out double t,
                    out Vector3d capsuleNormal)
                || t >= nearest)
            {
                continue;
            }

            nearest = t;
            position = new Vector3d(
                origin.X + (direction.X * t),
                origin.Y + (direction.Y * t),
                origin.Z + (direction.Z * t));
            normal = new Vector3f((float)capsuleNormal.X, (float)capsuleNormal.Y, (float)capsuleNormal.Z);
        }

        if (nearest < double.MaxValue)
        {
            hit = new WorldRayHit(position, normal, nearest * length, 0);
            return true;
        }

        return false;
    }

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
    public bool TryTrace(in WorldTraceRequest request, out WorldTraceHit hit)
    {
        hit = default;

        Vector3d direction = new(
            request.End.X - request.Origin.X,
            request.End.Y - request.Origin.Y,
            request.End.Z - request.Origin.Z);
        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;
        foreach (Entry entry in _entries)
        {
            if ((request.IgnoredOwner is not null && ReferenceEquals(entry.Owner, request.IgnoredOwner))
                || (request.IgnoredSubject is not null && ReferenceEquals(entry.Owner, request.IgnoredSubject))
                || GenericPickups?.IsCollected(entry.Owner) == true
                || _passable.Contains(entry.Owner)
                || (!request.IncludeInactiveOwners && !ConditionalContainerGate.IsTaskActive(entry.Owner)))
            {
                continue;
            }

            WorldTraceHitKind kind = entry.IsDynamic
                ? WorldTraceHitKind.DynamicObject
                : WorldTraceHitKind.StaticObject;
            WorldTraceMask category = entry.IsDynamic
                ? WorldTraceMask.DynamicObjects
                : WorldTraceMask.StaticObjects;
            if ((request.Mask & category) == 0
                || !MeetsSphere(request.Origin, direction, entry.Centre, entry.Radius)
                || !entry.TryCast(
                    request.Origin,
                    direction,
                    ref nearest,
                    useClosedTransform: false,
                    includePortals: request.TracePortals,
                    portalIsOpening: request.TracePortals ? PortalIsSightOpening : null,
                    out WorldRayHit candidate))
            {
                continue;
            }

            hit = WorldTraceHit.FromRayHit(request, candidate, kind, entry.Owner, entry.Label);
            found = true;
        }

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
                includePortals: false,
                portalIsOpening: null,
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
                includePortals: false,
                portalIsOpening: null,
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
                includePortals: false,
                portalIsOpening: null,
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
            ClosedBounds = BoundsOf(vertices);
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

        public (Vector3d Min, Vector3d Max) ClosedBounds { get; }

        public static Entry Create(
            MefCollisionMesh mesh,
            Vector3d origin,
            Matrix3f orientation,
            string instanceName,
            string label,
            string taskName,
            GameTask owner,
            WorldPlacement placement)
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
                placement);
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

        private static (Vector3d Min, Vector3d Max) BoundsOf(Vector3d[] vertices)
        {
            Vector3d min = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            Vector3d max = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
            foreach (Vector3d vertex in vertices)
            {
                min = new(Math.Min(min.X, vertex.X), Math.Min(min.Y, vertex.Y), Math.Min(min.Z, vertex.Z));
                max = new(Math.Max(max.X, vertex.X), Math.Max(max.Y, vertex.Y), Math.Max(max.Z, vertex.Z));
            }

            return (min, max);
        }

        public bool TryCast(
            Vector3d origin,
            Vector3d direction,
            ref double nearest,
            bool useClosedTransform,
            bool includePortals,
            Func<Vector3d, bool>? portalIsOpening,
            out WorldRayHit hit)
        {
            hit = default;
            bool found = false;
            Vector3d[] vertices = useClosedTransform ? _closedVertices : _vertices;

            foreach (MefCollisionFace face in Mesh.Faces)
            {
                // Portal polygons are visibility boundaries, not solid geometry. IGI1's human
                // movement trace leaves them clear so a body walks through a doorway. AI sight
                // includes them (0x489B00 → 0x4C4630) so a closed room is cover; an open door
                // punches a hole through the portal it occupied.
                if (Mesh.IsPortal(face.Material))
                {
                    if (!includePortals)
                    {
                        continue;
                    }

                    Vector3d a0 = vertices[face.I0];
                    Vector3d b0 = vertices[face.I1];
                    Vector3d c0 = vertices[face.I2];
                    Vector3d mid = new(
                        (a0.X + b0.X + c0.X) / 3.0,
                        (a0.Y + b0.Y + c0.Y) / 3.0,
                        (a0.Z + b0.Z + c0.Z) / 3.0);
                    if (portalIsOpening?.Invoke(mid) == true)
                    {
                        continue;
                    }
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

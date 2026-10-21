using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats;
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
public sealed partial class ObjectCollisionIndex
{
    private readonly ObjectCollisionEntry[] _entries;
    private readonly ObjectCollisionEntry[] _playerEntries;
    private readonly Dictionary<GameTask, ObjectCollisionEntry[]> _entriesByOwner;
    private readonly Dictionary<WorldPlacement, ObjectCollisionEntry[]> _entriesByPlacement;

    /// <summary>The broad phase for the hot main-thread casts; see <see cref="CollisionStaticGrid"/>.</summary>
    private readonly CollisionStaticGrid _defaultGrid;
    private readonly CollisionStaticGrid _playerGrid;

    /// <summary>Entries that have ever moved, walked linearly because the grid holds their build pose.</summary>
    /// <remarks>Doors, cameras and cutscene vehicles — tens of entries against a level's hundreds.</remarks>
    private readonly List<ObjectCollisionEntry> _dynamicDefault = [];
    private readonly List<ObjectCollisionEntry> _dynamicPlayer = [];

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

    /// <summary>An index with no placed objects, so a suite exercises only what it adds itself.</summary>
    /// <returns>The empty index.</returns>
    internal static ObjectCollisionIndex EmptyForTesting() => new([], []);

    private ObjectCollisionIndex(ObjectCollisionEntry[] entries, ObjectCollisionEntry[] playerEntries)
    {
        _entries = entries;
        _playerEntries = playerEntries;
        _defaultGrid = new CollisionStaticGrid(entries);
        _playerGrid = new CollisionStaticGrid(playerEntries);
        _entriesByOwner = new Dictionary<GameTask, ObjectCollisionEntry[]>(ReferenceEqualityComparer.Instance);
        _entriesByPlacement = new Dictionary<WorldPlacement, ObjectCollisionEntry[]>(ReferenceEqualityComparer.Instance);

        Dictionary<GameTask, List<ObjectCollisionEntry>> byOwner = new(ReferenceEqualityComparer.Instance);
        Dictionary<WorldPlacement, List<ObjectCollisionEntry>> byPlacement = new(ReferenceEqualityComparer.Instance);
        foreach (ObjectCollisionEntry entry in entries)
        {
            AddEntry(entry, byOwner, byPlacement);
        }

        foreach (ObjectCollisionEntry entry in playerEntries)
        {
            AddEntry(entry, byOwner, byPlacement);
        }

        foreach ((GameTask owner, List<ObjectCollisionEntry> owned) in byOwner)
        {
            _entriesByOwner.Add(owner, [.. owned]);
        }

        foreach ((WorldPlacement placement, List<ObjectCollisionEntry> placed) in byPlacement)
        {
            _entriesByPlacement.Add(placement, [.. placed]);
        }
    }

    private static void AddEntry(
        ObjectCollisionEntry entry,
        Dictionary<GameTask, List<ObjectCollisionEntry>> byOwner,
        Dictionary<WorldPlacement, List<ObjectCollisionEntry>> byPlacement)
    {
        if (!byOwner.TryGetValue(entry.Owner, out List<ObjectCollisionEntry>? owned))
        {
            owned = [];
            byOwner.Add(entry.Owner, owned);
        }

        owned.Add(entry);
        if (!byPlacement.TryGetValue(entry.Placement, out List<ObjectCollisionEntry>? placed))
        {
            placed = [];
            byPlacement.Add(entry.Placement, placed);
        }

        placed.Add(entry);
    }

    /// <summary>Moves a moved entry from the static broad phase to the linear dynamic list.</summary>
    /// <remarks>
    /// The grid buckets an entry where it stood at build time, so the first move retires it from the
    /// grid for good: its stale buckets are skipped by the walk's <see cref="ObjectCollisionEntry.IsDynamic"/> check,
    /// and the entry is tested linearly with its live transform from here on.
    /// </remarks>
    private void MarkDynamic(ObjectCollisionEntry entry)
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

        if (!_entriesByOwner.TryGetValue(owner, out ObjectCollisionEntry[]? entries) || entries.Length == 0)
        {
            return;
        }

        Vector3d min = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Vector3d max = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        Vector3d displacementUnits = displacementMetres * WorldScene.UnitsPerMetre;
        foreach (ObjectCollisionEntry entry in entries)
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

    internal bool PortalIsSightOpening(Vector3d point)
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
        if (!_entriesByOwner.TryGetValue(owner, out ObjectCollisionEntry[]? entries))
        {
            return false;
        }

        foreach (ObjectCollisionEntry entry in entries)
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

        foreach (ObjectCollisionEntry[] set in new[] { _entries, _playerEntries })
        {
            foreach (ObjectCollisionEntry entry in set)
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
        foreach (ObjectCollisionEntry[] set in new[] { _entries, _playerEntries })
        {
            foreach (ObjectCollisionEntry entry in set)
            {
                if (!entry.Owner.IsSelfOrDescendantOf(ancestor)
                    || IsRetired(entry)
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
        if (!_entriesByOwner.TryGetValue(owner, out ObjectCollisionEntry[]? entries))
        {
            return false;
        }

        bool updated = false;
        foreach (ObjectCollisionEntry entry in entries)
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
        if (!_entriesByPlacement.TryGetValue(placement, out ObjectCollisionEntry[]? entries))
        {
            return false;
        }

        foreach (ObjectCollisionEntry entry in entries)
        {
            entry.UpdateTransform(positionMetres, orientation);
            MarkDynamic(entry);
        }

        return true;
    }

    /// <summary>Gets the pose one placement's collision mesh currently sits at.</summary>
    /// <param name="placement">The exact placement.</param>
    /// <param name="positionMetres">Receives its origin, in metres.</param>
    /// <param name="orientation">Receives its local-to-world orientation.</param>
    /// <returns><see langword="false"/> when the placement has no collision here.</returns>
    /// <remarks>
    /// The pose a round actually struck: whatever <see cref="UpdateTransform(GameTask, Vector3d, Matrix3f)"/>
    /// and the carrier translations last wrote. A mark takes its local frame from this, so it sits on
    /// the face it hit however far the object has moved since the level was built.
    /// </remarks>
    public bool TryGetPose(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!_entriesByPlacement.TryGetValue(placement, out ObjectCollisionEntry[]? entries) || entries.Length == 0)
        {
            positionMetres = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        ObjectCollisionEntry entry = entries[0];
        positionMetres = entry.PoseOrigin * (1.0 / WorldScene.UnitsPerMetre);
        orientation = entry.PoseOrientation;
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

        foreach (ObjectCollisionEntry entry in _entries)
        {
            if (!entry.IsDynamic
                || IsRetired(entry)
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
            foreach (ObjectCollisionEntry entry in _entries)
            {
                total += entry.Mesh.Faces.Length;
            }

            return total;
        }
    }

    /// <summary>Builds an index over a scene's placements.</summary>
    /// <param name="placements">The level's placed objects.</param>
    /// <returns>The index; empty when nothing placed carries collision geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="placements"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Placements whose model has no <c>HSMC</c> group are skipped rather than treated as solid. Most
    /// scenery is like that — a model is solid only if its author gave it a collision mesh.
    /// </remarks>
    /// <param name="collision">
    /// How this title lays a collision mesh out, or null for the first game's. A wrong layout does not
    /// always fail: the row strides differ AND the material row moved a field, so a reader using the
    /// wrong one can return a plausible material for every face.
    /// </param>
    /// <param name="models">
    /// Resolves a model by name, for a spline segment's LOD extents; without it only the placed model's own vertices
    /// are measured.
    /// </param>
    public static ObjectCollisionIndex Build(
        IEnumerable<WorldPlacement> placements,
        CollisionLayout? collision = null,
        Func<string, MefModel?>? models = null)
    {
        ArgumentNullException.ThrowIfNull(placements);

        (ObjectCollisionEntry[] entries, ObjectCollisionEntry[] playerEntries) =
            ObjectCollisionIndexBuilder.Build(placements, collision, models);
        return new ObjectCollisionIndex(entries, playerEntries);
    }

    // A spline segment placement's two collision levels, bent between its waypoints, each with the origin its bent
    // vertices are measured from. False for anything else, and for a rigid segment (path B, not transcribed).
    internal static bool TryBendSegment(
        WorldPlacement placement,
        Func<string, MefModel?>? models,
        CollisionLayout? collision,
        out MefCollisionMesh? level0,
        out Vector3d origin0,
        out MefCollisionMesh? level1,
        out Vector3d origin1) =>
        ObjectCollisionIndexBuilder.TryBendSegment(placement, models, collision, out level0, out origin0, out level1, out origin1);

    /// <summary>Whether an owner's collision is switched off by <see cref="SetPassable"/>.</summary>
    internal bool IsPassable(GameTask owner) => _passable.Contains(owner);

    /// <summary>Gets or sets the live generic pickups removed from the collision world after collection.</summary>
    public GenericPickupRegistry? GenericPickups { get; set; }

    /// <summary>Gets or sets the live destructible placement states removed after retail destruction.</summary>
    public DestructibleObjectRegistry? Destructibles { get; set; }

    /// <summary>Whether an entry is out of the world: an intact model once destroyed, a wreck until then.</summary>
    /// <remarks>
    /// A car's last countdown tick (<c>0x42E5A0</c>) removes the car from the world and creates its wreck there
    /// in its place (<c>0x4F3390</c>), and the wreck's initializer (<c>0x4F2B90</c>) commits it to the world like
    /// any placed object (<c>0x4C6940</c>) -- so the wreck blocks where the car did. An ExplodeObject's model swap
    /// (<c>0x481A60</c>) likewise changes what its own object collides with.
    /// </remarks>
    internal bool IsRetired(ObjectCollisionEntry entry) =>
        (Destructibles?.IsDestroyed(entry.Placement) == true) != entry.IsDestroyedModel;
}

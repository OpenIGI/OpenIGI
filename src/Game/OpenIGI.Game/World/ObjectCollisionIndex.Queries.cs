using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <content>The casts and queries: every ray, trace and box the live world asks of the index.</content>
public sealed partial class ObjectCollisionIndex
{
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

    /// <summary>Casts a ray at a few chosen placements only, their portal faces counted as surfaces.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="placements">The placements to test, and nothing else.</param>
    /// <param name="struck">Receives the index into <paramref name="placements"/> of the one met nearest, or -1.</param>
    /// <param name="hit">Receives that contact.</param>
    /// <returns><see langword="false"/> when the ray meets none of them.</returns>
    /// <remarks>
    /// The trace a sector object makes to say which of its sectors a point stands in (<c>SectorObject_GetSectorHandler</c>,
    /// <c>IGI.exe 0x4DBCC0</c>): its own shell and its rooms, with <c>isTracePortals</c> set so an opening is struck like a
    /// wall. Nothing else in the world can come between, so no other entry is asked.
    /// </remarks>
    public bool TryCastPlacements(
        Vector3d origin,
        Vector3d end,
        IReadOnlyList<WorldPlacement> placements,
        out int struck,
        out WorldRayHit hit)
    {
        ArgumentNullException.ThrowIfNull(placements);
        struck = -1;
        hit = default;

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        if (direction.LengthSquared <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        for (int i = 0; i < placements.Count; i++)
        {
            if (!_entriesByPlacement.TryGetValue(placements[i], out ObjectCollisionEntry[]? entries))
            {
                continue;
            }

            foreach (ObjectCollisionEntry entry in entries)
            {
                if (MeetsSphere(origin, direction, entry.Centre, entry.Radius)
                    && entry.TryCast(
                        origin,
                        direction,
                        ref nearest,
                        useClosedTransform: false,
                        includePortals: true,
                        portalIsOpening: null,
                        out WorldRayHit candidate))
                {
                    hit = candidate;
                    struck = i;
                }
            }
        }

        return struck >= 0;
    }

    /// <summary>Collects every live solid triangle of the default collision level that touches a box.</summary>
    /// <param name="min">The box's low corner, in world units.</param>
    /// <param name="max">The box's high corner.</param>
    /// <param name="into">Receives the triangles; not cleared first.</param>
    /// <remarks>
    /// The same geometry and the same live filters as <see cref="TryCast"/> -- collected pickups,
    /// destroyed objects, passable owners and dormant containers are absent, doors are where they are
    /// now, and portal faces are not solid -- so a rigid body rests on exactly what a round would hit.
    /// </remarks>
    public void CollectTriangles(Vector3d min, Vector3d max, List<CollisionTriangle> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (ObjectCollisionEntry entry in _dynamicDefault)
        {
            CollectEntry(entry, min, max, into);
        }

        foreach (int index in _defaultGrid.Oversized)
        {
            ObjectCollisionEntry entry = _entries[index];
            if (!entry.IsDynamic)
            {
                CollectEntry(entry, min, max, into);
            }
        }

        _defaultGrid.BeginQuery();
        int x0 = CollisionStaticGrid.CellOf(min.X), x1 = CollisionStaticGrid.CellOf(max.X);
        int y0 = CollisionStaticGrid.CellOf(min.Y), y1 = CollisionStaticGrid.CellOf(max.Y);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (!_defaultGrid.TryGetCell(x, y, out int[] indices))
                {
                    continue;
                }

                foreach (int index in indices)
                {
                    ObjectCollisionEntry entry = _entries[index];
                    if (!_defaultGrid.AlreadyVisited(index) && !entry.IsDynamic)
                    {
                        CollectEntry(entry, min, max, into);
                    }
                }
            }
        }
    }

    private void CollectEntry(ObjectCollisionEntry entry, Vector3d min, Vector3d max, List<CollisionTriangle> into)
    {
        if (GenericPickups?.IsCollected(entry.Owner) == true
            || IsRetired(entry)
            || _passable.Contains(entry.Owner)
            || !ConditionalContainerGate.IsTaskActive(entry.Owner))
        {
            return;
        }

        // Sphere against box: the nearest point of the box to the centre, within the radius.
        double dx = Math.Max(min.X - entry.Centre.X, Math.Max(0.0, entry.Centre.X - max.X));
        double dy = Math.Max(min.Y - entry.Centre.Y, Math.Max(0.0, entry.Centre.Y - max.Y));
        double dz = Math.Max(min.Z - entry.Centre.Z, Math.Max(0.0, entry.Centre.Z - max.Z));
        if ((dx * dx) + (dy * dy) + (dz * dz) > entry.Radius * entry.Radius)
        {
            return;
        }

        entry.CollectTriangles(min, max, into);
    }

    /// <summary>Casts against live world collision while excluding one task's attached placements.</summary>
    /// <remarks>
    /// A moving vehicle starts its obstacle probe at its front edge. Excluding the vehicle owner keeps
    /// the probe from immediately striking its own collision mesh while retaining live doors and every
    /// other placed obstacle.
    /// </remarks>
    public bool TryCastIgnoring(
        GameTask ignoredOwner,
        Vector3d origin,
        Vector3d end,
        out WorldRayHit hit) =>
        TryCastIgnoring(ignoredOwner, origin, end, out hit, out _, out _);

    /// <summary>Casts while excluding one owner and identifies the blocking task.</summary>
    public bool TryCastIgnoring(
        GameTask ignoredOwner,
        Vector3d origin,
        Vector3d end,
        out WorldRayHit hit,
        out GameTask? hitOwner) =>
        TryCastIgnoring(ignoredOwner, origin, end, out hit, out hitOwner, out _);

    /// <summary>Casts while excluding one owner and identifies the blocking task and placement label.</summary>
    /// <remarks>
    /// Through <see cref="TryTrace"/>, so the exclusion rides the same broad phase and the same
    /// passable, collected, destroyed and dormant-container filters as every other live cast.
    /// </remarks>
    public bool TryCastIgnoring(
        GameTask ignoredOwner,
        Vector3d origin,
        Vector3d end,
        out WorldRayHit hit,
        out GameTask? hitOwner,
        out string hitLabel)
    {
        if (TryTrace(new WorldTraceRequest(origin, end, IgnoredOwner: ignoredOwner), out WorldTraceHit trace))
        {
            hit = trace.RayHit;
            hitOwner = trace.Owner;
            hitLabel = trace.Label;
            return true;
        }

        hit = default;
        hitOwner = null;
        hitLabel = string.Empty;
        return false;
    }

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
        bool found = CastIndexed(_playerEntries, _playerGrid, _dynamicPlayer, origin, end, out hit, retailArithmetic: true);
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
    /// <remarks>
    /// Through the same broad phase as <see cref="TryCast"/>. This walked the flat list, which for the
    /// storm's ground grid -- a hundred and more vertical probes a tick -- and every shot fired was a
    /// sphere test against every placed object in the level per ray. Main thread only, like every live
    /// cast here: the grid keeps per-query visit marks.
    /// </remarks>
    public bool TryTrace(in WorldTraceRequest request, out WorldTraceHit hit)
    {
        ObjectCollisionTraceTest test = new(this, request);
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
        ObjectCollisionEntry[] set,
        CollisionStaticGrid grid,
        List<ObjectCollisionEntry> dynamics,
        Vector3d origin,
        Vector3d end,
        out WorldRayHit hit,
        bool retailArithmetic = false)
    {
        ObjectCollisionRayTest test = new(this) { RetailArithmetic = retailArithmetic, End = end };
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
    internal static bool WalkIndexed<TTest>(
        ObjectCollisionEntry[] set,
        CollisionStaticGrid grid,
        List<ObjectCollisionEntry> dynamics,
        Vector3d origin,
        Vector3d end,
        ref TTest test)
        where TTest : struct, IObjectCollisionEntryTest
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

        foreach (ObjectCollisionEntry entry in dynamics)
        {
            found |= test.Test(entry, origin, direction, ref nearest);
        }

        foreach (int index in grid.Oversized)
        {
            ObjectCollisionEntry entry = set[index];
            if (!entry.IsDynamic)
            {
                found |= test.Test(entry, origin, direction, ref nearest);
            }
        }

        grid.BeginQuery();

        // A 2D walk over the cells the segment passes (Amanatides & Woo). Two dimensions suffice:
        // levels extend kilometres across and metres up, and a vertical probe degenerates to a single
        // cell, which is precisely the ground-snap case this exists for.
        int cellX = CollisionStaticGrid.CellOf(origin.X);
        int cellY = CollisionStaticGrid.CellOf(origin.Y);
        int endX = CollisionStaticGrid.CellOf(end.X);
        int endY = CollisionStaticGrid.CellOf(end.Y);

        int stepX = direction.X > 0.0 ? 1 : direction.X < 0.0 ? -1 : 0;
        int stepY = direction.Y > 0.0 ? 1 : direction.Y < 0.0 ? -1 : 0;

        double cell = CollisionStaticGrid.CellUnits;
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

                    ObjectCollisionEntry entry = set[index];
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


    /// <summary>One entry's live-cast test: the filter, the sphere, then the triangles.</summary>
    internal bool TestEntry(
        ObjectCollisionEntry entry,
        Vector3d origin,
        Vector3d direction,
        ref double nearest,
        ref WorldRayHit hit,
        Vector3d? retailEnd = null)
    {
        if (GenericPickups?.IsCollected(entry.Owner) == true
            || IsRetired(entry)
            || _passable.Contains(entry.Owner)
            || !ConditionalContainerGate.IsTaskActive(entry.Owner))
        {
            return false;
        }

        if (!MeetsSphere(origin, direction, entry.Centre, entry.Radius))
        {
            return false;
        }

        WorldRayHit candidate;
        if (retailEnd is { } end && entry.SupportsRetailRay)
        {
            if (!entry.TryCastRetail(origin, end, ref nearest, out candidate))
            {
                return false;
            }
        }
        else if (!entry.TryCast(
            origin,
            direction,
            ref nearest,
            useClosedTransform: false,
            includePortals: false,
            portalIsOpening: null,
            out candidate))
        {
            return false;
        }

        hit = candidate;
        return true;
    }

    private bool Cast(ObjectCollisionEntry[] entries, Vector3d origin, Vector3d end, bool honourPassable, out WorldRayHit hit)
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

        foreach (ObjectCollisionEntry entry in entries)
        {
            if (GenericPickups?.IsCollected(entry.Owner) == true
                || IsRetired(entry)
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

        ObjectCollisionEntry entry = _entries[index];
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
    public bool TryIdentify(Vector3d origin, Vector3d end, out string label, out WorldRayHit hit) =>
        TryIdentify(origin, end, out label, out _, out hit);

    /// <summary>Casts a ray, names the object it meets, and hands back the placement that was struck.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="label">Receives the struck object's instance name (and placing task).</param>
    /// <param name="placement">Receives the struck placement, for a caller that needs more than a name.</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    /// <remarks>
    /// The contact is on the COLLISION mesh, so it carries the game-material row and nothing about what
    /// is drawn. Handing the placement back is what lets a caller go on to ask the render mesh — see
    /// <see cref="RenderSurfaceProbe"/>, which needs the model, its pack and its transform, all of which
    /// live on the placement and none of which a label can carry.
    /// </remarks>
    public bool TryIdentify(
        Vector3d origin,
        Vector3d end,
        out string label,
        out WorldPlacement? placement,
        out WorldRayHit hit)
    {
        hit = default;
        label = string.Empty;
        placement = null;

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;

        foreach (ObjectCollisionEntry entry in _entries)
        {
            if (IsRetired(entry)
                || !ConditionalContainerGate.IsTaskActive(entry.Owner))
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
                placement = entry.Placement;
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
    /// <see cref="TryIdentify(Vector3d, Vector3d, out string, out WorldRayHit)"/> reports who blocks a world ray.
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

        foreach (ObjectCollisionEntry entry in _playerEntries)
        {
            if (IsRetired(entry)
                || _passable.Contains(entry.Owner)
                || !ConditionalContainerGate.IsTaskActive(entry.Owner))
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

    internal static bool MeetsSphere(Vector3d origin, Vector3d direction, Vector3d centre, double radius)
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
}

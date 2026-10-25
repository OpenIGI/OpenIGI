using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Builds an <see cref="ObjectCollisionIndex"/>'s two entry sets from a level's placements: the default level-zero
/// meshes and the human trace's level-one meshes, with bent spline segments and destroyed models beside them.
/// </summary>
internal static class ObjectCollisionIndexBuilder
{
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

    /// <summary>Reads every placement's collision meshes into the two entry sets, in placement order.</summary>
    /// <remarks>See <see cref="ObjectCollisionIndex.Build"/>.</remarks>
    internal static (ObjectCollisionEntry[] Entries, ObjectCollisionEntry[] PlayerEntries) Build(
        IEnumerable<WorldPlacement> placements,
        CollisionLayout? collision,
        Func<string, MefModel?>? models)
    {
        List<WorldPlacement> all = placements.ToList();
        bool hasElevator = HasElevatorTask(all);

        List<ObjectCollisionEntry> entries = [];
        List<ObjectCollisionEntry> playerEntries = [];
        Dictionary<MefModel, (MefCollisionMesh? Default, MefCollisionMesh? Player)> cache = [];

        foreach (WorldPlacement placement in all)
        {
            if (!cache.TryGetValue(placement.Model, out var meshes))
            {
                meshes = (
                    MefCollisionMesh.TryRead(placement.Model.Container, meshIndex: 0, out MefCollisionMesh? readDefaultMesh, collision)
                        ? readDefaultMesh
                        : null,
                    MefCollisionMesh.TryRead(placement.Model.Container, meshIndex: 1, out MefCollisionMesh? readPlayerMesh, collision)
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

            // A spline segment -- a piece of road, rail or bridge -- is not its model where it stands: the original
            // bends the model's collision along the curve between two waypoints and lays it about their midpoint
            // (0x4E4530, 0x49A050). The placement only carries the first waypoint and the drawn mesh, so the
            // collision is built here, bent on each level, exactly as the car's rays meet it. Laying the unbent
            // model at the first waypoint put the road beside itself: the player walked on the snow under it,
            // and its footsteps said so.
            if (TryBendSegment(placement, models, collision, out MefCollisionMesh? bentDefault, out Vector3d bentDefaultOrigin,
                    out MefCollisionMesh? bentPlayer, out Vector3d bentPlayerOrigin))
            {
                if (bentDefault is { Faces.Length: > 0 })
                {
                    entries.Add(ObjectCollisionEntry.Create(
                        bentDefault, bentDefaultOrigin, Matrix3f.Identity, placement.Instance.Name, label, placement.TaskName,
                        placement.Owner, placement));
                }

                if (bentPlayer is { Faces.Length: > 0 })
                {
                    playerEntries.Add(ObjectCollisionEntry.Create(
                        bentPlayer, bentPlayerOrigin, Matrix3f.Identity, placement.Instance.Name, label, placement.TaskName,
                        placement.Owner, placement, isPlayerSet: true));
                }

                continue;
            }

            // Character HSMC meshes stay at the authored spawn. World traces (AI sight, bullets)
            // would hit that leftover hull before the live detection mark and treat the player as
            // cover. Live humans are not static world; skip them on the world index.
            if (meshes.Default is { Faces.Length: > 0 } defaultMesh
                && !IsCharacterPlacement(placement))
            {
                entries.Add(ObjectCollisionEntry.Create(
                    defaultMesh, origin, placement.Orientation, placement.Instance.Name, label, placement.TaskName,
                    placement.Owner, placement));
            }

            MefCollisionMesh? playerMesh = meshes.Player;
            if (playerMesh is { Faces.Length: > 0 } && !(hasElevator && IsElevatorShaftVolume(placement.Instance.Name)))
            {
                playerEntries.Add(ObjectCollisionEntry.Create(
                    playerMesh, origin, placement.Orientation, placement.Instance.Name, label, placement.TaskName,
                    placement.Owner, placement, isPlayerSet: true));
            }

            // The destroyed model's own collision, on the same placement and under the same instance name, so
            // whatever moves the intact model (a car's pose, a camera head's) moves its wreck with it; it is
            // solid only once the placement is destroyed (IsRetired).
            if (placement.DestroyedReplacement is { } wreck && !IsCharacterPlacement(placement))
            {
                if (!cache.TryGetValue(wreck.Model, out var wreckMeshes))
                {
                    wreckMeshes = (
                        MefCollisionMesh.TryRead(wreck.Model.Container, meshIndex: 0, out MefCollisionMesh? readWreckDefault, collision)
                            ? readWreckDefault
                            : null,
                        MefCollisionMesh.TryRead(wreck.Model.Container, meshIndex: 1, out MefCollisionMesh? readWreckPlayer, collision)
                            ? readWreckPlayer
                            : null);
                    cache[wreck.Model] = wreckMeshes;
                }

                string wreckLabel = $"{label} (destroyed: {wreck.Instance.Name})";
                if (wreckMeshes.Default is { Faces.Length: > 0 } wreckDefault)
                {
                    entries.Add(ObjectCollisionEntry.Create(
                        wreckDefault, origin, placement.Orientation, placement.Instance.Name, wreckLabel, placement.TaskName,
                        placement.Owner, placement, isDestroyedModel: true));
                }

                if (wreckMeshes.Player is { Faces.Length: > 0 } wreckPlayer)
                {
                    playerEntries.Add(ObjectCollisionEntry.Create(
                        wreckPlayer, origin, placement.Orientation, placement.Instance.Name, wreckLabel, placement.TaskName,
                        placement.Owner, placement, isPlayerSet: true, isDestroyedModel: true));
                }
            }
        }

        return ([.. entries], [.. playerEntries]);
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
        out Vector3d origin1)
    {
        level0 = level1 = null;
        origin0 = origin1 = default;
        if (placement.BentGeometry is null
            || placement.Owner is not DeclaredTask waypoint
            || !string.Equals(waypoint.Type.Name, WorldScene.SplineWaypointType, StringComparison.Ordinal)
            || waypoint.Parent is not { } spline)
        {
            return false;
        }

        DeclaredTask? previous = null;
        foreach (GameTask child in spline.Children)
        {
            if (ReferenceEquals(child, waypoint))
            {
                break;
            }

            if (child is DeclaredTask candidate
                && string.Equals(candidate.Type.Name, WorldScene.SplineWaypointType, StringComparison.Ordinal))
            {
                previous = candidate;
            }
        }

        if (previous is null)
        {
            return false;
        }

        MefModel placed = placement.Model;
        Func<string, MefModel?> resolve = models
            ?? (name => string.Equals(name, placement.Instance.Name, StringComparison.OrdinalIgnoreCase) ? placed : null);
        Func<string, MefModel?> withPlaced = name => resolve(name)
            ?? (string.Equals(name, placement.Instance.Name, StringComparison.OrdinalIgnoreCase) ? placed : null);

        bool any = false;
        for (int level = 0; level < 2; level++)
        {
            if (RetailWorldObjects.BuildSegment(previous, waypoint, withPlaced, level, out _) is not { } segment
                || !MefCollisionMesh.TryRead(placed.Container, level, out MefCollisionMesh? source, collision)
                || source is null
                || source.Vertices.Count * 3 != segment.Vertices.Length
                || source.Spheres.Count * 4 != segment.Spheres.Length)
            {
                continue;
            }

            Vector3f[] vertices = new Vector3f[source.Vertices.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3f(segment.Vertices[3 * i], segment.Vertices[(3 * i) + 1], segment.Vertices[(3 * i) + 2]);
            }

            MefCollisionSphere[] spheres = new MefCollisionSphere[source.Spheres.Count];
            for (int i = 0; i < spheres.Length; i++)
            {
                MefCollisionSphere authored = source.Spheres[i];
                spheres[i] = authored with
                {
                    Centre = new Vector3f(segment.Spheres[4 * i], segment.Spheres[(4 * i) + 1], segment.Spheres[(4 * i) + 2]),
                    Radius = segment.Spheres[(4 * i) + 3],
                };
            }

            Vector3d origin = new(segment.Position[0], segment.Position[1], segment.Position[2]);
            if (level == 0)
            {
                level0 = source.WithVertices(vertices, spheres);
                origin0 = origin;
            }
            else
            {
                level1 = source.WithVertices(vertices, spheres);
                origin1 = origin;
            }

            any = true;
        }

        return any;
    }

    private static bool IsCharacterPlacement(WorldPlacement placement) =>
        WorldScene.IsCharacterTypeName(placement.Owner.Type.Name);
}

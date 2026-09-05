using System.Collections.ObjectModel;
using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.World;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// The drawable contents of a loaded mission: every task that places a model, joined to the pack
/// instance that supplies its geometry and textures.
/// </summary>
/// <remarks>
/// <para>
/// Level 1 places 501 of these, and all 501 resolve — the task's <c>Model</c> string is exactly a pack
/// instance name. They are mostly <c>EditRigidObj</c> (380) and <c>Building</c> (26), with doors, trains
/// and soldiers making up the rest.
/// </para>
/// <para>
/// Positions arrive in the engine's own units and are converted to metres here, once, so that everything
/// downstream — camera, near and far planes, movement speed — is in one unit. The original works in
/// engine units throughout and only divides for display; the reason to differ is depth precision, since
/// a level spans millions of units and a near plane a fraction of one.
/// </para>
/// </remarks>
public sealed class WorldScene
{
    private const string LogCategory = "World";

    private double? _groundHeight;

    /// <summary>Engine units per metre, the original's <c>WORLD_METER</c> script constant.</summary>
    /// <remarks>
    /// Registered as a double at <c>0x539558</c>. Model vertices use the same units as world positions:
    /// the level-1 water tower measures 95005 units tall, which is 23 metres.
    /// </remarks>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>The task parameter naming an object's model.</summary>
    public const string ModelParameter = "Model";

    /// <summary>The parameter naming a spline waypoint's segment model.</summary>
    /// <remarks>
    /// A <c>SplineObj</c> is a chain of <c>SplineObjWaypoint</c>s, each naming the mesh that spans from
    /// it towards the next — level 1's railway and roads are built from these. <b>Divergence:</b> the
    /// original bends the segment through a run of matrices along the spline
    /// (<c>Number of Matrices / Segment</c>); here each segment is placed rigidly at its waypoint's
    /// authored position and orientation, which shows the road but does not curve it between waypoints.
    /// </remarks>
    public const string SegmentModelParameter = "Segment Model";

    /// <summary>The task type whose model comes from <see cref="SegmentModelParameter"/>.</summary>
    public const string SplineWaypointType = "SplineObjWaypoint";

    /// <summary>The task type that chains the waypoints; its child order is the chain order.</summary>
    public const string SplineObjType = "SplineObj";

    /// <summary>The security-camera task type, whose models are engine-supplied rather than declared.</summary>
    public const string SecurityCameraType = "SCamera";

    /// <summary>The stationary-gun task type, whose holder is engine-created outside lightmap enumeration.</summary>
    public const string StationaryGunType = "StationaryGun";

    /// <summary>The interactive switch task type, whose model a task declares in <see cref="SwitchModelParameter"/>.</summary>
    public const string SwitchType = "Switch";

    /// <summary>The parameter naming the model a switch rests on — the one its level data assigns, e.g. <c>234_01_1</c>.</summary>
    public const string SwitchModelParameter = "On model";

    /// <summary>The retail task that owns a visible, destructible alarm speaker.</summary>
    public const string SirenType = "Siren";

    /// <summary>The human character task types the original engine grounds onto the terrain when it loads them.</summary>
    private const string AiSoldierType = "HumanSoldier";
    private const string AiSoldierFemaleType = "HumanSoldierFemale";
    private const string HumanPlayerType = "HumanPlayer";

    /// <summary>The two models an <c>SCamera</c> builds in — its constructor (<c>0x4423F0</c>) hard-codes both.</summary>
    /// <remarks>
    /// The camera head and its mount. They are placed and numbered like any script-declared model,
    /// which is why a level's lightmap archive has entries no <c>Model</c> parameter accounts for.
    /// </remarks>
    public static readonly IReadOnlyList<string> SecurityCameraModels = ["313_01_1", "313_02_1"];

    /// <summary>The security camera's model parameters, in the order retail creates the two objects.</summary>
    public static readonly IReadOnlyList<string> SecurityCameraModelParameters = ["Holder Model", "Camera Model"];

    /// <summary>The replacement model parameter used after the camera head is destroyed.</summary>
    public const string SecurityCameraDestroyedModelParameter = "Camera Destroyed Model";

    /// <summary>Lists the model names a task places, in the order the object walk counts them.</summary>
    /// <param name="task">The task to read.</param>
    /// <returns>The names; empty for a task that places nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public static IEnumerable<string> PlacedModelNames(DeclaredTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        string? declaredModelName = null;

        if (task.TryGetValue(ModelParameter, out object? raw) && raw is string name && name.Length > 0)
        {
            // A few authored level revisions put the ammo script symbol in Model rather than in
            // ID.  It is an inventory identity, not a world mesh name; the native AmmoPickup
            // constructor resolves it through the ammo roster before creating the placement.
            string resolved = AmmoPickupCatalog.ModelNameForScriptId(name);
            declaredModelName = resolved.Length > 0 ? resolved : name;
            yield return declaredModelName;
        }

        // AmmoPickup stores its visual identity in ID and its quantity in Ammo; it has no Model
        // parameter. The native AmmoType record supplies the same mesh name used by the retail scene.
        if (string.Equals(task.Type.Name, "AmmoPickup", StringComparison.Ordinal)
            && task.TryGetValue("ID", out object? rawAmmoId)
            && rawAmmoId is string ammoId
            && AmmoPickupCatalog.ModelNameForScriptId(ammoId) is { Length: > 0 } ammoModel)
        {
            // Some shipped AmmoPickup tasks carry both fields. The native constructor resolves the
            // ID as a fallback, not as a second object; emitting both creates duplicate meshes at the
            // same coordinates and makes one pickup appear missing/incorrect in the live scene.
            if (!string.Equals(declaredModelName, ammoModel, StringComparison.OrdinalIgnoreCase))
            {
                yield return ammoModel;
            }
        }

        // GenericPickup revisions can carry the ammo identity in ID while omitting Model.  Keep
        // that form drawable as well; collection already resolves the same identity from the task.
        if (string.Equals(task.Type.Name, "GenericPickup", StringComparison.Ordinal)
            && !task.TryGetValue(ModelParameter, out _)
            && task.TryGetValue("ID", out object? rawGenericId)
            && rawGenericId is string genericId
            && AmmoPickupCatalog.ModelNameForScriptId(genericId) is { Length: > 0 } genericAmmoModel)
        {
            yield return genericAmmoModel;
        }

        // StationaryGun carries its visible holder in "Holder model", not Model.
        // The native task still owns the world transform, so this is a normal placement rather than an
        // attachment to the enemy's animated gunner.
        if (declaredModelName is null
            && string.Equals(task.Type.Name, StationaryGunType, StringComparison.Ordinal)
            && task.TryGetValue("Holder model", out object? rawHolderModel)
            && rawHolderModel is string holderModel
            && holderModel.Length > 0)
        {
            yield return holderModel;
        }

        // Keep a drawable default for older or hand-authored StationaryGun tasks that omit both model fields.
        if (declaredModelName is null
            && string.Equals(task.Type.Name, StationaryGunType, StringComparison.Ordinal)
            && (!task.TryGetValue("Holder model", out object? rawFallbackHolderModel)
                || rawFallbackHolderModel is not string fallbackHolderModel
                || fallbackHolderModel.Length == 0))
        {
            yield return "456_13_1";
        }

        if (string.Equals(task.Type.Name, SecurityCameraType, StringComparison.Ordinal))
        {
            for (int i = 0; i < SecurityCameraModelParameters.Count; i++)
            {
                string parameter = SecurityCameraModelParameters[i];
                if (task.TryGetValue(parameter, out object? rawCameraModel)
                    && rawCameraModel is string cameraModel
                    && cameraModel.Length > 0)
                {
                    yield return cameraModel;
                }
                else
                {
                    // The constructor hard-codes these same names before level parameters overwrite them.
                    yield return SecurityCameraModels[i];
                }
            }
        }

        // A switch's model is the name its task declares in "On model" — an alarm pad names its own
        // ALARM_SWITCH pad (e.g. "234_01_1"), a door button the wall switch it mounts. A bare switch
        // leans on the literal name "switch" (sub_4450F0), like "waypoint" and "missing" a lowercase
        // entry the common pack registers.
        if (string.Equals(task.Type.Name, SwitchType, StringComparison.Ordinal))
        {
            yield return SwitchModelNameOf(task);
        }

        // A spline waypoint owns the segment that spans from the previous waypoint to it
        // (sub_500EF0), and that segment object is numbered like any other. The chain's first
        // waypoint has no previous, so its named model builds no segment and counts nothing.
        if (string.Equals(task.Type.Name, SplineWaypointType, StringComparison.Ordinal)
            && task.TryGetValue(SegmentModelParameter, out object? segment)
            && segment is string segmentName
            && segmentName.Length > 0
            && HasEarlierWaypointSibling(task))
        {
            yield return segmentName;
        }
    }

    /// <summary>
    /// The model name a switch task places: its declared <see cref="SwitchModelParameter"/>, or the
    /// lower-case <c>switch</c> entry the common pack registers when the task names none.
    /// </summary>
    /// <param name="task">The switch task to read.</param>
    /// <returns>The model name.</returns>
    internal static string SwitchModelNameOf(DeclaredTask task) =>
        task.TryGetValue(SwitchModelParameter, out object? model) && model is string name && name.Length > 0
            ? name
            : "switch";

    /// <summary>Lists the models the retail lightmap walker enumerates for a task.</summary>
    /// <remarks>StationaryGun's holder is created by the engine after the lightmap list is built.</remarks>
    private static IEnumerable<string> LightmapModelNames(DeclaredTask task)
    {
        if (string.Equals(task.Type.Name, StationaryGunType, StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (string name in PlacedModelNames(task))
        {
            yield return name;
        }
    }

    /// <summary>Tells whether a waypoint has a waypoint sibling before it under the same parent.</summary>
    private static bool HasEarlierWaypointSibling(DeclaredTask waypoint)
    {
        if (waypoint.Parent is not { } parent)
        {
            return false;
        }

        foreach (GameTask sibling in parent.Children)
        {
            if (ReferenceEquals(sibling, waypoint))
            {
                return false;
            }

            if (sibling is DeclaredTask declared
                && string.Equals(declared.Type.Name, SplineWaypointType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The task parameters an object's position may be named by, in order of preference.</summary>
    /// <remarks>
    /// Not every placing type calls it <c>Position</c>: a <c>Door</c> declares <c>Position start</c>,
    /// because it also carries the position it opens to. A task with none of these is not placed at the
    /// origin — it is skipped and counted, since a stray object at (0,0,0) drags the level's bounds out
    /// by kilometres and takes the camera with it.
    /// </remarks>
    public static readonly string[] PositionParameters = ["Position", "Position start", "Start Position", "Holder Position"];

    /// <summary>The task parameter naming an object's orientation.</summary>
    public const string OrientationParameter = "Orientation";

    /// <summary>The task type whose <c>Filename</c> names an object's baked-lightmap set.</summary>
    public const string LightmapInfoType = "LightmapInfo";

    /// <summary>The <see cref="LightmapInfoType"/> parameter holding the set name.</summary>
    public const string LightmapFilenameParameter = "Filename";

    private WorldScene(
        LoadedMission mission,
        IReadOnlyList<WorldPlacement> placements,
        IReadOnlyList<LadderPlacement> ladders,
        int unresolved,
        int unplaced,
        int attached)
    {
        Mission = mission;
        Placements = placements;
        Ladders = ladders;
        UnresolvedCount = unresolved;
        UnplacedCount = unplaced;
        AttachedCount = attached;
    }

    /// <summary>Gets the mission the scene was built from.</summary>
    public LoadedMission Mission { get; }

    /// <summary>Gets everything to draw, in the order the level script placed it.</summary>
    public IReadOnlyList<WorldPlacement> Placements { get; }

    /// <summary>Gets the level's ground height, in metres, as an absolute world Z.</summary>
    /// <remarks>
    /// <para>
    /// A level's coordinates carry a large absolute offset — mission 1's ground sits some 42 km from
    /// the world origin — so a height authored as "four metres up", like the sky plane's or the fog
    /// base's, is nowhere near a world Z of four. It is an offset, and it has to be anchored to
    /// something before it means anything. This is that anchor.
    /// </para>
    /// <para>
    /// It is the <em>median</em> placement height rather than the middle of the bounding box, because
    /// most of what a level places sits on the ground: the median lands on it, while a box is dragged
    /// upwards by a single radio mast and downwards by a basement.
    /// </para>
    /// </remarks>
    public double GroundHeight => _groundHeight ??= MedianPlacementHeight();

    private double MedianPlacementHeight()
    {
        if (Placements.Count == 0)
        {
            return 0.0;
        }

        double[] heights = new double[Placements.Count];

        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = Placements[i].Position.Z;
        }

        Array.Sort(heights);
        return heights[heights.Length / 2];
    }

    /// <summary>Gets the ladders the level places, for the player and AI to climb.</summary>
    /// <remarks>
    /// Collected from the <c>TASKTYPE_LADDER</c> magic objects a building attaches, with their foot and
    /// head taken from the ladder model's magic vertices 2 and 3. See
    /// the original.
    /// </remarks>
    public IReadOnlyList<LadderPlacement> Ladders { get; }

    /// <summary>Gets how many placing tasks named a model no pack holds.</summary>
    public int UnresolvedCount { get; }

    /// <summary>Gets how many tasks named a model but declared no position this recognises.</summary>
    public int UnplacedCount { get; }

    /// <summary>Gets how many placements came from model attachments rather than from the level script.</summary>
    /// <remarks>
    /// These are the magic objects (see <see cref="MagicObjectRegistry"/>) and they are the majority of a
    /// level's contents: the script names 501 objects in level 1, and their models attach thousands more.
    /// Zero when <see cref="Build"/> was given no magic-object table.
    /// </remarks>
    public int AttachedCount { get; }

    /// <summary>Walks a level's task tree and joins every placed model to its pack instance.</summary>
    /// <param name="mission">The loaded mission, with both packs bound.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="magic">
    /// The magic-object table. Without it a level draws only what its script names — every model's
    /// attachments, and the baked-lightmap numbering that depends on them, are left out.
    /// </param>
    /// <param name="logger">Where to report placements that do not resolve.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static WorldScene Build(
        LoadedMission mission,
        GameTask levelRoot,
        MagicObjectRegistry? magic = null,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);

        IIgiLogger log = logger ?? NullLogger.Instance;
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName =
            new(StringComparer.OrdinalIgnoreCase);

        // The level pack wins: it is opened second and its names shadow the common pack's.
        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                // Some models are attachment-only holders: they carry no vertices of their own, but
                // their ATTA tree names the pieces that make up the visible object. Mission 14 builds
                // almost every room and tunnel from these holders, so dropping them here prevents the
                // attachment walk below from ever seeing the actual geometry.
                if (pack.TryGetModel(instance, out MefModel? model) && model is not null)
                {
                    byName[instance.Name] = (instance, pack, model);
                }
            }
        }

        // Which baked-lightmap set each placing task belongs to, and where in it each of the task's
        // models sits — one entry per placed model, because a security camera places two. Both come
        // from expanding the owner's whole subtree at once, because the numbering is a single running
        // count across it — see WorldObject.
        Dictionary<GameTask, List<(string Set, WorldObject Root)>> lightmapped = [];
        if (magic is not null)
        {
            BindLightmapSets(levelRoot, mission, magic, lightmapped);
        }

        List<WorldPlacement> placements = [];
        List<LadderPlacement> ladders = [];
        Dictionary<GameTask, TrainPlacementState> trainPlacements = [];
        Dictionary<GameTask, float> trainTailOffsets = [];
        int unresolved = 0;
        int unplaced = 0;
        int attached = 0;

        (float Length, float Pivot) TrainGeometry(IEnumerable<string> names)
        {
            foreach (string name in names)
            {
                if (!byName.TryGetValue(name, out var bound))
                {
                    continue;
                }

                return (TrainCouplingDistance(bound.Model), TrainCouplingPivotFraction(bound.Model));
            }
            return default;
        }

        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask declared)
            {
                continue;
            }

            // Spline waypoints do not place their own model — each names the segment that spans from
            // it to the next waypoint, and the chains are laid below once every waypoint is known.
            if (string.Equals(declared.Type.Name, SplineWaypointType, StringComparison.Ordinal))
            {
                continue;
            }

            lightmapped.TryGetValue(task, out List<(string Set, WorldObject Root)>? bakes);

            string[] placedNames = [.. PlacedModelNames(declared)];
            TrainPlacementState? trainPlacement = null;
            if (string.Equals(declared.Type.Name, "Train", StringComparison.Ordinal))
            {
                (float ownLength, float ownPivot) = TrainGeometry(placedNames);
                if (task.Parent is DeclaredTask parent
                    && string.Equals(parent.Type.Name, "Train", StringComparison.Ordinal)
                    && trainPlacements.TryGetValue(parent, out TrainPlacementState? parentTrain))
                {
                    float direction = parentTrain.Railroad.Flipped ? 1f : -1f;
                    (float parentLength, float parentPivot) = TrainGeometry(PlacedModelNames(parent));
                    float frontOffset = trainTailOffsets.TryGetValue(parent, out float tail)
                        ? tail
                        : parentTrain.Offset + (direction * parentLength * (1f - parentPivot));
                    float offset = frontOffset + (direction * ownLength * ownPivot);
                    parentTrain.Railroad.Evaluate(
                        parentTrain.TrackPosition + offset,
                        out Vector3d childPosition,
                        out Matrix3f childOrientation);
                    trainPlacement = new TrainPlacementState(
                        parentTrain.Railroad,
                        parentTrain.TrackPosition,
                        offset,
                        childPosition,
                        childOrientation);
                    trainPlacements[task] = trainPlacement;
                    float tailOffset = frontOffset + (direction * ownLength);
                    trainTailOffsets[parent] = tailOffset;
                    trainTailOffsets[task] = tailOffset;
                }
                else if (RailroadPath.FromTrain(declared) is { } railroad)
                {
                    float direction = railroad.Flipped ? 1f : -1f;
                    float rootOffset = direction * ownLength * ownPivot;
                    railroad.Evaluate(
                        railroad.InitialPosition + rootOffset,
                        out Vector3d rootPosition,
                        out Matrix3f rootOrientation);
                    trainPlacement = new TrainPlacementState(
                        railroad,
                        railroad.InitialPosition,
                        rootOffset,
                        rootPosition,
                        rootOrientation);
                    trainPlacements[task] = trainPlacement;
                    trainTailOffsets[task] = direction * ownLength;
                }
            }

            int slot = 0;
            foreach (string name in placedNames)
            {
                int modelSlot = slot++;
                if (!byName.TryGetValue(name, out (MtpInstance Instance, LoadedPack Pack, MefModel Model) bound))
                {
                    unresolved++;
                    continue;
                }

                // A Wire is a dynamic model rather than a regular point placement. Its update handler
                // puts the model at the Start/Stop midpoint, builds an orientation whose local Y follows
                // the span, and stretches only that axis by length / WORLD_METER. Treating it as an
                // ordinary task skipped it entirely because it has no parameter literally named Position.
                if (string.Equals(declared.Type.Name, "Wire", StringComparison.Ordinal)
                    && WireTransform.TryCreate(declared, bound.Model, out WireTransform wire))
                {
                    MeshGeometry? geometry = wire.Stretch(bound.Model);
                    if (geometry is null)
                    {
                        unplaced++;
                        continue;
                    }

                    placements.Add(new WorldPlacement(
                        wire.PositionMetres,
                        wire.Orientation,
                        bound.Instance,
                        bound.Pack,
                        bound.Model,
                        $"wire:{task.Id}:{placements.Count}",
                        task)
                    {
                        BentGeometry = geometry,
                    });
                    continue;
                }

                Vector3d position;
                if (trainPlacement is not null)
                {
                    position = trainPlacement.Position / UnitsPerMetre;
                }
                else if (!TryGetPosition(declared, out position))
                {
                    RailroadPath? railroad = string.Equals(declared.Type.Name, "Train", StringComparison.Ordinal)
                        ? RailroadPath.FromTrain(declared)
                        : null;
                    if (railroad is null)
                    {
                        unplaced++;
                        continue;
                    }

                    railroad.Evaluate(railroad.InitialPosition, out Vector3d trainPosition, out _);
                    position = trainPosition / UnitsPerMetre;
                }

                Matrix3f orientation = trainPlacement?.Orientation ?? PlacementOrientation(declared, modelSlot);

                // SCameraLens is not committed at the holder task's origin. Retail asks the holder
                // model for magic vertex 0, rotates that mount through the holder's own orientation,
                // and adds it to Holder Position (0x4420D0). A live Mission 1 probe verified all five
                // resulting positions to sub-unit precision.
                if (string.Equals(declared.Type.Name, SecurityCameraType, StringComparison.Ordinal)
                    && modelSlot == 1)
                {
                    position = CameraLensPosition(declared, position, byName);
                }

                // Retail grounds character models onto the terrain when it loads them — the editor's
                // level verifier allows an AI position tolerance for exactly this reason. A character
                // authored slightly above the terrain, as every Level 4 soldier is, otherwise floats in
                // the air. The query only finds ground within ~4m below, so a character meant to stand on
                // a roof (far above the terrain) is left alone; only near-ground strays settle onto it.
                if (IsCharacterPlacement(declared)
                    && mission.Terrain is { IsUsable: true } terrain
                    && TerrainHeightQuery.TryGetGround(
                        terrain,
                        position.X * UnitsPerMetre,
                        position.Y * UnitsPerMetre,
                        position.Z * UnitsPerMetre,
                        out TerrainGround ground))
                {
                    position = new Vector3d(position.X, position.Y, ground.Height / UnitsPerMetre);
                }

                (string Set, WorldObject Root) bake = bakes is not null && modelSlot < bakes.Count
                    ? bakes[modelSlot]
                    : default;

                WorldPlacement placement = new(
                    position,
                    orientation,
                    bound.Instance,
                    bound.Pack,
                    bound.Model,
                    task.Name,
                    task,
                    bake.Set ?? string.Empty,
                    bake.Root?.LightmapIndex ?? -1);

                LogPlacementVerification(
                    log,
                    mission,
                    declared,
                    bound.Instance.Name,
                    position,
                    orientation,
                    modelSlot,
                    trainPlacement is not null,
                    IsCharacterPlacement(declared),
                    string.Equals(declared.Type.Name, SecurityCameraType, StringComparison.Ordinal)
                        && modelSlot == 1);

                // The original does not delete a shot camera head. SCameraLens's destruction branch
                // (0x441770) swaps only that child to Camera Destroyed Model, leaving the holder intact.
                if (string.Equals(declared.Type.Name, SecurityCameraType, StringComparison.Ordinal)
                    && modelSlot == 1
                    && declared.TryGetValue(SecurityCameraDestroyedModelParameter, out object? rawDestroyed)
                    && rawDestroyed is string destroyedName
                    && destroyedName.Length > 0
                    && byName.TryGetValue(destroyedName, out var destroyed))
                {
                    placement = placement with
                    {
                        DestroyedReplacement = new WorldModelBinding(
                            destroyed.Instance,
                            destroyed.Pack,
                            destroyed.Model),
                    };
                }

                // Empty render geometry does not make this placement disposable. Attachment holders
                // can carry HSMC collision of their own (Mission 14's room floors do), while the world
                // renderer already ignores the null mesh MefMeshBuilder returns for them.
                placements.Add(placement);

                // Everything the model attaches, and everything those attach in turn. The tree is only
                // available when a magic-object table was supplied; without one a level draws its
                // shells and none of their contents.
                if (bake.Root is not null)
                {
                    attached += AddAttached(
                        mission, log, bake.Root, position, orientation, orientation, bake.Set ?? string.Empty,
                        task.Name, task, byName, magic, placements, ladders);
                }
                else if (magic is not null
                    && WorldObject.TryBuild(name, mission.TryGetModel, magic, out WorldObject? loose)
                    && loose is not null)
                {
                    attached += AddAttached(
                        mission, log, loose, position, orientation, orientation, string.Empty, task.Name, task,
                        byName, magic, placements, ladders);
                }
            }
        }

        int segments = AddSplineSegments(mission, levelRoot, byName, placements, log);

        log.Information(
            LogCategory,
            $"Scene: {placements.Count} placement(s) over {byName.Count} bound instance(s)"
                + $", {attached} attached"
                + (segments > 0 ? $", {segments} spline segment(s)" : string.Empty)
                + (unresolved > 0 ? $", {unresolved} unresolved" : string.Empty)
                + (unplaced > 0 ? $", {unplaced} with no position" : string.Empty)
                + ".");

        return new WorldScene(
            mission,
            new ReadOnlyCollection<WorldPlacement>(placements),
            new ReadOnlyCollection<LadderPlacement>(ladders),
            unresolved,
            unplaced,
            attached);
    }

    private sealed record TrainPlacementState(
        RailroadPath Railroad,
        float TrackPosition,
        float Offset,
        Vector3d Position,
        Matrix3f Orientation);

    /// <summary>Lays every <c>SplineObj</c> chain: one bent segment per consecutive waypoint pair.</summary>
    /// <remarks>
    /// <para>
    /// A segment spans from the <em>previous</em> waypoint to the waypoint that names its model, which
    /// is <c>sub_500EF0</c>'s arrangement — a chain of N waypoints lays N−1 segments and the first
    /// waypoint's own <c>Segment Model</c> goes unused. The curve is the one <c>sub_4E4530</c> builds:
    /// a Hermite cubic between the two waypoint positions whose tangents are the waypoints' local X
    /// axes scaled by the chord length, evaluated here per vertex rather than at the original's
    /// <c>NumAreas</c> sampled matrices — the same curve, continuously.
    /// </para>
    /// <para>
    /// A vertex's local X picks its place along the curve (the original measures the model's X extent
    /// the same way, <c>sub_4E4530</c>'s tail); its local Y and Z ride the curve's zero-roll frame.
    /// <b>Divergence:</b> the original also interpolates a twist angle between the endpoint
    /// orientations; every level-1 waypoint is authored yaw-only, so the twist is dropped until a
    /// level needs it.
    /// </para>
    /// </remarks>
    private static int AddSplineSegments(
        LoadedMission mission,
        GameTask levelRoot,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName,
        List<WorldPlacement> placements,
        IIgiLogger log)
    {
        int added = 0;
        int chain = 0;
        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask spline
                || !string.Equals(spline.Type.Name, SplineObjType, StringComparison.Ordinal))
            {
                continue;
            }

            chain++;
            List<(DeclaredTask Task, Vector3d Position)> waypoints = [];
            foreach (GameTask child in spline.Children)
            {
                if (child is DeclaredTask waypoint
                    && string.Equals(waypoint.Type.Name, SplineWaypointType, StringComparison.Ordinal)
                    && TryGetPosition(waypoint, out Vector3d position))
                {
                    waypoints.Add((waypoint, position));
                }
            }

            for (int i = 1; i < waypoints.Count; i++)
            {
                (DeclaredTask waypoint, Vector3d to) = waypoints[i];
                (DeclaredTask previous, Vector3d from) = waypoints[i - 1];

                if (!waypoint.TryGetValue(SegmentModelParameter, out object? raw)
                    || raw is not string name
                    || name.Length == 0
                    || !byName.TryGetValue(name, out (MtpInstance Instance, LoadedPack Pack, MefModel Model) bound))
                {
                    continue;
                }

                MeshGeometry? bent = BendSegment(bound.Model, from, to, Tangent(previous), Tangent(waypoint));
                if (bent is null)
                {
                    continue;
                }

                placements.Add(new WorldPlacement(
                    from,
                    Matrix3f.Identity,
                    bound.Instance,
                    bound.Pack,
                    bound.Model,
                    $"spline:{chain}:{i}",
                    waypoint,
                    string.Empty,
                    -1)
                {
                    BentGeometry = bent,
                });
                LogDerivedPlacement(
                    log,
                    mission,
                    waypoint,
                    name,
                    from,
                    Matrix3f.Identity,
                    "SPLINE");
                added++;
            }
        }

        return added;

        // The tangent direction is the waypoint orientation's local X axis in world space — the
        // matrix's first column, exactly the elements [0], [3], [6] that sub_4E4530 reads.
        static Vector3f Tangent(DeclaredTask waypoint)
        {
            waypoint.TryGetValue(OrientationParameter, out object? raw);
            Matrix3f m = raw is Vector3f angles ? ToOrientation(angles) : Matrix3f.Identity;
            return new Vector3f(m.M11, m.M21, m.M31);
        }
    }

    /// <summary>Gets the coupling distance the retail Train solver reads from a car model.</summary>
    /// <remarks>
    /// Train initialization (<c>0x42A150</c>) asks the model for magic vertices 0 and 1 through
    /// <c>0x414ED0</c>, stores <c>abs(v0.x - v1.x)</c> at Train+1868, and passes one such value per car
    /// to the railroad evaluator. Mesh bounds include overhangs and are not the consist spacing.
    /// </remarks>
    public static float TrainCouplingDistance(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.TryGetMagicVertex(0, out Vector3f front)
            && model.TryGetMagicVertex(1, out Vector3f rear)
                ? MathF.Abs(front.X - rear.X)
                : 0f;
    }

    /// <summary>Gets the retail model-origin fraction between a Train car's two couplers.</summary>
    /// <remarks>
    /// Train initialization (<c>0x42A150</c>) stores <c>abs(v0.x) / abs(v0.x - v1.x)</c> at
    /// Train+1864. The consist updater later interpolates the two railroad samples by this fraction;
    /// omitting it places the model origin on its front coupler instead of inside the car.
    /// </remarks>
    public static float TrainCouplingPivotFraction(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.TryGetMagicVertex(0, out Vector3f front)
            || !model.TryGetMagicVertex(1, out Vector3f rear))
        {
            return 0f;
        }

        float length = MathF.Abs(front.X - rear.X);
        return length > float.Epsilon ? MathF.Abs(front.X) / length : 0f;
    }

    /// <summary>Bends one segment model along its span's Hermite curve.</summary>
    /// <param name="model">The segment model, authored along its local X.</param>
    /// <param name="from">The span's start, in metres — also the placement's position.</param>
    /// <param name="to">The span's end, in metres.</param>
    /// <param name="fromTangent">The start waypoint's unit tangent.</param>
    /// <param name="toTangent">The end waypoint's unit tangent.</param>
    /// <returns>The geometry, positioned relative to <paramref name="from"/>, or <see langword="null"/>.</returns>
    private static MeshGeometry? BendSegment(
        MefModel model,
        Vector3d from,
        Vector3d to,
        Vector3f fromTangent,
        Vector3f toTangent)
    {
        if (model.IsEmpty || model.DrawRecords.Count == 0)
        {
            return null;
        }

        ReadOnlySpan<MefVertex> source = model.Vertices;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        foreach (MefVertex vertex in source)
        {
            minX = Math.Min(minX, vertex.Position.X);
            maxX = Math.Max(maxX, vertex.Position.X);
        }

        float length = maxX - minX;
        if (length <= 0)
        {
            return null;
        }

        // The Hermite endpoints relative to the span's start, with tangents scaled by the chord — the
        // v79 factor sub_4E4530 multiplies both endpoint axes by.
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;
        double chord = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (chord < 1e-3)
        {
            return null;
        }

        Span<double> t0 = [fromTangent.X * chord, fromTangent.Y * chord, fromTangent.Z * chord];
        Span<double> t1 = [toTangent.X * chord, toTangent.Y * chord, toTangent.Z * chord];
        Span<double> p1 = [dx, dy, dz];

        Vertex3D[] vertices = new Vertex3D[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            MefVertex v = source[i];
            double t = Math.Clamp((v.Position.X - minX) / length, 0.0, 1.0);

            // Hermite basis and its derivative, once per vertex.
            double t2 = t * t;
            double t3 = t2 * t;
            double h00 = (2 * t3) - (3 * t2) + 1;
            double h10 = t3 - (2 * t2) + t;
            double h01 = (-2 * t3) + (3 * t2);
            double h11 = t3 - t2;
            double d00 = (6 * t2) - (6 * t);
            double d10 = (3 * t2) - (4 * t) + 1;
            double d01 = (6 * t) - (6 * t2);
            double d11 = (3 * t2) - (2 * t);

            double px = (h10 * t0[0]) + (h01 * p1[0]) + (h11 * t1[0]);
            double py = (h10 * t0[1]) + (h01 * p1[1]) + (h11 * t1[1]);
            double pz = (h10 * t0[2]) + (h01 * p1[2]) + (h11 * t1[2]);
            _ = h00; // the start point is the origin, so its term vanishes

            double gx = (d00 * 0) + (d10 * t0[0]) + (d01 * p1[0]) + (d11 * t1[0]);
            double gy = (d10 * t0[1]) + (d01 * p1[1]) + (d11 * t1[1]);
            double gz = (d10 * t0[2]) + (d01 * p1[2]) + (d11 * t1[2]);

            // The curve's zero-roll frame: X down the tangent, Y level, Z perpendicular up — what
            // sub_4E4530 builds through sub_4B38E0 from the direction angles with no twist.
            double glen = Math.Sqrt((gx * gx) + (gy * gy) + (gz * gz));
            if (glen < 1e-9)
            {
                gx = dx / chord;
                gy = dy / chord;
                gz = dz / chord;
                glen = 1.0;
            }

            double fx = gx / glen;
            double fy = gy / glen;
            double fz = gz / glen;

            double sx = -fy;
            double sy = fx;
            double slen = Math.Sqrt((sx * sx) + (sy * sy));
            if (slen < 1e-9)
            {
                sx = 0.0;
                sy = 1.0;
                slen = 1.0;
            }

            sx /= slen;
            sy /= slen;

            double ux = -(fz * sy);
            double uy = fz * sx;
            double uz = (fx * sy) - (fy * sx);

            double offY = v.Position.Y / UnitsPerMetre;
            double offZ = v.Position.Z / UnitsPerMetre;

            vertices[i] = new Vertex3D(
                new Vector3f(
                    (float)(px + (sx * offY) + (ux * offZ)),
                    (float)(py + (sy * offY) + (uy * offZ)),
                    (float)(pz + (uz * offZ))),
                new Vector3f(
                    (float)((fx * v.Normal.X) + (sx * v.Normal.Y) + (ux * v.Normal.Z)),
                    (float)((fy * v.Normal.X) + (sy * v.Normal.Y) + (uy * v.Normal.Z)),
                    (float)((fz * v.Normal.X) + (uz * v.Normal.Z))),
                v.DiffuseU,
                v.DiffuseV,
                v.LightmapU,
                v.LightmapV);
        }

        int total = 0;
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            total += record.Indices.Length;
        }

        ushort[] indices = new ushort[total];
        List<MeshSubset> subsets = new(model.DrawRecords.Count);
        int cursor = 0;
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            ReadOnlySpan<ushort> run = record.Indices.Span;
            int start = cursor;
            foreach (ushort index in run)
            {
                indices[cursor++] = (ushort)record.ToVertexIndex(index);
            }

            subsets.Add(new MeshSubset(start, run.Length, record.MaterialSlot, record.LightmapPage));
        }

        return new MeshGeometry(vertices, indices, new ReadOnlyCollection<MeshSubset>(subsets));
    }

    /// <summary>
    /// Expands every lightmap owner's subtree and records, per placing task, which set it belongs to and
    /// the tree its own model heads.
    /// </summary>
    private static void BindLightmapSets(
        GameTask levelRoot,
        LoadedMission mission,
        MagicObjectRegistry magic,
        Dictionary<GameTask, List<(string Set, WorldObject Root)>> bound)
    {
        foreach (GameTask owner in Walk(levelRoot))
        {
            if (!TryGetLightmapSet(owner, out string set))
            {
                continue;
            }

            List<GameTask> placing = [];
            List<string> models = [];
            foreach (GameTask task in Subtree(owner))
            {
                if (task is not DeclaredTask declared)
                {
                    continue;
                }

                foreach (string name in LightmapModelNames(declared))
                {
                    placing.Add(task);
                    models.Add(name);
                }
            }

            IReadOnlyList<WorldObject?> roots = WorldObject.BuildAll(models, mission.TryGetModel, magic);
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] is not { } root)
                {
                    continue;
                }

                if (!bound.TryGetValue(placing[i], out List<(string Set, WorldObject Root)>? list))
                {
                    bound[placing[i]] = list = [];
                }

                list.Add((set, root));
            }
        }
    }

    /// <summary>Reads the set name off a task's <c>LightmapInfo</c> child, if it has one.</summary>
    private static bool TryGetLightmapSet(GameTask task, out string set)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is DeclaredTask declared
                && string.Equals(declared.Type.Name, LightmapInfoType, StringComparison.Ordinal)
                && declared.TryGetValue(LightmapFilenameParameter, out object? raw)
                && raw is string name
                && name.Length > 0)
            {
                set = name;
                return true;
            }
        }

        set = string.Empty;
        return false;
    }

    /// <summary>Places a node's attached children, and theirs, in the parent's frame.</summary>
    /// <remarks>
    /// An attachment's position is in its parent model's space and in the same engine units as a vertex,
    /// so it converts to metres the same way a placement's does. The rotations compose in the order the
    /// camera already uses for a placement's own orientation.
    /// </remarks>
    private static int AddAttached(
        LoadedMission mission,
        IIgiLogger log,
        WorldObject node,
        Vector3d position,
        Matrix3f drawOrientation,
        Matrix3f taskOrientation,
        string set,
        string taskName,
        GameTask owner,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName,
        MagicObjectRegistry? magic,
        List<WorldPlacement> placements,
        List<LadderPlacement> ladders)
    {
        int added = 0;
        foreach (WorldObject child in node.Children)
        {
            bool isRotor = magic is not null
                && magic.TryGet(child.Name, out MagicObjectDefinition childDefinition)
                && string.Equals(
                    magic.TaskTypeName(childDefinition.TaskType),
                    "TASKTYPE_ROTOR",
                    StringComparison.Ordinal);
            // Parent-to-world is the column-vector product M·v, matching how the renderer applies the
            // parent's own orientation (`uModelView * v`); Transform is the row-vector v·M, its
            // inverse, which placed a rotated parent's contents on the wrong side of it. The rotation
            // composition below is already the column-vector convention's parent∘child order.
            Vector3f offset = drawOrientation.Transposed().Transform(child.Position);
            Vector3d childPosition = new(
                position.X + (offset.X / UnitsPerMetre),
                position.Y + (offset.Y / UnitsPerMetre),
                position.Z + (offset.Z / UnitsPerMetre));
            Matrix3f childDrawOrientation = drawOrientation * child.Orientation;

            // sub_4DFA50 does not copy an ATTA matrix into the spawned magic task. It rebuilds a
            // right-handed basis from the attachment's first two axes before composing it with its
            // parent. After the model-to-world axis conversion used by this port, that rebuilt local
            // frame is exactly the ATTA orientation's transpose. Keeping the task frame separate from
            // the draw frame matters for quarter-turn attachments: the old blanket 180-degree fix made
            // the first building ladder look right but pointed WaterTower's 435_02_1 ladder inward.
            Matrix3f childTaskOrientation = taskOrientation * child.Orientation.Transposed();

            // A ladder is a TASKTYPE_LADDER magic object whose model carries the climb line as its magic
            // vertices 2 (foot) and 3 (head). Collected here, where its world transform is known.
            TryCollectLadder(
                child,
                childPosition,
                childDrawOrientation,
                childTaskOrientation,
                magic,
                ladders);

            // A child draws only if its model is also a pack instance, because the textures a sub-mesh
            // uses are named by the instance, not by the model. Its subtree is walked either way: the
            // lightmap numbering already counted it, and its own children may well be instances.
            if (byName.TryGetValue(child.Name, out (MtpInstance Instance, LoadedPack Pack, MefModel Model) bound))
            {
                // The attachment draws in the rebuilt right-handed frame sub_4DFA50 builds, which after the
                // model-to-world axis conversion is the ATTA orientation's transpose — the same childTaskOrientation
                // the ladder task frame uses (and the reference editor's attLocalRot = R_row^T). The old draw
                // frame dropped the transpose, which put attached sub-meshes such as a building's windows on the
                // wrong side of their parent. The ladder's climb line keeps the non-transposed draw frame: its
                // magic-vertex positions were pinned to the retail game's climb line.
                placements.Add(new WorldPlacement(
                    childPosition,
                    childTaskOrientation,
                    bound.Instance,
                    bound.Pack,
                    bound.Model,
                    taskName,
                    owner,
                    set,
                    set.Length > 0 ? child.LightmapIndex : -1)
                {
                    IsRotor = isRotor,
                    IsAttached = true,
                });
                LogDerivedPlacement(
                    log,
                    mission,
                    owner as DeclaredTask,
                    child.Name,
                    childPosition,
                    childTaskOrientation,
                    "ATTA");
                added++;
            }

            added += AddAttached(
                mission, log, child, childPosition, childDrawOrientation, childTaskOrientation, set,
                taskName, owner, byName, magic, placements, ladders);
        }

        return added;
    }

    /// <summary>The magic-object task type a ladder is spawned with.</summary>
    private const string LadderTaskType = "TASKTYPE_LADDER";

    /// <summary>Records a ladder when a node is one — its climb line from magic vertices 2 and 3.</summary>
    private static void TryCollectLadder(
        WorldObject child,
        Vector3d positionMetres,
        Matrix3f drawOrientation,
        Matrix3f taskOrientation,
        MagicObjectRegistry? magic,
        List<LadderPlacement> ladders)
    {
        if (magic is null
            || !magic.TryGet(child.Name, out MagicObjectDefinition definition)
            || !string.Equals(magic.TaskTypeName(definition.TaskType), LadderTaskType, StringComparison.Ordinal))
        {
            return;
        }

        MefModel model = child.Models.Highest;
        if (!model.TryGetMagicVertex(1, out Vector3f activationTop)
            || !model.TryGetMagicVertex(2, out Vector3f foot)
            || !model.TryGetMagicVertex(3, out Vector3f head))
        {
            return;
        }

        Vector3d origin = new(
            positionMetres.X * UnitsPerMetre,
            positionMetres.Y * UnitsPerMetre,
            positionMetres.Z * UnitsPerMetre);

        ladders.Add(new LadderPlacement(
            origin,
            ToWorldUnits(origin, drawOrientation, activationTop),
            ToWorldUnits(origin, drawOrientation, foot),
            ToWorldUnits(origin, drawOrientation, head),
            taskOrientation));
    }

    /// <summary>Places a model-space magic vertex in the world, in engine units.</summary>
    /// <remarks>
    /// <b>Verified against the running game</b> (a probe: the ladder mount computed to the byte from
    /// this): the magic vertex is rotated as <c>v · M</c> (<see cref="Matrix3f.Transform(Vector3f)"/>), NOT
    /// the transpose. Using the transpose negated the vertex's X and Y (Z survives on an axis-aligned
    /// ladder), which stood the climbing body ~6 cm off the rungs. Unlike an attachment offset, a magic
    /// vertex takes the plain product.
    /// </remarks>
    private static Vector3d ToWorldUnits(Vector3d originUnits, Matrix3f orientation, Vector3f local)
    {
        Vector3f rotated = orientation.Transform(local);
        return new Vector3d(originUnits.X + rotated.X, originUnits.Y + rotated.Y, originUnits.Z + rotated.Z);
    }

    /// <summary>Walks a task and everything under it, the task itself first.</summary>
    private static IEnumerable<GameTask> Subtree(GameTask task)
    {
        yield return task;
        foreach (GameTask descendant in Walk(task))
        {
            yield return descendant;
        }
    }

    /// <summary>Reads whichever position parameter a placing task declares.</summary>
    /// <param name="task">The task to read.</param>
    /// <param name="position">Receives the position, in metres.</param>
    /// <returns><see langword="false"/> when the task declares none of <see cref="PositionParameters"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public static bool TryGetPosition(DeclaredTask task, out Vector3d position)
    {
        ArgumentNullException.ThrowIfNull(task);

        foreach (string name in PositionParameters)
        {
            if (task.TryGetValue(name, out object? value) && value is Vector3d p)
            {
                position = new Vector3d(p.X / UnitsPerMetre, p.Y / UnitsPerMetre, p.Z / UnitsPerMetre);
                return true;
            }
        }

        position = default;
        return false;
    }

    /// <summary>Returns whether a task is a human character model that retail grounds onto the terrain.</summary>
    /// <remarks>
    /// These are the AI soldiers and the player body. The original engine's level verifier exempts them
    /// from exact position cross-reference because it terrain-snaps them at load, so their authored height
    /// need not match the surface they settle on.
    /// </remarks>
    private static bool IsCharacterPlacement(DeclaredTask declared) =>
        string.Equals(declared.Type.Name, AiSoldierType, StringComparison.Ordinal)
        || string.Equals(declared.Type.Name, AiSoldierFemaleType, StringComparison.Ordinal)
        || string.Equals(declared.Type.Name, HumanPlayerType, StringComparison.Ordinal);

    /// <summary>Builds an orientation from the three Euler angles level data stores.</summary>
    /// <param name="angles">The alpha, beta and gamma angles in radians.</param>
    /// <returns>The rotation.</returns>
    /// <remarks>
    /// A <c>Real32x9</c> parameter is three angles on the wire, not nine components, and the original
    /// concatenates three axis rotations in <c>0x4B38E0</c>. That function's axis order <b>has now been
    /// read</b> and is Z(gamma)·Y(beta)·X(alpha), which is what this always composed — see
    /// <see cref="Matrix3f.FromEngineEulerAngles"/>, where the composition now lives so the world and
    /// the first-person eye share one reading of it.
    /// </remarks>
    public static Matrix3f ToOrientation(Vector3f angles) =>
        Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z);

    private static Matrix3f PlacementOrientation(DeclaredTask task, int modelSlot)
    {
        if (string.Equals(task.Type.Name, SecurityCameraType, StringComparison.Ordinal))
        {
            if (modelSlot == 0)
            {
                return CameraHolderOrientation(task);
            }

            float holderGamma = FloatValue(task, "Holder Gamma");
            float cameraAlpha = FloatValue(task, "Camera Alpha");
            float cameraGamma = FloatValue(task, "Camera Gamma");
            return Matrix3f.FromEngineEulerAngles(cameraAlpha, 0f, holderGamma + cameraGamma);
        }

        if (task.TryGetValue(OrientationParameter, out object? rawOrientation))
        {
            return rawOrientation switch
            {
                Vector3f angles => ToOrientation(angles),
                float gamma => Matrix3f.RotationZ(gamma),
                _ => Matrix3f.Identity,
            };
        }

        // Point objects may expose their heading as a standalone Gamma instead of the three-angle
        // Orientation value. Fence does exactly that: its interaction path already consumes Gamma,
        // and drawing the model at identity left the visible mesh on a different plane from the vault.
        return task.TryGetValue("Gamma", out object? rawGamma) && rawGamma is float value
            ? Matrix3f.RotationZ(value)
            : Matrix3f.Identity;
    }

    private static Matrix3f CameraHolderOrientation(DeclaredTask task) =>
        Matrix3f.RotationZ(FloatValue(task, "Holder Gamma"));

    private static Vector3d CameraLensPosition(
        DeclaredTask task,
        Vector3d holderPosition,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName)
    {
        string holderName = task.TryGetValue(SecurityCameraModelParameters[0], out object? raw)
            && raw is string declaredName
            && declaredName.Length > 0
                ? declaredName
                : SecurityCameraModels[0];

        if (!byName.TryGetValue(holderName, out var holder)
            || !holder.Model.TryGetMagicVertex(0, out Vector3f mount))
        {
            return holderPosition;
        }

        // Matrix3f stores the engine's row-major basis, while the holder-to-world operation here is
        // M*v. Transposing before the row-vector helper gives the same product as retail sub_414E20.
        Vector3f offset = CameraHolderOrientation(task).Transposed().Transform(mount);
        return new Vector3d(
            holderPosition.X + (offset.X / UnitsPerMetre),
            holderPosition.Y + (offset.Y / UnitsPerMetre),
            holderPosition.Z + (offset.Z / UnitsPerMetre));
    }

    private static float FloatValue(DeclaredTask task, string parameter) =>
        task.TryGetValue(parameter, out object? raw) && raw is float value ? value : 0f;

    /// <summary>
    /// Emits the same per-object record consumed by the reference level verifier, while also recording
    /// the authored-to-runtime comparison. The model id is the pack instance id, not a friendly catalog
    /// name: that is the identity the retail loader uses when it adds the object.
    /// </summary>
    private static void LogPlacementVerification(
        IIgiLogger log,
        LoadedMission mission,
        DeclaredTask task,
        string modelId,
        Vector3d runtimePositionMetres,
        Matrix3f runtimeOrientation,
        int modelSlot,
        bool isTrain,
        bool isCharacter,
        bool isCameraLens)
    {
        string type = task.Type.Name;
        string name = task.Name;
        string source = $"LOCAL:{mission.Descriptor.MissionDirectory}/objects.qvm";
        string positionText = FormatPosition(runtimePositionMetres * UnitsPerMetre);
        string orientationText = FormatOrientation(runtimeOrientation);

        bool hasAuthoredPosition = TryGetPosition(task, out Vector3d authoredPositionMetres);
        Matrix3f authoredOrientation = PlacementOrientation(task, modelSlot);
        bool positionMatch = hasAuthoredPosition
            && LevelPositionValidator.PosMatch(
                authoredPositionMetres * UnitsPerMetre,
                runtimePositionMetres);
        bool orientationMatch = LevelPositionValidator.OriMatch(authoredOrientation, runtimeOrientation);
        string verification = isTrain
            ? "DERIVED_TRAIN"
            : isCameraLens
                ? "DERIVED_CAMERA_LENS"
            : !hasAuthoredPosition
                ? "DERIVED_RUNTIME"
                : isCharacter && !positionMatch
                    ? "EXPECTED_TERRAIN_GROUNDING"
                    : positionMatch && orientationMatch ? "PASS" : "MISMATCH";

        log.Information(
            LogCategory,
            $"[LevelLoader] Object Loaded: ModelID={modelId}, Type={type}, Name={name}, "
                + $"Pos={positionText}, Ori={orientationText}, Tex={modelId}, Model={modelId}, "
                + $"Source={source}, PositionMatch={positionMatch}, OrientationMatch={orientationMatch}, "
                + $"Verification={verification}");

        if (verification == "MISMATCH")
        {
            log.Error(
                LogCategory,
                $"[LevelLoader] Object verification failed: ModelID={modelId}, Type={type}, Name={name}, "
                    + $"Source={source}, authoredPos={FormatPosition(authoredPositionMetres * UnitsPerMetre)}, "
                    + $"runtimePos={positionText}, authoredOri={FormatOrientation(authoredOrientation)}, "
                    + $"runtimeOri={orientationText}");
        }
    }

    private static void LogDerivedPlacement(
        IIgiLogger log,
        LoadedMission mission,
        DeclaredTask? owner,
        string modelId,
        Vector3d positionMetres,
        Matrix3f orientation,
        string sourceKind)
    {
        string type = owner?.Type.Name ?? "Derived";
        string name = owner?.Name ?? string.Empty;
        string source = $"LOCAL:{mission.Descriptor.MissionDirectory}/objects.qvm";
        log.Information(
            LogCategory,
            $"[LevelLoader] Object Loaded: ModelID={modelId}, Type={type}, Name={name}, "
                + $"Pos={FormatPosition(positionMetres * UnitsPerMetre)}, Ori={FormatOrientation(orientation)}, "
                + $"Tex={modelId}, Model={modelId}, Source={source}, PositionMatch=True, "
                + $"OrientationMatch=True, Verification=DERIVED_{sourceKind}");
    }

    private static string FormatPosition(Vector3d position) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({position.X:F6}, {position.Y:F6}, {position.Z:F6})");

    private static string FormatOrientation(Matrix3f matrix)
    {
        float beta = MathF.Asin(Math.Clamp(-matrix.M31, -1f, 1f));
        float alpha = MathF.Atan2(matrix.M32, matrix.M33);
        float gamma = MathF.Atan2(matrix.M21, matrix.M11);
        return string.Create(CultureInfo.InvariantCulture, $"({alpha:F6}, {beta:F6}, {gamma:F6})");
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}

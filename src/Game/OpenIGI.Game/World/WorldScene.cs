using System.Collections.ObjectModel;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

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
    /// <summary>The log category the scene walk reports under.</summary>
    internal const string LogCategory = "World";

    private double? _groundHeight;

    /// <summary>Engine units per metre, the original's <c>WORLD_METER</c> script constant.</summary>
    /// <remarks>
    /// Registered as a double at <c>0x539558</c>. Model vertices use the same units as world positions:
    /// the level-1 water tower measures 95005 units tall, which is 23 metres.
    /// </remarks>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Converts a position or an offset from engine units into metres.</summary>
    /// <param name="units">The value in engine units.</param>
    /// <returns>The same value in metres.</returns>
    /// <remarks>
    /// Beside the constant that defines it, because every title's scene walk needs it and the arithmetic
    /// is the kind that goes wrong by being retyped: a walk that forgets it renders a level four thousand
    /// times too large, which looks like an empty world rather than like a scale error.
    /// <para>
    /// Both games agree on the divisor. <c>Game_Open</c> registers <c>WORLD_METER</c> as a double
    /// (<c>IGI.exe 0x539558</c>, <c>igi2.exe 0x640B80</c>) and both hold 4096.
    /// </para>
    /// </remarks>
    public static Vector3d ToMetres(Vector3d units) =>
        new(units.X / UnitsPerMetre, units.Y / UnitsPerMetre, units.Z / UnitsPerMetre);

    /// <summary>Converts a position or an offset from metres into engine units.</summary>
    /// <param name="metres">The value in metres.</param>
    /// <returns>The same value in engine units.</returns>
    /// <remarks>
    /// The inverse of <see cref="ToMetres"/>, and it exists because both games' terrain needs it: the
    /// level-of-detail thresholds are authored in engine units, so the eye is converted into that space
    /// rather than the height field out of it.
    /// </remarks>
    public static Vector3d ToUnits(Vector3d metres) =>
        new(metres.X * UnitsPerMetre, metres.Y * UnitsPerMetre, metres.Z * UnitsPerMetre);

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

    /// <summary>A switch's five models, in the order its update picks them (<c>0x4450F0</c>, <c>+368 + 16 * slot</c>).</summary>
    public static readonly string[] SwitchModelParameters = ["On model", "On pressed model", "Off model", "Off pressed model", "Destroyed model"];

    /// <summary>The alarm light task type: a light that turns with the alarm.</summary>
    public const string AlarmLightType = "AlarmLight";

    /// <summary>The models an alarm light wears, on and off, in the order <see cref="WorldPlacement.Variants"/> keeps them.</summary>
    public static readonly string[] AlarmLightModelParameters = ["On model", "Off model"];

    /// <summary>The parameter an alarm light names its shot-out model by, as the level data spells it.</summary>
    public const string AlarmLightDeadModelParameter = "Destroyde model";

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

        // An alarm light wears its "On model" from the moment it is made (0x43A450) -- its off model only once it has been on
        // and gone off, which the alarm registry picks (AlarmLightModelParameters).
        if (string.Equals(task.Type.Name, AlarmLightType, StringComparison.Ordinal)
            && task.TryGetValue(SwitchModelParameter, out object? rawLight)
            && rawLight is string lightModel
            && lightModel.Length > 0)
        {
            yield return lightModel;
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

    /// <summary>Builds a scene from placements a title has already resolved.</summary>
    /// <param name="mission">The mission the placements belong to, which is what locates the lightmap archive.</param>
    /// <param name="placements">Everything to draw, in the order the level placed it.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <see cref="Build"/> walks a task tree and knows a great deal about which types Project I.G.I.
    /// places and what their parameters are called — down to naming the security-camera models. That
    /// walk is not shared, and neither is it needed: a title that has already worked out what it places
    /// has nothing left to discover, and only needs somewhere to put the answer.
    /// </para>
    /// <para>
    /// So this is the seam that lets one renderer draw both games. The scene, the placement and the
    /// renderer are shared; how a level is read into placements is the title's own business.
    /// </para>
    /// </remarks>
    public static WorldScene FromPlacements(LoadedMission mission, IReadOnlyList<WorldPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(placements);

        return new WorldScene(mission, placements, [], unresolved: 0, unplaced: 0, attached: 0);
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
    /// head taken from the ladder model's magic vertices 2 and 3.
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
    /// <param name="vehiclePhysics">Optional physics definitions used to bind a CAR's authored wreck model.</param>
    /// <param name="destroyedModelArchiveName">
    /// The archive's spelling of a destroyed model a mission names otherwise, by mission and authored name, or
    /// <see langword="null"/> for none: a replacement the archive does not hold under its authored name is then left out.
    /// </param>
    /// <returns>The scene.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static WorldScene Build(
        LoadedMission mission,
        GameTask levelRoot,
        MagicObjectRegistry? magic = null,
        IOpenIgiLogger? logger = null,
        VehiclePhysicsRegistry? vehiclePhysics = null,
        Func<int, string, string?>? destroyedModelArchiveName = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
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
        TrainPlacementBuilder trains = new(byName);
        int unresolved = 0;
        int unplaced = 0;
        int attached = 0;

        foreach (GameTask task in levelRoot.Descendants())
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
                trainPlacement = trains.Place(declared, placedNames);
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
                    Vector3d[] coupler = new Vector3d[1];
                    if (railroad is null || !railroad.TryWalk(railroad.InitialPosition, [], coupler))
                    {
                        unplaced++;
                        continue;
                    }

                    position = coupler[0] / UnitsPerMetre;
                }

                Matrix3f orientation = trainPlacement?.Orientation ?? WorldPlacementOrientation.PlacementOrientation(declared, modelSlot);

                // SCameraLens is not committed at the holder task's origin. Retail asks the holder
                // model for magic vertex 0, rotates that mount through the holder's own orientation,
                // and adds it to Holder Position (0x4420D0). A live Mission 1 probe verified all five
                // resulting positions to sub-unit precision.
                if (string.Equals(declared.Type.Name, SecurityCameraType, StringComparison.Ordinal)
                    && modelSlot == 1)
                {
                    position = WorldPlacementOrientation.CameraLensPosition(declared, position, byName);
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

                WorldSceneDiagnostics.LogPlacementVerification(
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
                    && TryResolveDestroyedBinding(destroyedName, placement, mission, byName, destroyedModelArchiveName, out WorldModelBinding? destroyed))
                {
                    placement = placement with
                    {
                        DestroyedReplacement = destroyed,
                    };
                }

                // A switch wears one of five models by its state (0x4450F0): on, on and pressed, off, off and pressed, and
                // destroyed, each its own parameter.
                if (string.Equals(declared.Type.Name, SwitchType, StringComparison.Ordinal))
                {
                    WorldModelBinding?[] variants = new WorldModelBinding?[SwitchModelParameters.Length];
                    bool any = false;
                    for (int worn = 0; worn < variants.Length; worn++)
                    {
                        if (declared.TryGetValue(SwitchModelParameters[worn], out object? rawVariant)
                            && rawVariant is string variantName
                            && variantName.Length > 0
                            && TryResolveDestroyedBinding(variantName, placement, mission, byName, destroyedModelArchiveName, out WorldModelBinding? variant))
                        {
                            variants[worn] = variant;
                            any = true;
                        }
                    }

                    if (any)
                    {
                        placement = placement with { Variants = variants };
                    }
                }

                // An alarm light wears its on model or its off model (0x43A270), and its dead model once shot out -- under the
                // level data's own spelling, "Destroyde model".
                if (string.Equals(declared.Type.Name, AlarmLightType, StringComparison.Ordinal))
                {
                    WorldModelBinding?[] worn = new WorldModelBinding?[AlarmLightModelParameters.Length];
                    for (int i = 0; i < worn.Length; i++)
                    {
                        if (declared.TryGetValue(AlarmLightModelParameters[i], out object? rawWorn)
                            && rawWorn is string wornName
                            && wornName.Length > 0
                            && TryResolveDestroyedBinding(wornName, placement, mission, byName, destroyedModelArchiveName, out WorldModelBinding? binding))
                        {
                            worn[i] = binding;
                        }
                    }

                    placement = placement with { Variants = worn };
                    if (declared.TryGetValue(AlarmLightDeadModelParameter, out object? rawDead)
                        && rawDead is string deadName
                        && deadName.Length > 0
                        && TryResolveDestroyedBinding(deadName, placement, mission, byName, destroyedModelArchiveName, out WorldModelBinding? dead))
                    {
                        placement = placement with { DestroyedReplacement = dead };
                    }
                }

                // ExplodeObject carries the same authored replacement convention as the camera head,
                // but under the generic "Destroyed model" parameter. Keep the live placement in the
                // scene so collision and lightmap identity remain stable; the renderer selects this
                // binding only after the exact task has reached retail's destroyed state.
                if (placement.DestroyedReplacement is null
                    && declared.TryGetValue("Destroyed model", out object? rawGenericDestroyed)
                    && rawGenericDestroyed is string genericDestroyedName
                    && genericDestroyedName.Length > 0
                    && TryResolveDestroyedBinding(genericDestroyedName, placement, mission, byName, destroyedModelArchiveName, out WorldModelBinding? genericDestroyed))
                {
                    placement = placement with
                    {
                        DestroyedReplacement = genericDestroyed,
                    };
                }

                // Cars do not declare a "Destroyed model" level parameter.  Their own retail
                // physicsobj definition supplies it as the final DefinePhysicsObjType value (the
                // Level 6 APC is 601_01_1 -> 601_08_1).  Preserve that authored binding on the
                // placement so the shared destruction path can select it after a launcher hit.
                if (placement.DestroyedReplacement is null
                    && string.Equals(task.Type.Name, "Car", StringComparison.Ordinal)
                    && vehiclePhysics?.TryGetCar(placement.Instance.Name, out CarPhysicsDefinition car) == true
                    && car.DestroyedModel.Length > 0
                    && TryResolveDestroyedBinding(car.DestroyedModel, placement, mission, byName, destroyedModelArchiveName, out WorldModelBinding? vehicleDestroyed))
                {
                    placement = placement with
                    {
                        DestroyedReplacement = vehicleDestroyed,
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
                    attached += WorldAttachmentWalker.AddAttached(
                        mission, log, bake.Root, position, orientation, orientation, bake.Set ?? string.Empty,
                        task.Name, task, byName, magic, placements, ladders);
                }
                else if (magic is not null
                    && WorldObject.TryBuild(name, mission.TryGetModel, magic, out WorldObject? loose)
                    && loose is not null)
                {
                    attached += WorldAttachmentWalker.AddAttached(
                        mission, log, loose, position, orientation, orientation, string.Empty, task.Name, task,
                        byName, magic, placements, ladders);
                }
            }
        }

        int segments = TrainPlacementBuilder.AddSplineSegments(mission, levelRoot, byName, placements, log);

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

    /// <summary>Resolves an authored destroyed model, including archive-only QVM replacement meshes.</summary>
    private static bool TryResolveDestroyedBinding(
        string modelName,
        WorldPlacement intact,
        LoadedMission mission,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName,
        Func<int, string, string?>? archiveName,
        out WorldModelBinding? binding)
    {
        if (byName.TryGetValue(modelName, out var placed))
        {
            binding = new WorldModelBinding(placed.Instance, placed.Pack, placed.Model);
            return true;
        }

        // Some QVM-only replacements (Level 6 Terminal_94's 252_02_1) have geometry in the
        // archive but no INST record. Retain the intact pack and material-slot map while replacing
        // only its name and geometry: an archive fallback may come from common, whose texture-table
        // indices cannot safely be applied to a level instance.
        foreach (LoadedPack pack in new[] { mission.LevelPack, mission.CommonPack })
        {
            if (TryGetDestroyedArchiveModel(mission.Descriptor.Id, pack, modelName, archiveName, out MefModel? model) && model is not null)
            {
                MtpInstance instance = intact.Instance with { Name = modelName };
                binding = new WorldModelBinding(instance, intact.Pack, model);
                return true;
            }
        }

        binding = null;
        return false;
    }

    /// <summary>Resolves an archive-only destroyed mesh, including the retail terminal name inversion.</summary>
    private static bool TryGetDestroyedArchiveModel(
        int missionId,
        LoadedPack pack,
        string modelName,
        Func<int, string, string?>? archiveName,
        out MefModel? model)
    {
        if (pack.TryGetArchiveModel(modelName, out model))
        {
            return true;
        }

        // A known data pair only (the title's quirks: Level 6's Terminal_94 names 252_02_1, the archive stores 252_01_2). The
        // task keeps its authored name; no broad rewrite is applied to other missing replacements.
        return archiveName?.Invoke(missionId, modelName) is { } spelling
            && pack.TryGetArchiveModel(spelling, out model);
    }

    /// <summary>Gets the coupling distance the retail Train solver reads from a car model.</summary>
    /// <param name="model">The car model.</param>
    /// <returns>The distance between its two couplers, or nought when it has no coupler vertices.</returns>
    /// <remarks>
    /// Train initialization (<c>0x42A150</c>) asks the model for magic vertices 0 and 1 through
    /// <c>0x414ED0</c>, stores <c>abs(v0.x - v1.x)</c> at Train+1868, and passes one such value per car
    /// to the railroad evaluator. Mesh bounds include overhangs and are not the consist spacing.
    /// </remarks>
    public static float TrainCouplingDistance(MefModel model) => TrainPlacementBuilder.TrainCouplingDistance(model);

    /// <summary>Gets the rolling radius of a wheel model, in world units.</summary>
    /// <param name="model">The wheel model.</param>
    /// <returns>Half its largest extent, or nought when it has no geometry.</returns>
    /// <remarks>
    /// A wheel is a disc: its two largest extents are both the diameter and the smallest is the
    /// tread width, so half the largest is the radius. Retail keeps a radius on each wheel task's
    /// own record, which the port has no reader for; measuring the drawn model gets within a tenth
    /// of it. The Mission 1 truck's wheel measures 0.46 m here, against the 0.50 m its spin rate on
    /// a straight implies in a capture of the running game -- where the suspension travel that
    /// stood in for it before was 0.3 m, forty per cent out.
    /// </remarks>
    public static float WheelRadius(MefModel model) => TrainPlacementBuilder.WheelRadius(model);

    /// <summary>Gets the retail model-origin fraction between a Train car's two couplers.</summary>
    /// <param name="model">The car model.</param>
    /// <returns>The model origin's fraction of the way from the front coupler, or nought.</returns>
    /// <remarks>
    /// Train initialization (<c>0x42A150</c>) stores <c>abs(v0.x) / abs(v0.x - v1.x)</c> at
    /// Train+1864. The consist updater later interpolates the two railroad samples by this fraction;
    /// omitting it places the model origin on its front coupler instead of inside the car.
    /// </remarks>
    public static float TrainCouplingPivotFraction(MefModel model) => TrainPlacementBuilder.TrainCouplingPivotFraction(model);

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
        foreach (GameTask owner in levelRoot.Descendants())
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

    /// <summary>Walks a task and everything under it, the task itself first.</summary>
    private static IEnumerable<GameTask> Subtree(GameTask task)
    {
        yield return task;
        foreach (GameTask descendant in task.Descendants())
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
                position = ToMetres(p);
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
    private static bool IsCharacterPlacement(DeclaredTask declared) => IsCharacterTypeName(declared.Type.Name);

    /// <summary>Returns whether a task type name is one of the human character types retail grounds onto the terrain.</summary>
    /// <param name="typeName">The task's type name.</param>
    /// <remarks>The AI soldiers, male and female, and the player body; the name must match exactly.</remarks>
    internal static bool IsCharacterTypeName(string? typeName) =>
        string.Equals(typeName, AiSoldierType, StringComparison.Ordinal)
        || string.Equals(typeName, AiSoldierFemaleType, StringComparison.Ordinal)
        || string.Equals(typeName, HumanPlayerType, StringComparison.Ordinal);

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
}

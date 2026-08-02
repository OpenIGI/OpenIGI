using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Draws a level's placed soldiers — each the model, skeleton and stand animation its own task declares.</summary>
/// <remarks>
/// <para>
/// The spawn is <c>0x456E50</c> + <c>0x45F890</c>: the bone object
/// on the declared <c>Bone Heirachy</c> skeleton, the declared <c>Stand Animation</c> (or 2) on bone 0,
/// the ordinary <c>Model</c> parameter resolved by name, and the facing a pure <c>Z(Gamma)</c>. The body
/// draws through <c>Human</c>'s ordinary render handler under the <b>world</b> projection —
/// <c>0x4D9620</c> leaves <c>+212</c> at 1, so <c>0x49F700</c>'s view-model block never rebuilds for it.
/// </para>
/// <para>
/// Soldiers sharing a <c>(model, skeleton, stand animation)</c> triple stand in the same pose, so the
/// CPU skin runs once per distinct triple and each soldier draws that mesh at its own position and
/// facing. The facing can live in the per-soldier model matrix because the pose is skinned with an
/// identity root — the root rotation only ever left-multiplies every bone (see
/// <see cref="ViewModelRenderer"/>).
/// </para>
/// </remarks>
public sealed class SoldierRenderer : IDisposable
{
    private const string LogCategory = "Soldier";

    /// <summary>How far above the declared position the ground probe starts — 0.5 m.</summary>
    private const double ProbeAboveInUnits = 2048.0;

    /// <summary>How far below the declared position the probe reaches — 2 m, the sliding step-down budget.</summary>
    private const double ProbeBelowInUnits = 8192.0;

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Group> _groups = [];
    private readonly List<HeldWeapon> _weapons = [];
    private readonly Dictionary<SoldierView, HumanHealth> _health = [];

    // Each soldier's combat row, resolved from its own AIType at the difficulty in force. It is what
    // gives a guard its maximum health -- sub_406F70 writes the row's "Damage scale" over Human+600,
    // so a guard is worth 0.3 of a person on easy and 0.8 on hard, not the constructor's 1.0.
    private readonly Dictionary<SoldierView, AiCombatParameters> _combat = [];

    // A dead soldier cannot stay in a shared group: the group is skinned from ONE representative's pose, so
    // a corpse there would keep standing in the group's idle animation. Each gets its own single-member
    // group instead, ticked and re-skinned from its own rig, which is what lets it fall.
    private readonly Dictionary<SoldierView, AiSoldier> _brains = [];
    private TerrainData? _terrain;
    private WorldRayCast? _cast;
    private SoldierRound? _onRound;
    private string _lastCombatReport = string.Empty;
    private readonly HashSet<SoldierView> _combatClipMissing = [];

    // The alarm chain: the level-wide activation claims (dword_5BDA28), the tasks that have actually
    // been pressed, and one AlarmControl state (0x4408A0's update) per control id the soldiers name.
    // The control turns on when any soldier's named trigger has been pressed -- a recorded stand-in
    // for the control's two authored condition expressions, which the port does not evaluate yet.
    private readonly AiActivationClaims _activationClaims = new();
    private readonly HashSet<int> _activatedTasks = [];
    private readonly Dictionary<int, AiAlarmControlState> _alarmStates = [];
    private GameTask? _levelRoot;

    // The level's doors: ticked here (their update runs on the task pass, before the AIs), queried by
    // each walker's door hop, and switched out of the collision index while they stand open.
    private DoorRegistry? _doors;
    private ObjectCollisionIndex? _solids;

    /// <summary>Gives the renderer the level's doors and the index their collision is switched in.</summary>
    /// <param name="doors">The door registry, or <see langword="null"/> for a level with none.</param>
    /// <param name="solids">The placed-object collision index.</param>
    public void SetDoors(DoorRegistry? doors, ObjectCollisionIndex? solids)
    {
        _doors = doors;
        _solids = solids;
    }
    private readonly HashSet<string> _reportedChannels = new(StringComparer.Ordinal);
    private int _deathsSoFar;
    private int _walkAnimation = -1;
    private int _runAnimation = -1;
    private int _tick;
    private int _travelSamples;
    private double _travelTotal;
    private double _travelMax;
    private bool _disposed;

    // The player's own third-person body -- Jones, the rig the first-person view never shows. It is NOT a
    // guard: it has no AI, is not in _groups, and the soldier tick and draw loops never touch it. It loads
    // with the level (LoadPlayerBody), stands invisibly until the player dies, then poses the death clip
    // (KillPlayerBody) and is drawn (DrawPlayerBody) once the death camera has pulled back off the eye.
    // Reuses the same BuildGroup / re-skin / draw machinery a soldier's body does. See HumanPlayerBody.
    private SoldierView? _playerBodyView;
    private Group? _playerBodyGroup;
    private Member? _playerBodyMember;
    private bool _playerBodyActive;
    private bool _playerBodyClimbing;
    private float _playerBodyFacing;
    private AiSoldier.GroundQuery? _playerBodyGround;

    // The player body's own tick-state pair and frame bookkeeping -- the same smoothing a soldier's
    // Member carries, kept as fields because there is only ever one of it.
    private Vector3d _playerBodyTickWhere;
    private Vector3d _playerBodyPrevTickWhere;
    private bool _playerBodyHasTickState;
    private int _playerBodyCaptureTick = -1;

    // The frame alpha BeginFrame resolved, kept for DrawPlayerBody which runs once per frame.
    private float _frameAlpha = 1f;
    private bool _frameSmooth;

    /// <summary>Initializes a new instance of the <see cref="SoldierRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SoldierRenderer(IGraphicsDevice device, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the number of soldiers that resolved and will draw.</summary>
    public int Count { get; private set; }

    /// <summary>Builds the drawable soldiers from a loaded level.</summary>
    /// <param name="mission">The mission whose packs hold the models and animation sets.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="terrain">The level's terrain, for standing soldiers on the ground it declares.</param>
    /// <param name="cast">The placed-object ray cast, for soldiers standing on floors and platforms.</param>
    /// <returns>The number of soldiers that resolved.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public int Load(LoadedMission mission, GameTask levelRoot, TerrainData? terrain = null, WorldRayCast? cast = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Release();
        _terrain = terrain;
        _cast = cast;

        Dictionary<(string Model, int Skeleton, int Stand), Group> groups = [];
        foreach (HumanSoldierTask soldier in Walk(levelRoot))
        {
            if (!mission.TryGetAnimationSet(soldier.BoneHierarchy, out BoneAnimationSet? set) || set is null)
            {
                _logger.Warning(
                    LogCategory,
                    $"'{soldier.Name}' declares skeleton {soldier.BoneHierarchy}, which no pack loaded a set for.");
                continue;
            }

            SoldierView view = new(soldier, set);
            if (soldier.StandAnimation >= 0 && !view.DeclaredStandAnimationFound)
            {
                // The original's own wording (0x456E50).
                _logger.Warning(LogCategory, $"Animation #{soldier.StandAnimation} not found");
            }

            (string, int, int) key = (soldier.Model, soldier.BoneHierarchy, view.StandAnimation);
            if (!groups.TryGetValue(key, out Group? group))
            {
                group = BuildGroup(mission, soldier.Model, view);
                if (group is null)
                {
                    continue;
                }

                groups[key] = group;
                _groups.Add(group);
            }

            // Its own mesh, so it can hold its own pose (see Member) -- animated, so each tick's
            // re-skin replaces the vertices in place and the pose replaced becomes the previous-pose
            // stream the motion vectors and the between-tick blending read.
            MeshHandle own;
            try
            {
                own = _device.CreateAnimatedMesh(group.Geometry.Vertices, group.Geometry.Indices);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            group.Soldiers.Add(new Member(view, Stand(soldier, group, terrain, cast), own));
            Count++;
        }

        _logger.Information(
            LogCategory,
            $"{Count} soldier(s) over {_groups.Count} distinct (model, skeleton, animation) triple(s).");
        return Count;
    }

    /// <summary>How high above a soldier's feet its broad-phase sphere is centred, in world units.</summary>
    private const double PickChestUnits = 3686.0;

    /// <summary>The broad-phase cull radius, in world units — generous (a soldier plus its outstretched arms).</summary>
    private const double PickCullRadiusUnits = 8192.0;

    /// <summary>
    /// Names the soldier under a crosshair ray by tracing its <b>skinned collision mesh</b> — the same
    /// geometry a round is scored against, so the readout hits exactly where a shot would.
    /// </summary>
    /// <param name="originUnits">The eye, in world units.</param>
    /// <param name="direction">The look direction; need not be unit length.</param>
    /// <param name="reachUnits">How far to look, in world units.</param>
    /// <param name="name">Receives the struck soldier's model instance name.</param>
    /// <param name="distanceUnits">Receives how far along the ray it sits.</param>
    /// <param name="feetUnits">Receives the struck soldier's feet position, in world units.</param>
    /// <param name="zone">Receives the hit zone the round would score, or <c>-1</c> when none.</param>
    /// <returns><see langword="false"/> when the crosshair is not on a soldier's body.</returns>
    public bool TryPick(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        out string name,
        out double distanceUnits,
        out Vector3d feetUnits,
        out int zone)
    {
        name = string.Empty;
        distanceUnits = double.MaxValue;
        feetUnits = default;
        zone = -1;

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d dir = new(direction.X / length, direction.Y / length, direction.Z / length);
        Vector3d end = new(
            originUnits.X + (dir.X * reachUnits),
            originUnits.Y + (dir.Y * reachUnits),
            originUnits.Z + (dir.Z * reachUnits));
        bool found = false;

        foreach (Group group in _groups)
        {
            if (group.CollisionMesh is not { } mesh)
            {
                continue;
            }

            foreach (Member member in group.Soldiers)
            {
                SoldierView view = member.View;
                double snappedZ = SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
                Vector3d feet = new(view.Position.X, view.Position.Y, snappedZ);

                // Broad phase: a generous body sphere, so a ray nowhere near a soldier never touches its mesh.
                Vector3d centre = new(feet.X, feet.Y, feet.Z + PickChestUnits);
                if (!RaySphere(originUnits, dir, centre, PickCullRadiusUnits, reachUnits, out _))
                {
                    continue;
                }

                Matrix3f m = view.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                if (HumanHitTest.TryCastMesh(
                        originUnits, end, feet, facing, mesh, view.Pose(), view.RootSample.Z, out HumanHit h)
                    && h.Distance < distanceUnits)
                {
                    distanceUnits = h.Distance;
                    name = group.Instance?.Name ?? "soldier";
                    feetUnits = feet;
                    zone = h.Zone;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Names the detection state of the soldier under a ray, toward a target eye — the overlay's "why won't this
    /// guard see me" readout. Picks the nearest soldier the ray's body sphere touches and asks its brain which
    /// gate (cone, line of sight) is passing.
    /// </summary>
    /// <param name="originUnits">The ray origin in engine units.</param>
    /// <param name="direction">The ray direction; need not be normalised.</param>
    /// <param name="reachUnits">How far to look, in units.</param>
    /// <param name="enemyEyeUnits">The eye the guards actually test against — the player's, in units.</param>
    /// <param name="info">The formatted detection line, empty when no soldier is under the ray.</param>
    /// <returns><see langword="true"/> when a soldier was under the ray and had a brain to ask.</returns>
    public bool TryDetectionInfo(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        Vector3d enemyEyeUnits,
        out string info)
    {
        info = string.Empty;

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d dir = new(direction.X / length, direction.Y / length, direction.Z / length);
        AiSoldier? nearest = null;
        double nearestT = double.MaxValue;

        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            double snappedZ = SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
            Vector3d centre = new(view.Position.X, view.Position.Y, snappedZ + PickChestUnits);
            if (RaySphere(originUnits, dir, centre, PickCullRadiusUnits, reachUnits, out double t) && t < nearestT)
            {
                nearestT = t;
                nearest = brain;
            }
        }

        if (nearest is null)
        {
            return false;
        }

        (double distU, bool inCone, bool los, bool alerted, bool visible) = nearest.ProbeDetection(enemyEyeUnits);
        string cone = inCone ? "IN" : "out";
        string clear = los ? "clear" : "BLOCKED";
        string alert = alerted ? "yes" : "no";
        string seen = visible ? "YES" : "no";
        info = FormattableString.Invariant(
            $"detect: dist={distU / WorldScene.UnitsPerMetre:F1}m cone={cone} los={clear} alert={alert} seen={seen} {nearest.Activity}");
        return true;
    }

    // Nearest positive entry of a ray (unit direction) into a sphere, within reach. Distance is along the ray.
    private static bool RaySphere(Vector3d origin, Vector3d dir, Vector3d centre, double radius, double reach, out double t)
    {
        t = 0.0;
        double ox = origin.X - centre.X;
        double oy = origin.Y - centre.Y;
        double oz = origin.Z - centre.Z;
        double b = (ox * dir.X) + (oy * dir.Y) + (oz * dir.Z);
        double c = (ox * ox) + (oy * oy) + (oz * oz) - (radius * radius);
        double disc = (b * b) - c;
        if (disc < 0.0)
        {
            return false;
        }

        double root = Math.Sqrt(disc);
        double near = -b - root;
        double far = -b + root;
        t = near >= 0.0 ? near : far;
        return t >= 0.0 && t <= reach;
    }

    /// <summary>Puts a soldier's feet on whatever is under its declared position.</summary>
    /// <remarks>
    /// <para>
    /// In the original the level's declared <c>Position</c> is only where a soldier <em>starts</em>: its
    /// update runs the same movement integrator as the player's, whose ground pass (<c>0x462390</c>)
    /// snaps the body onto the surface below within the first ticks. The port does not tick soldiers
    /// yet, so a body drawn with its skeleton root at the declared height stands buried to the waist
    /// wherever the position was authored at floor level.
    /// </para>
    /// <para>
    /// This reproduces the settled state, not the mechanism: probe down from half a metre above the
    /// declared position — placed objects first, terrain as the fallback, highest surface wins — and
    /// stand the <b>skin's lowest vertex</b> on it. The original's own foot reference is
    /// <c>0x48A340</c>'s bone-0-plus-<c>Human+1300</c> offset, and the <c>+1300</c> term is still
    /// unread (<c>player.md</c> §14.9), so the mesh's own extent is the honest stand-in. A recorded
    /// divergence, retired with the soldier update.
    /// </para>
    /// </remarks>
    private static Vector3d Stand(HumanSoldierTask soldier, Group group, TerrainData? terrain, WorldRayCast? cast)
    {
        Vector3d declared = soldier.Position;
        double probeTop = declared.Z + ProbeAboveInUnits;
        double groundZ = declared.Z;

        if (cast is not null
            && cast(
                new Vector3d(declared.X, declared.Y, probeTop),
                new Vector3d(declared.X, declared.Y, declared.Z - ProbeBelowInUnits),
                out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
        }

        if (terrain is not null
            && TerrainHeightQuery.TryGetGround(terrain, declared.X, declared.Y, probeTop, out TerrainGround ground)
            && ground.Height >= declared.Z - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = Math.Max(groundZ, ground.Height);
        }

        return new Vector3d(
            declared.X / WorldScene.UnitsPerMetre,
            declared.Y / WorldScene.UnitsPerMetre,
            (groundZ / WorldScene.UnitsPerMetre) - group.LowestVertexZ);
    }

    /// <summary>The collision surface under a point, in world units — placed objects first, terrain second.</summary>
    /// <param name="x">The point's X, in world units.</param>
    /// <param name="y">Its Y.</param>
    /// <param name="declaredZ">The feet the query starts from; returned unchanged when nothing is under them.</param>
    /// <returns>The highest floor within the probe, or <paramref name="declaredZ"/> if none.</returns>
    /// <remarks>
    /// The same query <see cref="Stand"/> runs, without the mesh lift — for grounding a live soldier's feet
    /// each frame. It only reaches half a metre up (<see cref="ProbeAboveInUnits"/>), so it pulls a soldier
    /// authored just below its floor up onto it, but never lifts one standing correctly.
    /// </remarks>
    private double SnapToGround(double x, double y, double declaredZ)
    {
        double groundZ = declaredZ;
        double probeTop = declaredZ + ProbeAboveInUnits;

        if (_cast is not null
            && _cast(new Vector3d(x, y, probeTop), new Vector3d(x, y, declaredZ - ProbeBelowInUnits), out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
        }

        if (_terrain is not null
            && TerrainHeightQuery.TryGetGround(_terrain, x, y, probeTop, out TerrainGround ground)
            && ground.Height >= declaredZ - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = Math.Max(groundZ, ground.Height);
        }

        return groundZ;
    }

    /// <summary>Gives every soldier an AI that patrols the level's nav graph and reacts to the player.</summary>
    /// <param name="graphs">
    /// The level's nav graphs, keyed by the id of the <c>AIGraph</c> task that loads each. A soldier is
    /// bound to the one its own <c>HumanAI</c>'s <c>Graph ID</c> names; one that names a graph the level
    /// does not ship gets no brain rather than being routed over somebody else's node ids.
    /// </param>
    /// <param name="sight">The world cast used for line of sight, or <see langword="null"/> to see through walls.</param>
    /// <param name="scripts">Where the archetype behaviour script is read from, or <see langword="null"/> for none.</param>
    /// <param name="natives">The registry the behaviour script resolves its AI natives against.</param>
    /// <param name="levelRoot">
    /// The level's task tree, which the authored patrol paths are collected from. Without it a soldier has
    /// no path to walk and stands where the level put it.
    /// </param>
    /// <param name="config">
    /// The combat table out of <c>common/ai/settings.qsc</c>, or <see langword="null"/> to leave every
    /// soldier on the constructor's defaults.
    /// </param>
    /// <param name="difficulty">Which of the table's three columns to read.</param>
    /// <returns>How many soldiers got a brain.</returns>
    /// <remarks>
    /// The phase is the soldier's index modulo four, which is what staggers the level's detection sweeps
    /// across the four ticks the original spreads them over (<c>ai.md</c> §5.1).
    /// </remarks>
    public int Animate(
        IReadOnlyDictionary<int, GraphFile> graphs,
        WorldRayCast? sight = null,
        ResourceRegistry? scripts = null,
        NativeRegistry? natives = null,
        GameTask? levelRoot = null,
        HumanAiConfigRegistry? config = null,
        AiDifficulty difficulty = AiDifficulty.Normal)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _brains.Clear();
        _combat.Clear();
        _health.Clear();
        _activatedTasks.Clear();
        _alarmStates.Clear();
        _levelRoot = levelRoot;
        if (graphs.Count == 0)
        {
            // No graphs means no patrolling -- but a brain does not need a node to see or to shoot, so
            // the soldiers are still armed and still fight from where they stand.
            _logger.Warning(LogCategory, "The level ships no nav graph, so no soldier can patrol.");
        }

        // The walk clip is the locomotion table's forward-travelling state (state 1 travels +Y), read from
        // 0x538870 -- not a chosen id.
        _walkAnimation = HumanLocomotionStates.For(1).AnimationId;

        // The run clip is the soldier's own RunState (0x53ED10), measured off "Combat - Follow": a chasing
        // guard runs in state 17 = animation 48 at ~4.4 m/s, not the 1.28 m/s walk. Its own root motion is
        // what makes the chase move at run speed instead of strolling.
        _runAnimation = HumanSoldierStates.AnimationOf(HumanSoldierStates.RunState);

        // The level's authored patrol paths, keyed by the PatrolPath task's own id -- what AIAction_Patrol's
        // first argument names (ai.md 7.1).
        AiPatrolPathRegistry? paths = levelRoot is null ? null : AiPatrolPathRegistry.Build(levelRoot);
        if (paths is not null)
        {
            _logger.Information(LogCategory, $"{paths.Count} authored patrol path(s) in this level.");
        }

        AiScriptHost? host = scripts is not null && natives is not null ? new AiScriptHost(natives, _logger) : null;
        Dictionary<string, QvmProgram?> scriptCache = new(StringComparer.Ordinal);

        int index = 0;
        int patrolling = 0;
        int perTask = 0;
        int unroutable = 0;
        Dictionary<int, int> graphUse = [];
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView view = member.View;

                // Each soldier walks the graph ITS OWN HumanAI names, not the level's biggest one. A
                // soldier whose graph the level does not ship still gets a BRAIN -- nothing in detection
                // or the stand-and-fight arm needs a node, so leaving it brainless made an inert prop
                // that neither saw nor fought where retail's would still shoot back. It just cannot
                // patrol or reposition, which is what the graph is actually for.
                HumanAiTask? brainTask = FindMind(view);
                int graphId = brainTask?.GraphId ?? 0;
                if (!graphs.TryGetValue(graphId, out GraphFile? graph) || graph.RouteTable is null)
                {
                    graph = null;
                    if (brainTask is not null)
                    {
                        unroutable++;
                    }
                }

                AiSoldier brain = new(graph, view.Position, GammaOf(view), index & 3, index);
                brain.SetPatrolPaths(paths);
                brain.SetGroundQuery(MakeGroundQuery());
                if (_doors is { } levelDoors)
                {
                    brain.SetDoorQuery(levelDoors.Nearest);
                }
                if (levelRoot is not null)
                {
                    GameTask taskRoot = levelRoot;
                    brain.SetActivateSupport(
                        _activationClaims,
                        (int id, out Vector3d position) =>
                        {
                            position = TaskPositionOf(taskRoot, id);
                            return position != default;
                        },
                        id => _activatedTasks.Add(id));
                }
                if (graph is not null)
                {
                    graphUse[graphId] = graphUse.GetValueOrDefault(graphId) + 1;
                }
                if (sight is not null)
                {
                    // 0x489B20: the target is seen when the cast is clear, OR when whatever blocked it sits at
                    // or past 0.79 of the way there - so clipping a corner near the target does not hide it.
                    brain.SetLineOfSight((from, to) =>
                    {
                        if (!sight(from, to, out WorldRayHit blocked))
                        {
                            return true;
                        }

                        double total = Length(from, to);
                        return total <= 0.0
                            || Length(from, blocked.Position) / total >= AiSoldier.SightObstructionTolerance;
                    });
                }

                // Which script this soldier runs: its own MISSION:AI/<taskid>.qvm when the level ships one,
                // otherwise the archetype its AIType selects (ai.md 7.5a and 2.3). The per-task script is
                // what names the soldier's patrol path, so without it a guard has nothing to patrol.
                // When a per-task script exists, the archetype is passed separately so DefaultHandler can
                // delegate to it — the mechanism the original uses at 0x44E060.
                HumanAiTask? mind = brainTask;
                if (host is not null && scripts is not null)
                {
                    QvmProgram? perTaskScript = null;
                    if (mind is not null && mind.Id != GameTask.NoId)
                    {
                        perTaskScript = LoadScript(scripts, scriptCache, AiScriptApi.MissionScriptPath(mind.Id));
                        if (perTaskScript is not null)
                        {
                            perTask++;
                        }
                    }

                    AiArchetype archetype = mind?.Archetype ?? AiArchetype.Guard;
                    QvmProgram? archetypeScript = LoadScript(scripts, scriptCache, AiScriptApi.ScriptPath(archetype));

                    QvmProgram? primary = perTaskScript ?? archetypeScript;
                    QvmProgram? fallback = perTaskScript is not null ? archetypeScript : null;

                    if (primary is not null)
                    {
                        brain.SetScript(host, primary, fallback);
                        patrolling++;
                    }
                }

                _brains[view] = brain;
                index++;
            }
        }

        // AFTER the brains exist: BindCombatRows installs the behaviour on each, so running it before the
        // loop would have found no brains to install it on and every guard would have gone on standing
        // there looking at the player.
        BindCombatRows(config, difficulty);

        int hasRun = 0, total = 0;
        foreach (SoldierView v in _groups.SelectMany(g => g.Soldiers).Select(m => m.View))
        {
            total++;
            if (v.Bones.Set.TryGet(_runAnimation, out _))
            {
                hasRun++;
            }
        }

        _logger.Information(
            LogCategory,
            $"{_brains.Count} soldier(s) with brains ({patrolling} scripted, {perTask} from their own " +
            $"MISSION:AI script), walk animation {_walkAnimation}, run animation {_runAnimation} " +
            $"(in {hasRun} of {total} soldier set(s); the rest fall back to the walk when chasing).");
        _logger.Information(
            LogCategory,
            $"Soldiers per nav graph: " +
            string.Join(", ", graphUse.OrderBy(p => p.Key).Select(p => $"graph{p.Key}:{p.Value}")) +
            (unroutable > 0
                ? $"; {unroutable} soldier(s) name a graph the level does not ship (they fight, but cannot patrol)"
                : string.Empty));
        return _brains.Count;
    }

    /// <summary>Puts each soldier's own weapon model in its hand.</summary>
    /// <param name="mission">The mission whose packs hold the weapon models.</param>
    /// <param name="roster">The weapon table, which maps a <c>WEAPON_ID_*</c> to the model that draws it.</param>
    /// <returns>How many soldiers ended up holding something.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Three reads join up here. <b>What a soldier carries is authored</b>, as ordinary <c>Gun</c> child
    /// tasks of the <c>HumanSoldier</c> carrying a <c>WEAPON_ID_*</c> — the same mechanism that arms the
    /// player — and the <b>first</b> of them is the one in hand, because everything that asks a human what
    /// it is holding takes slot 0 of the list at <c>Human+1220</c> (see
    /// <see cref="PlayerLoadout.WeaponsOf"/>). <b>Which model</b> comes from that id's row of the weapon
    /// table, whose <c>GunModel</c> field is what the world draws it with. And <b>where it hangs</b> is
    /// <see cref="HumanAttachPoints.ThirdPersonWeapon"/>, bone 22, which <c>0x4600C0</c>'s third-person arm
    /// reads out of the body's own palette at <c>+2928</c>/<c>+2940</c>.
    /// </para>
    /// <para>
    /// <b>Correction to the milestone's own framing.</b> The weapon is <em>not</em> resolved from the
    /// soldier's <c>AIType</c>. The AK in <c>AITYPE_PATROL_AK</c> is part of the <c>settings.qsc</c> key
    /// <c>(AIType, Difficulty)</c>, and a level-1 guard whose type says AK routinely carries an MP5SD —
    /// because its own child task says so. Nothing in the engine maps a type to a model.
    /// </para>
    /// </remarks>
    public int Arm(LoadedMission mission, WeaponTypeRegistry roster)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(roster);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int armed = 0;
        Dictionary<string, HeldWeapon?> cache = new(StringComparer.OrdinalIgnoreCase);

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                IReadOnlyList<LoadoutWeapon> carried = PlayerLoadout.WeaponsOf(member.View.Soldier);
                if (carried.Count == 0)
                {
                    continue;
                }

                // Slot 0 of the weapon list, which is the first child that named a weapon.
                string weaponId = carried[0].WeaponId;
                if (!roster.TryGetByScriptId(weaponId, out WeaponType weapon))
                {
                    _logger.Warning(LogCategory, $"'{weaponId}' is not in the weapon table.");
                    continue;
                }

                if (!cache.TryGetValue(weapon.GunModel, out HeldWeapon? held))
                {
                    held = BuildWeapon(mission, weapon.GunModel);
                    cache[weapon.GunModel] = held;
                }

                if (held is null)
                {
                    continue;
                }

                member.Weapon = held;
                member.WeaponType = weapon;
                member.AmmoInClip = weapon.RoundsPerClip;
                armed++;
            }
        }

        _logger.Information(
            LogCategory,
            $"{armed} of {Count} soldier(s) hold a weapon model, over {cache.Values.Count(w => w is not null)} " +
            $"distinct model(s).");
        return armed;
    }

    private HeldWeapon? BuildWeapon(LoadedMission mission, string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return null;
        }

        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' is in no pack this mission opened.");
            return null;
        }

        MeshGeometry? geometry = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
        if (geometry is null)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' has nothing to draw.");
            return null;
        }

        MeshHandle mesh;
        try
        {
            mesh = _device.CreateMesh(geometry.Vertices, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' did not upload: {ex.Message}");
            return null;
        }

        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
        HeldWeapon held = new(geometry, mesh, instance, pack);
        _weapons.Add(held);
        return held;
    }

    /// <summary>Reports what every soldier's AI is actually doing, for diagnosing a level that looks wrong.</summary>
    /// <remarks>
    /// Written for the "why is nobody patrolling?" question, which needs to distinguish four different
    /// failures that look identical on screen: no brain, no script, a script that never asked to patrol, and
    /// a patrol that started but cannot move.
    /// </remarks>
    public void LogAiSummary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Dictionary<string, int> activities = [];
        Dictionary<int, int> paths = [];
        int withRoute = 0;
        int moving = 0;

        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            string key = brain.Activity.ToString();
            activities[key] = activities.GetValueOrDefault(key) + 1;

            if (brain.Route is { } route)
            {
                withRoute++;
                paths[route.Path.Id] = paths.GetValueOrDefault(route.Path.Id) + 1;
                if (route.Current is { } command && command.Kind.IsMovement())
                {
                    moving++;
                }
            }

            _ = view;
        }

        _logger.Information(
            LogCategory,
            $"AI: {_brains.Count} brain(s); {withRoute} on a patrol path; {moving} on a movement command.");

        foreach ((string activity, int count) in activities.OrderByDescending(p => p.Value))
        {
            _logger.Information(LogCategory, $"AI:   activity {activity}: {count}");
        }

        _logger.Information(
            LogCategory,
            $"AI: patrol paths in use: {string.Join(", ", paths.Keys.Order())}");

        if (_travelSamples > 0)
        {
            double mean = _travelTotal / _travelSamples;
            _logger.Information(
                LogCategory,
                $"AI: walk step over {_travelSamples} sample(s): mean {mean:F1} u/tick " +
                $"({mean * 30.0 / WorldScene.UnitsPerMetre:F2} m/s), max {_travelMax:F1} u/tick. " +
                $"Retail measures 160-185 u/tick (about 1.28 m/s).");
        }
    }

    /// <summary>Logs how the level's fight is going, once per change.</summary>
    /// <remarks>
    /// One line, and only when the numbers move, so it is a running commentary rather than a per-tick
    /// flood. It is what tells a "the guards do nothing" report apart into its three possible causes:
    /// nobody sees the player, somebody sees him and never engages, or somebody engages and never
    /// fires.
    /// </remarks>
    private void ReportCombat()
    {
        int seeing = 0;
        int engaging = 0;
        int chasing = 0;
        foreach (AiSoldier brain in _brains.Values)
        {
            if (brain.SeesEnemy)
            {
                seeing++;
            }

            if (brain.Activity == AiSoldierActivity.Engaging)
            {
                engaging++;
            }

            if (brain.IsMovingInCombat)
            {
                chasing++;
            }
        }

        if (seeing == 0 && engaging == 0 && RoundsFired == 0)
        {
            return;
        }

        string report =
            $"{seeing} soldier(s) see the player, {engaging} engaging, {chasing} chasing, {RoundsFired} round(s) fired.";
        if (report == _lastCombatReport)
        {
            return;
        }

        _lastCombatReport = report;
        _logger.Information(LogCategory, $"AI: {report}");
    }

    /// <summary>Logs each input channel a fight asks for, once per type and channel.</summary>
    /// <remarks>
    /// Deliberately the same shape as the probe's <c>[combat] channel</c> line from the retail game, so
    /// a run of the port and a capture of the original can be put side by side. The slot is resolved
    /// through the port's transcription of <c>off_54151C</c>; if it disagrees with the probe's, the
    /// transcription is wrong.
    /// </remarks>
    private void ReportChannels(AiSoldier brain)
    {
        foreach (AiChannelWrite write in brain.ChannelWrites)
        {
            string line = $"[combat] channel type={(int)brain.Type} channel={(int)write.Channel} "
                + $"-> slot {write.Slot} value={write.Value:0.00}";

            if (_reportedChannels.Add(line))
            {
                _logger.Information(LogCategory, line);
            }
        }
    }

    /// <summary>Turns a raised trigger into a round, at the weapon's own cadence.</summary>
    /// <remarks>
    /// <para>
    /// The AI never fires: it holds slot 6 down and the weapon decides when a round comes out, which is
    /// the split the original has between <c>0x48D8E0</c> and the human's weapon tick <c>0x462E50</c>.
    /// The clip is what the AI watches to count its burst, so it has to drain here and nowhere else.
    /// </para>
    /// <para>
    /// The reload takes the weapon's own <c>Reload Time</c>; both of the AI's two reload routes reach
    /// the same place, so they are not told apart here.
    /// </para>
    /// </remarks>
    private void FireIfAsked(SoldierView view, AiSoldier brain, Vector3d enemyFeet, bool enemyCrouching)
    {
        Member? member = FindMember(view);
        if (member?.WeaponType is not { } weapon)
        {
            return;
        }

        if (member.FireCooldown > 0)
        {
            member.FireCooldown--;
        }

        if (member.ReloadTicks > 0 && --member.ReloadTicks == 0)
        {
            member.AmmoInClip = weapon.RoundsPerClip;
        }

        AiCombatCommand command = brain.Combat;

        if ((command.PressReload || command.ReloadWeaponDirectly)
            && member.ReloadTicks == 0
            && member.AmmoInClip < weapon.RoundsPerClip)
        {
            member.ReloadTicks = Math.Max(1, (int)(weapon.ReloadTimeSeconds * 30f));
            return;
        }

        if (!command.PullTrigger || member.FireCooldown > 0 || member.AmmoInClip <= 0 || member.ReloadTicks > 0)
        {
            return;
        }

        member.AmmoInClip--;
        member.FireCooldown = GunTiming.TicksPerRound(weapon.RoundsPerMinute);
        RoundsFired++;

        // The round leaves along where the GUN is pointed, not at the target's true position. Retail's
        // round is a hit-scan ray resolved in the fire tick (GunShot per-tick 0x47B750): the guard's body
        // was turned onto the TRACKED aim point (0x48D8E0 holds fire until the facing is within 2 degrees
        // of it), the ray flies that way, and the hit probability rides the cast's own context (slot+68,
        // loaded from Human+1236 at spawn, sub_474360) to be rolled only when the trace actually meets a
        // human. So the tracked aim's lag against a MOVING target is a real, geometric source of misses,
        // and a round that fails the roll flies on to whatever is behind.
        //
        // A burst-firing guard carries its aim in Combat.TurnTo (the tracked point, started on the target
        // at burst setup, 0x48B9C0). A follow-firing patrol has no aim solve at all (ai.md 13.7) -- it
        // sprays along its own facing whenever the target is inside a 16.3-degree cone, so its ray runs
        // along the body's heading at the aim point's height.
        Vector3d aim = brain.Combat.TurnTo ?? SprayTarget(brain, enemyFeet, enemyCrouching);
        _onRound?.Invoke(brain.EyePosition(), aim, brain.HitProbability, weapon);

        // Every other soldier near the shot hears it -- the same AIEVENT_GUNSHOT a player's shot raises.
        NotifyGunshot(brain.EyePosition(), silenced: false, except: view);
    }

    /// <summary>Where a follow-firing patrol's round goes: along its own facing, at the aim point's height.</summary>
    /// <param name="brain">The shooter.</param>
    /// <param name="enemyFeet">The target's feet, in world units.</param>
    /// <param name="enemyCrouching">Whether the target is crouching.</param>
    /// <remarks>
    /// The follow fire has no burst and no aim solve (ai.md §13.7): the trigger is pressed whenever the
    /// target is inside a 16.3° cone about the shooter's forward axis, and the round flies where the BODY
    /// points. The port's facing has no pitch, so the ray borrows the aim point's height and keeps the
    /// facing's horizontal error — which is the part that decides the spray's geometric misses.
    /// </remarks>
    private static Vector3d SprayTarget(AiSoldier brain, Vector3d enemyFeet, bool enemyCrouching)
    {
        Vector3d aimPoint = AiSoldier.AimPointOf(enemyFeet, enemyCrouching);
        Vector3d eye = brain.EyePosition();
        double dx = aimPoint.X - eye.X;
        double dy = aimPoint.Y - eye.Y;
        double reach = Math.Sqrt((dx * dx) + (dy * dy));
        if (reach < 1.0)
        {
            return aimPoint;
        }

        return new Vector3d(
            eye.X + (-Math.Sin(brain.Facing) * reach),
            eye.Y + (Math.Cos(brain.Facing) * reach),
            aimPoint.Z);
    }

    /// <summary>Delivers a gunshot to every soldier in earshot — the hearing side of <see cref="AiEvent.Gunshot"/>.</summary>
    /// <param name="origin">Where the shot was fired, in world units.</param>
    /// <param name="silenced">Whether the weapon is silenced (cuts the range to a thirtieth).</param>
    /// <param name="except">A soldier not to notify — the shooter, which does not hear its own shot as news.</param>
    /// <remarks>
    /// The range gate (30 m, or 1 m silenced) is applied per soldier in <see cref="AiSoldier.HearGunshot"/>.
    /// The event is queued and pumped on the soldier's next tick; what it does about it is its archetype
    /// script's business.
    /// </remarks>
    public void NotifyGunshot(Vector3d origin, bool silenced, SoldierView? except = null)
    {
        foreach (AiSoldier brain in _brains.Values)
        {
            if (except is not null && _brains.TryGetValue(except, out AiSoldier? shooter) && ReferenceEquals(shooter, brain))
            {
                continue;
            }

            brain.HearGunshot(origin, silenced);
        }
    }

    private Member? FindMember(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (ReferenceEquals(member.View, view))
                {
                    return member;
                }
            }
        }

        return null;
    }

    /// <summary>Refuses a soldier's move that would walk it into another soldier — <c>0x462BA0</c>.</summary>
    /// <remarks>
    /// The original runs this inside the movement integrator against the whole human registry; the port
    /// applies it to each soldier as it is stepped, against every other living soldier's current position.
    /// It <b>refuses</b> a closing move rather than pushing out, exactly as the original does — so two bodies
    /// can jam but neither passes through the other, which is what "two soldiers collided" was missing.
    /// </remarks>
    private void SeparateFromOtherSoldiers(AiSoldier brain, Vector3d before)
    {
        Vector3d resolved = HumanSeparation.Resolve(before, brain.Position, OtherLivingSoldiers(brain));
        brain.SeparateTo(resolved);
    }

    private IEnumerable<Vector3d> OtherLivingSoldiers(AiSoldier self)
    {
        foreach ((_, AiSoldier other) in _brains)
        {
            if (!ReferenceEquals(other, self) && other.Activity != AiSoldierActivity.Dead)
            {
                yield return other.Position;
            }
        }
    }

    /// <summary>Gives every soldier the combat row its own <c>AIType</c> names.</summary>
    /// <remarks>
    /// A soldier with no <c>HumanAI</c>, or one naming a type the engine does not register, is left off
    /// the table: the original never configures such a body either, so its maximum health stays the 1.0
    /// its constructor set.
    /// </remarks>
    private void BindCombatRows(HumanAiConfigRegistry? config, AiDifficulty difficulty)
    {
        if (config is null)
        {
            return;
        }

        int bound = 0;
        var arms = new Dictionary<AiCombatArm, int>();

        // The resolved accuracy per AI type actually in the level, so "I die too fast even on Easy" can be
        // read straight off the log: point-blank / cone-end hit chance is the row's HitProbability pair, and
        // a guard that shows 1.000 / 0.250 got the built-in default -- settings.qsc did not supply a row for
        // this (type, difficulty) and the difficulty is doing nothing. A genuinely lower number is the data.
        var accuracyByType = new Dictionary<AiType, (int Count, AiCombatParameters Row)>();

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (FindMind(member.View)?.ResolvedType is not { } type)
                {
                    continue;
                }

                AiCombatParameters row = config.Parameters(type, difficulty);
                _combat[member.View] = row;
                bound++;

                (int count, AiCombatParameters _) = accuracyByType.GetValueOrDefault(type);
                accuracyByType[type] = (count + 1, row);

                if (_brains.TryGetValue(member.View, out AiSoldier? brain))
                {
                    Member held = member;
                    brain.SetCombat(type, row, () => new AiCombatWeaponState(
                        held.AmmoInClip,
                        held.WeaponType?.RoundsPerClip ?? 0,
                        held.WeaponType?.MaximumRoundsPerBurst ?? 0,
                        held.WeaponType?.Category ?? WeaponCategory.Knife));
                    arms[brain.Arm] = arms.GetValueOrDefault(brain.Arm) + 1;
                }
            }
        }

        // The arm decides whether a soldier can chase at all: only the patrol (move-and-fight) arm hunts and
        // advances; the guard arm stands its ground. Logged so a "they don't chase" report can be told apart.
        string split = string.Join(", ", arms.OrderBy(a => a.Key).Select(a => $"{a.Key}:{a.Value}"));
        _logger.Information(
            LogCategory,
            $"AI: {bound} soldier(s) configured at {HumanAiConfigRegistry.NameOf(difficulty)} ({difficulty}); arms {split}.");

        // One line per AI type: the hit chance a guard of that type actually fires with, at this difficulty.
        // "hit@0m" is point-blank (where you die), "hit@far" is the cone's end; the round handler rolls a
        // uniform against the range-lerped value between them. clip% is the fraction of a magazine a burst
        // spends. 1.000/0.250 = the default, i.e. settings.qsc gave no row for this pair.
        foreach ((AiType type, (int count, AiCombatParameters row)) in accuracyByType.OrderBy(e => e.Key))
        {
            string name = HumanAiConfigRegistry.NameOf(difficulty);
            _logger.Information(
                LogCategory,
                FormattableString.Invariant(
                    $"AI accuracy [{type}] x{count} @ {name}: hit@0m={row.HitProbabilityAtMinimumRange:F3} hit@far={row.HitProbabilityAtMaximumRange:F3} clip%={row.MinimumClipFraction:F2}+{row.RandomClipFraction:F2} aimPrec={row.AimPrecision:F2}"));
        }
    }

    /// <summary>The <c>HumanAI</c> child a soldier's body carries, if the level gave it one.</summary>
    /// <summary>A level task's position by id — what the activate walk aims at.</summary>
    /// <param name="task">The subtree to search.</param>
    /// <param name="id">The task id the script named.</param>
    /// <returns>The position, or default when the id names nothing with one.</returns>
    private static Vector3d TaskPositionOf(GameTask task, int id)
    {
        if (task.Id == id)
        {
            if (task is DeclaredTask declared
                && declared.TryGetValue("Position", out object? pos)
                && pos is Vector3d p)
            {
                return p;
            }

            return default;
        }

        foreach (GameTask child in task.Children)
        {
            Vector3d found = TaskPositionOf(child, id);
            if (found != default)
            {
                return found;
            }
        }

        return default;
    }

    private static HumanAiTask? FindMind(SoldierView view)
    {
        foreach (GameTask child in view.Soldier.Children)
        {
            if (child is HumanAiTask mind)
            {
                return mind;
            }
        }

        return null;
    }

    private QvmProgram? LoadScript(ResourceRegistry scripts, Dictionary<string, QvmProgram?> cache, string qscPath)
    {
        string path = qscPath.Replace(".qsc", ".qvm", StringComparison.Ordinal);
        if (cache.TryGetValue(path, out QvmProgram? cached))
        {
            return cached;
        }

        QvmProgram? program = null;
        try
        {
            program = QvmProgram.Read(scripts.Load(path));
        }
        catch (Exception ex) when (ex is IOException or QvmFormatException or InvalidOperationException)
        {
            // A level ships a per-task script only for the soldiers it wants to override, so a miss here is
            // ordinary rather than an error.
            _logger.Debug(LogCategory, $"No behaviour script at '{path}': {ex.Message}");
        }

        cache[path] = program;
        return program;
    }

    private AiSoldier.GroundQuery? MakeGroundQuery()
    {
        TerrainData? terrain = _terrain;
        WorldRayCast? cast = _cast;
        if (terrain is null && cast is null)
        {
            return null;
        }

        return (double x, double y, double z, out double groundZ) =>
        {
            groundZ = double.MinValue;
            bool found = false;

            if (cast is not null
                && cast(
                    new Vector3d(x, y, z),
                    new Vector3d(x, y, z - ProbeBelowInUnits - ProbeAboveInUnits),
                    out WorldRayHit hit))
            {
                groundZ = hit.Position.Z;
                found = true;
            }

            if (terrain is not null
                && TerrainHeightQuery.TryGetGround(terrain, x, y, z, out TerrainGround ground)
                && ground.Height >= z - ProbeBelowInUnits - ProbeAboveInUnits
                && ground.Height <= z)
            {
                if (ground.Height > groundZ)
                {
                    groundZ = ground.Height;
                }

                found = true;
            }

            return found;
        };
    }

    private static float GammaOf(SoldierView view) => view.Soldier.Gamma;

    private static double Length(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>A round a soldier has just fired.</summary>
    /// <param name="origin">Where it leaves, in world units — the soldier's eye.</param>
    /// <param name="aim">Where it is aimed, in world units.</param>
    /// <param name="hitProbability">
    /// The chance the AI gave it of being on target — <c>Human+1236</c>, which the weapon reads with
    /// every round.
    /// </param>
    /// <param name="weapon">What fired it.</param>
    public delegate void SoldierRound(Vector3d origin, Vector3d aim, float hitProbability, WeaponType weapon);

    /// <summary>Sets where a soldier's rounds go.</summary>
    /// <param name="handler">The handler, or <see langword="null"/> to let rounds vanish.</param>
    /// <remarks>
    /// The soldier raises the trigger; this is where the round becomes a cast. Keeping the two apart is
    /// the original's own split — the AI writes an input slot and the body's weapon path does the rest.
    /// </remarks>
    public void SetRoundHandler(SoldierRound? handler) => _onRound = handler;

    /// <summary>Gets how many rounds the level's soldiers have fired.</summary>
    public int RoundsFired { get; private set; }

    /// <summary>Advances every soldier's animation by one tick and re-skins the groups.</summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing advanced a soldier's rig before this existed</b>, so every guard in the level stood frozen on
    /// the first frame of its stand animation — which reads as a default A-pose. Soldiers are grouped by
    /// (model, skeleton, animation) and share one skinned mesh, so ticking the group's first soldier and
    /// re-skinning from its pose animates the whole group: they run the same clip from the same start, so they
    /// share a pose by construction.
    /// </para>
    /// <para>
    /// The device's meshes are immutable, so this destroys and re-uploads. There are only a handful of groups
    /// in a level, so it is a few uploads a tick — worth replacing with a dynamic vertex buffer, but not worth
    /// leaving the guards frozen for.
    /// </para>
    /// </remarks>
    /// <param name="enemy">The enemy's eye position, which the view cones are tested against.</param>
    /// <param name="enemyAlive">
    /// Whether the enemy is still a target. A dead player is not: the detection scan (<c>0x489750</c>)
    /// unregisters a human that reports itself invalid, so the guards lose it and stop firing at the
    /// corpse — passing <see langword="false"/> reproduces that.
    /// </param>
    /// <param name="enemyFeet">
    /// The enemy task's position — its feet, what combat aims at (<c>sub_451380</c>). Defaults to
    /// <paramref name="enemy"/> when the caller has no body to speak of.
    /// </param>
    /// <param name="enemyCrouching">Whether the enemy is crouching, which halves the aim raise.</param>
    public void Tick(
        Vector3d enemy = default,
        bool enemyAlive = true,
        Vector3d? enemyFeet = null,
        bool enemyCrouching = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _tick++;

        // Vision watches the eye; combat aims at the feet + 0.5 m (sub_451380). See AiSoldier.Tick.
        Vector3d feet = enemyFeet ?? enemy;

        // EVERY soldier's rig is advanced, not just each group's representative. Ticking only the
        // representative left every other soldier's BoneObject frozen, so its RootMotion() was zero and the
        // AI never moved it -- while the shared group mesh, posed from the representative, showed it walking
        // on the spot. Exactly one soldier per group actually moved: that group's representative.
        foreach ((SoldierView view, AiSoldier _) in _brains)
        {
            view.Tick();
        }

        // The doors advance with the task pass, before the AIs read their state. A door that is not
        // fully closed stops colliding, so a walker (and a round, and a sight ray) passes the doorway.
        if (_doors is { } doors)
        {
            doors.Tick();
            if (_solids is { } solids)
            {
                foreach (PlacedDoor door in doors.Doors)
                {
                    solids.SetPassable(door.TaskName, !door.State.IsFullyClosed);
                }
            }
        }

        // The alarm controls advance first, so this tick's AIEVENT_ALARMON reflects this tick's state
        // -- 0x4408A0's update runs on the task pass, before the AIs. The on condition stands in for
        // the control's authored expression: any named trigger pressed turns it on, and it stays on.
        foreach ((int controlId, AiAlarmControlState alarm) in _alarmStates)
        {
            bool triggered = false;
            foreach (AiSoldier owner in _brains.Values)
            {
                if (owner.AlarmControlId == controlId
                    && owner.AlarmTriggerId != -1
                    && _activatedTasks.Contains(owner.AlarmTriggerId))
                {
                    triggered = true;
                    break;
                }
            }

            alarm.Update(_tick, disabled: false, enabled: true, triggerCondition: triggered, onCondition: triggered);
        }

        // The AI runs second, so the root motion it is handed is this tick's, not last tick's.
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (view.IsDead)
            {
                // A corpse is still stepped. The death state's per-tick (0x45C700) adds the fall solver's
                // corrective velocity, the soldier update moves the body by the death clip's own root
                // motion, and the ground query catches it. Freezing the body here is what left it standing.
                brain.Kill();
                brain.TickDeadBody(view.RootMotion());
                view.PlaceAt(brain.Position, view.Orientation);
                continue;
            }

            double travel = Math.Abs(view.RootMotion().Y);

            // Retail ground truth, measured by hooking 0x48E920 in the original: a walking guard covers
            // 160-185 units a tick, about 1.28 m/s, varying through the gait cycle. Recorded here so the
            // port's own figure can be compared without another instrumented run.
            if (brain.Activity == AiSoldierActivity.Patrolling && travel > 0.0)
            {
                _travelSamples++;
                _travelTotal += travel;
                _travelMax = Math.Max(_travelMax, travel);
            }

            // The alarm the soldier watches is the one ITS OWN +15044 names -- resolved per AI, as the
            // retail per-tick resolves the control task each tick. First sight of an id lazily makes
            // the state, since the ids arrive only once the CREATE dispatch has run the script.
            bool alarmOn = false;
            if (brain.AlarmControlId != -1)
            {
                if (!_alarmStates.TryGetValue(brain.AlarmControlId, out AiAlarmControlState? watched))
                {
                    watched = new AiAlarmControlState();
                    _alarmStates[brain.AlarmControlId] = watched;
                }

                alarmOn = watched.IsOn;
            }

            Vector3d before = brain.Position;
            brain.Tick(
                _tick, enemy, travel, alarmOn: alarmOn, enemyAlive: enemyAlive,
                enemyFeet: feet, enemyCrouching: enemyCrouching);
            SeparateFromOtherSoldiers(brain, before);
            FireIfAsked(view, brain, feet, enemyCrouching);

            // An engaged guard pulls in the guards beside it: every 20 ticks it posts AIEVENT_ALERT
            // with a 6.875 m reach (sub_48A920's %20 counter -> sub_48AB10, radius 28160). This is how
            // one guard's fight wakes the room even before anybody fires.
            if (brain.Activity == AiSoldierActivity.Engaging && _tick % AiSoldier.AlertPingPeriodTicks == 0)
            {
                foreach (AiSoldier neighbour in _brains.Values)
                {
                    if (!ReferenceEquals(neighbour, brain))
                    {
                        neighbour.ReceiveAlert(brain.Position);
                    }
                }
            }
            ReportCombat();
            ReportChannels(brain);

            // A patrol ANIMATION command names a clip outright; a soldier in a fight plays its WEAPON's
            // own clips; otherwise the gait picks between the walk cycle and the soldier's stand
            // animation.
            if (brain.RequestedAnimation >= 0)
            {
                view.SetMoving(true, brain.RequestedAnimation);
            }
            else if (brain.Activity == AiSoldierActivity.Engaging && brain.IsMovingInCombat)
            {
                // The soldier is travelling a combat route this tick -- advancing, repositioning or hunting a
                // lost target while it watches. A chasing guard RUNS (retail's Combat-Follow runs in state 17
                // = anim 48 at ~4.4 m/s), and the run clip's own root motion is what carries it at that speed
                // (AiSoldier.AdvanceInCombat reads it). Fall back to the walk if the set has no run clip.
                if (_runAnimation < 0 || !view.SetMoving(true, _runAnimation))
                {
                    view.SetMoving(true, _walkAnimation);
                }
            }
            else if (brain.Activity == AiSoldierActivity.Engaging)
            {
                // Stationary combat: run the real off_53EC10 machine instead of inferring the state each
                // tick. It is fed the clip-finished signal (BoneClip.EndedThisTick = the machine's 0x1 bit)
                // so every state holds until its clip completes -- the hold the old per-tick inference could
                // not do, which is why the firing animation looped.
                //
                // A set that lacks the state's clip must not WEDGE the machine: with no clip playing,
                // EndedThisTick never fires, the machine held its state forever, and the guard stood
                // frozen in the walk pose while its rounds kept leaving -- "a guard playing no animation
                // but I'm taking damage". When last tick's clip could not be placed, the done signal is
                // forced so the machine advances and the next state gets its chance to animate.
                bool animationDone = (view.Bones.SourceOf(0)?.EndedThisTick ?? false)
                    || _combatClipMissing.Contains(view);
                int anim = HumanSoldierStates.AnimationOf(brain.TickCombatState(animationDone));
                bool placed = anim >= 0 && view.SetMoving(true, anim);
                if (placed)
                {
                    _combatClipMissing.Remove(view);
                }
                else
                {
                    view.SetMoving(false, _walkAnimation);
                    _combatClipMissing.Add(view);
                }
            }
            else
            {
                view.SetMoving(brain.Activity == AiSoldierActivity.Patrolling, _walkAnimation);
            }

            view.PlaceAt(brain.Position, brain.Orientation);
        }

        // Each soldier is skinned from ITS OWN rig, which is what the original does (0x49F700 is called
        // through a stack descriptor about once per soldier per frame). Sharing one skinned mesh per group
        // made every soldier wear the first one's pose, so once guards diverged onto real patrol paths
        // nothing appeared to animate at all.
        //
        // The skin lands in a persistent per-soldier buffer and replaces the mesh's vertices in place:
        // no allocation, no mesh churn, and the pose being replaced becomes the mesh's previous-pose
        // stream -- which is exactly the "previous frame's skinned vertices" the temporal resolve needs
        // and the second pose the between-tick blend draws against.
        foreach (Group group in _groups)
        {
            if (group.Model is null)
            {
                continue;
            }

            foreach (Member member in group.Soldiers)
            {
                if (!_brains.ContainsKey(member.View))
                {
                    // Un-brained soldiers still need their own rig advanced.
                    member.View.Tick();
                }

                Vertex3D[] skinned = member.SkinScratch ??= new Vertex3D[group.Geometry.Vertices.Length];
                group.Geometry.Vertices.CopyTo(skinned, 0);
                try
                {
                    MefSkinner.Skin(
                        group.Model,
                        member.View.UnrotatedPose(),
                        (float)(1.0 / WorldScene.UnitsPerMetre),
                        skinned);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!member.Mesh.IsValid)
                {
                    continue;
                }

                try
                {
                    _device.UpdateAnimatedMesh(member.Mesh, skinned);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                member.PoseFlippedSinceFrame = true;
            }
        }

        // With the poses settled, record where each body stands and faces THIS tick. The presentation
        // draws between the last two of these records, so they are the whole interface between the
        // 30 Hz simulation and however fast the display runs.
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                CaptureTickState(member);
            }
        }
    }

    /// <summary>Records a soldier's drawn placement for this tick, keeping the tick before.</summary>
    private void CaptureTickState(Member member)
    {
        (Vector3d where, Matrix3f orientation) = TickPlacementOf(member);
        float facing = FacingOf(orientation);

        member.PrevTickWhere = member.HasTickState ? member.TickWhere : where;
        member.PrevTickFacing = member.HasTickState ? member.TickFacing : facing;
        member.TickWhere = where;
        member.TickFacing = facing;
        member.TickOrientation = orientation;

        if (member.Weapon is not null)
        {
            BonePose pose = member.View.UnrotatedPose();
            if (HumanAttachPoints.ThirdPersonWeapon < pose.BoneCount)
            {
                Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
                Matrix3f rotation = pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
                member.PrevTickHandPosition = member.HasHandState ? member.TickHandPosition : hand;
                member.PrevTickHandRotation = member.HasHandState ? member.TickHandRotation : rotation;
                member.TickHandPosition = hand;
                member.TickHandRotation = rotation;
                member.HasHandState = true;
            }
        }

        member.HasTickState = true;
    }

    /// <summary>Where a soldier's body is drawn for the current tick, in metres, and its heading.</summary>
    /// <remarks>
    /// The placement <see cref="Draw"/> used to derive per pass, moved to the tick so the ground probe
    /// runs once per soldier per tick instead of once per soldier per pass — the shadow cascades and
    /// the geometry pass then agree by construction.
    /// </remarks>
    private (Vector3d Where, Matrix3f Orientation) TickPlacementOf(Member member)
    {
        SoldierView soldier = member.View;

        // A soldier with a brain is drawn where its AI put it, lifted by the CLIP'S OWN ROOT HEIGHT.
        // That is the original's Human+1300 (see SoldierView.RootSample): every retail skeleton's
        // bone 0 rest translation is zero, so the pose puts the pelvis at the object origin and the
        // feet a metre below it, and the root track's Z is what bridges the two. Only the Z is taken:
        // the horizontal part of the root track is what MOVES the body — the mover consumes the same
        // track's per-tick delta — so adding it again would run the model a stride ahead of its feet.
        if (_brains.TryGetValue(soldier, out AiSoldier? brain) && brain.HasGroundQuery)
        {
            // Snap the feet to whatever the collision puts under them, exactly as the static-soldier
            // Stand() does. A level often authors a soldier a little below its floor; the query only
            // pulls UP onto a floor within the half-metre probe, so a soldier already on its floor
            // does not move.
            double groundedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
            return (new Vector3d(
                soldier.Position.X / WorldScene.UnitsPerMetre,
                soldier.Position.Y / WorldScene.UnitsPerMetre,
                (groundedZ + soldier.RootSample.Z) / WorldScene.UnitsPerMetre), soldier.Orientation);
        }

        if (_brains.ContainsKey(soldier))
        {
            return (new Vector3d(
                soldier.Position.X / WorldScene.UnitsPerMetre,
                soldier.Position.Y / WorldScene.UnitsPerMetre,
                member.Position.Z), soldier.Orientation);
        }

        return (member.Position, soldier.Orientation);
    }

    /// <summary>A body's heading out of its world 3×3 — the inverse of <c>Z(facing)</c>.</summary>
    /// <remarks>
    /// The second column is the body's forward, and a soldier's forward is
    /// <c>(-sin f, cos f)</c> (see <see cref="AiSoldier"/>), so the angle reads straight back out.
    /// Orientations are pure Z rotations for every body this renderer draws.
    /// </remarks>
    private static float FacingOf(in Matrix3f orientation) => MathF.Atan2(-orientation.M12, orientation.M22);

    /// <summary>Resolves where every soldier is drawn for one rendered frame.</summary>
    /// <param name="tickFraction">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <param name="smooth">
    /// Whether to draw between the two tick states. Off, every value is this tick's verbatim — the
    /// original's stepped presentation, which the vanilla reference captures depend on being exact.
    /// </param>
    /// <remarks>
    /// Once per frame, before any pass draws. <see cref="Draw"/> runs once per shadow cascade and once
    /// for the geometry pass, so anything resolved per frame — the blend, the ground snap, the motion
    /// bookkeeping — has to be resolved here or the passes would disagree with each other.
    /// </remarks>
    public void BeginFrame(float tickFraction, bool smooth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        float alpha = Math.Clamp(tickFraction, 0f, 1f);
        _frameAlpha = alpha;
        _frameSmooth = smooth;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                ResolveFrameState(member, alpha, smooth);
            }
        }
    }

    /// <summary>Computes one soldier's drawn placement, pose blend and motion for this frame.</summary>
    private void ResolveFrameState(Member member, float alpha, bool smooth)
    {
        if (!member.HasTickState)
        {
            // Drawn before its first tick: stand exactly where the level put it.
            CaptureTickState(member);
        }

        // The rigid half. When nothing moved — or smoothing is off — the tick's own values pass
        // through verbatim, so the vanilla picture is not merely close but identical.
        Vector3d where = smooth && member.PrevTickWhere != member.TickWhere
            ? PresentationBlend.Lerp(member.PrevTickWhere, member.TickWhere, alpha)
            : member.TickWhere;

        Matrix3f orientation = smooth && member.PrevTickFacing != member.TickFacing
            ? Matrix3f.FromEngineEulerAngles(0f, 0f, PresentationBlend.LerpAngle(member.PrevTickFacing, member.TickFacing, alpha))
            : member.TickOrientation;

        member.PreviousDrawn = member.HasFrameState ? (member.DrawnWhere, member.DrawnOrientation) : null;
        member.DrawnWhere = where;
        member.DrawnOrientation = orientation;

        // The pose half. Smoothing draws 1-alpha of the previous tick's skin; the blend that described
        // LAST frame's pose is kept so the geometry pass can reconstruct last frame's vertex from the
        // same two streams. When the streams flipped since the last frame the old blend no longer names
        // the right pair, and the previous stream itself — the whole of last tick's pose — is the
        // closest expressible stand-in; by then last frame's blend was nearly there anyway.
        member.PreviousPoseBlend = member.PoseFlippedSinceFrame ? 1f : member.PoseBlend;
        member.PoseBlend = smooth ? 1f - alpha : 0f;
        member.PoseFlippedSinceFrame = false;

        // A body's very first frame has no history anywhere on screen; say so rather than let the
        // resolve reproject into whatever stood there before.
        member.NoHistoryThisFrame = !member.HasFrameState;
        member.HasFrameState = true;

        // The held weapon rides the hand bone, interpolated the same way — rigid, so its previous
        // transform alone reprojects it exactly.
        if (member.Weapon is not null && member.HasHandState)
        {
            Vector3f hand;
            Matrix3f handRotation;
            if (smooth)
            {
                hand = member.PrevTickHandPosition + ((member.TickHandPosition - member.PrevTickHandPosition) * alpha);
                handRotation = PresentationBlend.InterpolateRotation(member.PrevTickHandRotation, member.TickHandRotation, alpha);
            }
            else
            {
                hand = member.TickHandPosition;
                handRotation = member.TickHandRotation;
            }

            // M . v, so the transpose of the row-vector Transform (see DrawWeapon's remarks).
            Vector3f turned = orientation.Transposed().Transform(hand);
            member.PreviousWeapon = member.HasWeaponFrameState
                ? (member.DrawnWeaponOrigin, member.DrawnWeaponRotation)
                : null;
            member.DrawnWeaponOrigin = new Vector3d(
                where.X + (turned.X / WorldScene.UnitsPerMetre),
                where.Y + (turned.Y / WorldScene.UnitsPerMetre),
                where.Z + (turned.Z / WorldScene.UnitsPerMetre));
            member.DrawnWeaponRotation = orientation * handRotation;
            member.HasWeaponFrameState = true;
        }
    }

    /// <summary>Draws every soldier.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform — soldiers use no other.</param>
    /// <remarks>
    /// Called once per shadow cascade and once for the geometry pass; everything per-frame was settled
    /// by <see cref="BeginFrame"/>, so every pass draws the same bodies in the same places.
    /// </remarks>
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_groups.Count == 0)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        // Motion is a geometry-pass concern: only the G-buffer records it, and the previous pose is
        // built against the scene camera, which the shadow cascades' light cameras are not.
        bool reportMotion = _device.CurrentPass == ScenePass.Geometry;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                // A dead soldier keeps being drawn -- a body stays on the ground in the original, and
                // vanishing was only ever the port having no death animation to play. Each soldier
                // carries its own skinned mesh, so a corpse is posed by its own death clip.
                if (!member.HasFrameState)
                {
                    ResolveFrameState(member, 1f, smooth: false);
                }

                Matrix4f modelView = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);

                // The pose blend applies to the body's animated mesh in every pass — the shadow pass
                // blends too, or the body would stand a step from its own shadow.
                _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);

                // The gun first, with its OWN previous transform: it is placed by the hand bone, and
                // handing it the body's previous pose would report it sliding by the distance between
                // the two.
                DrawWeapon(member, camera, reportMotion);

                // The body's previous rigid pose, through the CURRENT camera; the previous-pose vertex
                // stream carries the limbs. Together they are the exact previous clip position — the
                // thing whose absence had TAA off at every preset.
                if (reportMotion && member.PreviousDrawn is { } previous)
                {
                    _device.SetPreviousPose(camera.CreateModelView(previous.Where, previous.Orientation));
                }

                // Out of the ambient occlusion. A soldier is thin, close and moving, which is the
                // worst case for a search that has to guess how deep a surface is from its front
                // face alone -- it darkened the ground behind every guard in a halo of speckle
                // that followed him about. The right repair is a thickness test in the search
                // itself; until that exists, soldiers are left out rather than shaded wrongly.
                _device.SetMaterialOverride(new SurfaceMaterial(
                    CastsOcclusion: false,
                    NoHistory: member.NoHistoryThisFrame));

                foreach (MeshSubset subset in group.Geometry.Subsets)
                {
                    if (subset.IndexCount == 0)
                    {
                        continue;
                    }

                    TextureHandle diffuse = TextureHandle.None;
                    if (subset.HasMaterial
                        && group.Instance is { } instance
                        && group.Pack is { } pack
                        && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
                    {
                        diffuse = GetTexture(texture);
                    }

                    _device.DrawMesh(
                        member.Mesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        diffuse,
                        TextureHandle.None,
                        BlendMode.Opaque);
                }

                _device.SetMaterialOverride(SurfaceMaterial.Default);
                _device.ClearPreviousPose();
                _device.ClearPoseInterpolation();
            }
        }
    }

    /// <summary>Gets a value indicating whether the player's third-person body is posed and drawing.</summary>
    /// <remarks>Set once the player has died and <see cref="KillPlayerBody"/> has run; the death camera reads it.</remarks>
    public bool IsPlayerBodyActive => _playerBodyActive;

    /// <summary>Scores a guard's round on the <b>player's own body</b> — its skinned collision mesh.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="playerFeetUnits">The player's feet, in world units.</param>
    /// <param name="playerYaw">Which way the player faces, in radians.</param>
    /// <param name="hit">The contact, when there is one.</param>
    /// <returns><see langword="false"/> when the round misses the player's body, or no body is loaded.</returns>
    /// <remarks>
    /// The original resolves a shot at the player exactly as at a soldier — against the human's skinned
    /// collision mesh — so a round through a limb scores that limb (×0.5–0.75), not the torso. The port
    /// had cast at a 0.55 m cylinder and scored every hit as the trunk, which made the player both a
    /// fatter target and one that took maximum damage from every round. This poses the player's own
    /// (otherwise idle) third-person body at the live position and heading and traces the real mesh.
    /// </remarks>
    public bool TryHitPlayerBody(
        Vector3d origin, Vector3d end, Vector3d playerFeetUnits, float playerYaw, out HumanHit hit)
    {
        hit = default;
        if (_playerBodyView is not { } view || _playerBodyGroup?.CollisionMesh is not { } mesh)
        {
            return false;
        }

        view.PlaceAt(playerFeetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, playerYaw));
        Matrix3f m = view.Orientation;
        Vector3f facing = new(m.M12, m.M22, m.M32);
        return HumanHitTest.TryCastMesh(
            origin, end, playerFeetUnits, facing, mesh, view.Pose(), view.RootSample.Z, out hit);
    }

    /// <summary>
    /// Loads the player's third-person body — the model the level's <c>HumanPlayer</c> declares, on
    /// skeleton 0's set, standing in clip 2 — so the death camera has a body to reveal.
    /// </summary>
    /// <param name="mission">The mission whose packs hold the model.</param>
    /// <param name="model">The player's model, from the <c>HumanPlayer</c> task's <c>Model</c> parameter.</param>
    /// <param name="set">Skeleton 0's animation set (<c>000.iff</c>), the same one the player walks on.</param>
    /// <param name="position">Where the level places the player, in engine units.</param>
    /// <param name="gamma">The heading the player starts facing.</param>
    /// <returns><see langword="false"/> when the model does not resolve or does not upload.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The body is built from a synthetic <see cref="HumanSoldierTask"/> because that is all
    /// <see cref="SoldierView"/> needs — skeleton, stand clip, position and heading — and the player's
    /// constructor (<c>0x410370</c>) builds exactly that on <c>Human+104</c> (see <see cref="HumanPlayerBody"/>).
    /// It is kept out of <c>_groups</c> so nothing else draws or ticks it; only the death path does.
    /// </remarks>
    public bool LoadPlayerBody(
        LoadedMission mission, string model, BoneAnimationSet set, Vector3d position, float gamma)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(set);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ReleasePlayerBody();

        HumanSoldierTask task = new()
        {
            Model = model,
            BoneHierarchy = set.Skeleton.Id,
            StandAnimation = HumanPlayerBody.StandAnimation,
            Position = position,
            Gamma = gamma,
        };

        SoldierView view;
        try
        {
            view = new SoldierView(task, set);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(LogCategory, $"The player body could not be posed on skeleton {set.Skeleton.Id}: {ex.Message}");
            return false;
        }

        Group? group = BuildGroup(mission, model, view);
        if (group is null)
        {
            return false;
        }

        MeshHandle own;
        try
        {
            own = _device.CreateAnimatedMesh(group.Geometry.Vertices, group.Geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"The player body '{model}' did not upload: {ex.Message}");
            return false;
        }

        _playerBodyView = view;
        _playerBodyGroup = group;
        _playerBodyMember = new Member(view, default, own);
        _playerBodyGround = MakeGroundQuery();
        _playerBodyActive = false;
        _playerBodyHasTickState = false;
        _playerBodyCaptureTick = -1;
        _logger.Information(LogCategory, $"Loaded the player's third-person body '{model}' on skeleton {set.Skeleton.Id}.");
        return true;
    }

    /// <summary>
    /// Poses the player's body with its death fall and starts drawing it — the player has died.
    /// </summary>
    /// <param name="deathAnimation">The fall clip the death state chose, from the player's hit-zone row.</param>
    /// <param name="feet">Where the body falls, in engine units — the player's own position.</param>
    /// <param name="gamma">Which way the body faces as it goes down.</param>
    /// <remarks>Does nothing when no body was loaded; the death still happens, just without a body to show.</remarks>
    public void KillPlayerBody(int deathAnimation, Vector3d feet, float gamma)
    {
        if (_playerBodyView is null)
        {
            return;
        }

        _playerBodyFacing = gamma;
        _playerBodyView.PlaceAt(feet, Matrix3f.FromEngineEulerAngles(0f, 0f, gamma));
        _playerBodyView.Kill(deathAnimation);
        _playerBodyActive = true;

        // A death is a cut, not a movement: the body appears where the player fell, and nothing on
        // screen is its history. Snap the interpolation pair and start the frame bookkeeping over.
        CapturePlayerBodyTickState(snap: true);
        if (_playerBodyMember is { } fresh)
        {
            fresh.HasFrameState = false;
            fresh.HasTickState = false;
        }

        // The fall clip has to resolve in the player's set, or Kill silently keeps the idle and the body
        // never falls; reported once so a wrong id is visible rather than a body that just stands there.
        if (_playerBodyView.CurrentAnimation != deathAnimation)
        {
            _logger.Warning(
                LogCategory,
                $"The player's death clip {deathAnimation} is not in its set; the body keeps clip "
                    + $"{_playerBodyView.CurrentAnimation} and will not fall.");
        }
    }

    /// <summary>Advances the player body's death clip by one tick and re-skins it, once it is active.</summary>
    /// <remarks>
    /// The same per-tick re-skin a corpse gets: the clip advances, holding its last frame when it runs out,
    /// and the body's own mesh is rebuilt from the new pose so the fall plays through.
    /// </remarks>
    public void TickPlayerBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_playerBodyActive
            || _playerBodyView is null
            || _playerBodyMember is null
            || _playerBodyGroup?.Model is not { } model)
        {
            return;
        }

        _playerBodyView.Tick();

        // The knock-back. A dying body is thrown -- the fall clip's own root-motion track carries it back
        // and down, and the retail mover consumes exactly that (0x4109A0 samples the body clip's root delta
        // through sub_4D2FB0 and moves the human by it, until 0x40E620 sees the body grounded and stops it).
        // Advancing the clip WITHOUT moving the body left it crumpling on the spot instead of being thrown,
        // which is the missing "knocked to the ground" feel. This is the corpse move a soldier already gets
        // (AiSoldier.TickDeadBody): the horizontal root motion, rotated by the facing, plus a ground snap.
        Vector3f rootMotion = _playerBodyView.RootMotion();
        Vector3d world = SoldierDeathFall.RotateAboutZ(
            new Vector3d(rootMotion.X, rootMotion.Y, rootMotion.Z), _playerBodyFacing);

        double px = _playerBodyView.Position.X + world.X;
        double py = _playerBodyView.Position.Y + world.Y;
        double pz = _playerBodyView.Position.Z + world.Z;
        if (_playerBodyGround is { } ground
            && ground(px, py, pz + ProbeAboveInUnits, out double groundZ))
        {
            pz = groundZ;
        }

        _playerBodyView.PlaceAt(new Vector3d(px, py, pz), _playerBodyView.Orientation);
        ReskinPlayerBody(_playerBodyView, model);
        CapturePlayerBodyTickState(snap: false);
    }

    /// <summary>Gets whether the player's third-person body is being shown climbing a ladder.</summary>
    public bool IsPlayerBodyClimbing => _playerBodyClimbing;

    /// <summary>Starts the third-person body climbing — one continuous clip loop (state 37).</summary>
    /// <param name="animation">The climb clip, animation 168 in the player's set (state 37's binding).</param>
    /// <remarks>Called once on mounting; the clip loops and is never restarted, so the pose never snaps.</remarks>
    public void StartPlayerClimbLoop(int animation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playerBodyView is not { } view)
        {
            return;
        }

        _playerBodyClimbing = true;
        view.StartClimbLoop(animation);

        // Mounting is a cut: the body appears on the rungs, so neither the interpolation pair nor the
        // temporal history may bridge from wherever it stood before.
        _playerBodyHasTickState = false;
        if (_playerBodyMember is { } member)
        {
            member.HasFrameState = false;
        }
    }

    /// <summary>Advances the climb clip one tick and returns how far its root motion moved the body.</summary>
    /// <param name="facing">Which way the body faces — into the ladder.</param>
    /// <param name="rootDelta">
    /// Receives the clip's root-motion delta for this tick, already rotated into the facing frame (world
    /// units) — the same transform a walking guard's motion gets. The caller applies it to the position.
    /// </param>
    /// <param name="cycled">Receives whether the clip completed a cycle this tick — one rung done.</param>
    /// <remarks>Ticks and re-skins the body but does not move it; placement is the caller's, via
    /// <see cref="PlacePlayerBodyClimbing"/>.</remarks>
    public void AdvancePlayerClimbStep(float facing, out Vector3d rootDelta, out bool cycled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        rootDelta = default;
        cycled = false;
        if (_playerBodyView is not { } view || _playerBodyGroup?.Model is not { } model)
        {
            return;
        }

        _playerBodyClimbing = true;
        _playerBodyFacing = facing;
        view.Tick();

        Vector3f rm = view.RootMotion();
        rootDelta = SoldierDeathFall.RotateAboutZ(new Vector3d(rm.X, rm.Y, rm.Z), facing);
        cycled = view.ClipEndedThisTick;
        ReskinPlayerBody(view, model);

        // The caller places the body with the delta straight after; the placement call captures the
        // tick state, so nothing is recorded here off a stale position.
    }

    /// <summary>Places the climbing body without advancing its clip — holds the pose at a rung boundary.</summary>
    /// <param name="feet">Where the body is on the ladder, in engine units.</param>
    /// <param name="facing">Which way it faces — into the ladder.</param>
    public void PlacePlayerBodyClimbing(Vector3d feet, float facing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playerBodyView is not { } view)
        {
            return;
        }

        _playerBodyClimbing = true;
        _playerBodyFacing = facing;
        view.PlaceAt(feet, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));

        // Skin at least once so the body hangs in the climb pose even before the first rung advances
        // -- the animated mesh otherwise still holds the rest pose it was created with. The scratch
        // buffer doubles as the has-ever-been-skinned marker.
        if (_playerBodyMember is { SkinScratch: null } && _playerBodyGroup?.Model is { } model)
        {
            ReskinPlayerBody(view, model);
        }

        CapturePlayerBodyTickState(snap: false);
    }

    /// <summary>Stops showing the climbing body — the player has left the ladder.</summary>
    public void HidePlayerBodyClimbing() => _playerBodyClimbing = false;

    // Re-poses the player body's animated mesh in place. Shared by the death fall and the ladder climb.
    private void ReskinPlayerBody(SoldierView view, MefModel model)
    {
        if (_playerBodyMember is not { } member || _playerBodyGroup is null || !member.Mesh.IsValid)
        {
            return;
        }

        Vertex3D[] skinned = member.SkinScratch ??= new Vertex3D[_playerBodyGroup.Geometry.Vertices.Length];
        _playerBodyGroup.Geometry.Vertices.CopyTo(skinned, 0);
        try
        {
            MefSkinner.Skin(model, view.UnrotatedPose(), (float)(1.0 / WorldScene.UnitsPerMetre), skinned);
        }
        catch (ArgumentException)
        {
            return;
        }

        try
        {
            _device.UpdateAnimatedMesh(member.Mesh, skinned);
        }
        catch (ArgumentException)
        {
            return;
        }

        member.PoseFlippedSinceFrame = true;
    }

    /// <summary>Records where the player's body stands this tick, keeping the tick before.</summary>
    /// <param name="snap">
    /// Whether this placement is a discontinuity — a death, a ladder mount — after which the pair
    /// must not be interpolated across.
    /// </param>
    private void CapturePlayerBodyTickState(bool snap)
    {
        if (_playerBodyView is not { } view)
        {
            return;
        }

        double lift = _playerBodyClimbing ? FootToPelvis(view.UnrotatedPose()) : view.RootSample.Z;
        Vector3d where = new(
            view.Position.X / WorldScene.UnitsPerMetre,
            view.Position.Y / WorldScene.UnitsPerMetre,
            (view.Position.Z + lift) / WorldScene.UnitsPerMetre);

        if (snap || !_playerBodyHasTickState)
        {
            _playerBodyPrevTickWhere = where;
        }
        else if (_playerBodyCaptureTick != _tick)
        {
            _playerBodyPrevTickWhere = _playerBodyTickWhere;
        }

        // A second capture in the same tick only refines the current state; the previous one stands.
        _playerBodyTickWhere = where;
        _playerBodyCaptureTick = _tick;
        _playerBodyHasTickState = true;
    }

    // How far the pose's lowest bone (the feet) sits below the root, in world units — the lift that puts
    // the feet at the body's position. A Z-only facing rotation leaves Z alone, so the pose's local Z is
    // its world Z.
    private static double FootToPelvis(BonePose pose)
    {
        float minZ = 0f;
        foreach (Vector3f p in pose.Positions)
        {
            minZ = Math.Min(minZ, p.Z);
        }

        return -minZ;
    }

    /// <summary>Draws the player's third-person body, when it is active — what the death camera looks at.</summary>
    /// <param name="camera">The eye, which by now is the death camera pulled back off the body.</param>
    /// <param name="projection">The projection to draw with.</param>
    /// <remarks>
    /// Drawn on its own, not in the soldier loop, and only when active — so it never appears in front of
    /// the first-person view during normal play. The body sits at its own position lifted by the death
    /// clip's root height, exactly as a fallen soldier is (see <see cref="Draw"/>).
    /// </remarks>
    public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!(_playerBodyActive || _playerBodyClimbing)
            || _playerBodyView is not { } view
            || _playerBodyMember is not { } member
            || _playerBodyGroup is not { } group)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        // A body is normally lifted by its clip's root height (Human+1300), which for a fall or a stand IS
        // the pelvis height above the feet. But the LADDER climb clip's root track is the CLIMB itself
        // (it rises ~3000 units a cycle and RESETS -- verified against the game: pos+rootSample drops
        // ~2383 at the wrap, yet the game never teleports, so it does NOT lift by rootSample). The vertical
        // is already carried in the body's position; the lift is the pose's own foot-to-pelvis so the feet
        // land on that position and track it as the clip animates. (For a stand or fall this equals
        // RootSample.Z anyway, which is why the corpse still sits on the ground.)
        //
        // The tick capture (CapturePlayerBodyTickState) computes exactly this per tick; frames between
        // ticks draw between the last two captures, the same smoothing a soldier gets.
        double lift = _playerBodyClimbing ? FootToPelvis(view.UnrotatedPose()) : view.RootSample.Z;
        Vector3d where = new(
            view.Position.X / WorldScene.UnitsPerMetre,
            view.Position.Y / WorldScene.UnitsPerMetre,
            (view.Position.Z + lift) / WorldScene.UnitsPerMetre);

        if (_frameSmooth && _playerBodyHasTickState)
        {
            where = PresentationBlend.Lerp(_playerBodyPrevTickWhere, _playerBodyTickWhere, _frameAlpha);
        }

        // The same frame bookkeeping a soldier gets from ResolveFrameState, done here because this
        // body draws once per frame and in no other pass.
        member.PreviousDrawn = member.HasFrameState ? (member.DrawnWhere, member.DrawnOrientation) : null;
        member.DrawnWhere = where;
        member.DrawnOrientation = view.Orientation;
        member.PreviousPoseBlend = member.PoseFlippedSinceFrame ? 1f : member.PoseBlend;
        member.PoseBlend = _frameSmooth ? 1f - _frameAlpha : 0f;
        member.PoseFlippedSinceFrame = false;
        member.NoHistoryThisFrame = !member.HasFrameState;
        member.HasFrameState = true;

        Matrix4f modelView = camera.CreateModelView(where, view.Orientation);
        bool reportMotion = _device.CurrentPass == ScenePass.Geometry;

        _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);
        if (reportMotion && member.PreviousDrawn is { } previous)
        {
            _device.SetPreviousPose(camera.CreateModelView(previous.Where, previous.Orientation));
        }

        if (member.NoHistoryThisFrame)
        {
            _device.SetMaterialOverride(new SurfaceMaterial(NoHistory: true));
        }

        foreach (MeshSubset subset in group.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && group.Instance is { } instance
                && group.Pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = GetTexture(texture);
            }

            _device.DrawMesh(
                member.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }

        if (member.NoHistoryThisFrame)
        {
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }

        _device.ClearPreviousPose();
        _device.ClearPoseInterpolation();
    }

    private void ReleasePlayerBody()
    {
        if (_playerBodyGroup is { } group && group.Mesh.IsValid)
        {
            _device.DestroyMesh(group.Mesh);
        }

        if (_playerBodyMember is { } member && member.Mesh.IsValid)
        {
            _device.DestroyMesh(member.Mesh);
        }

        _playerBodyView = null;
        _playerBodyGroup = null;
        _playerBodyMember = null;
        _playerBodyActive = false;
    }

    /// <summary>Draws the weapon a soldier is holding, at its hand bone's own world transform.</summary>
    /// <param name="member">The soldier.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="reportMotion">Whether this pass records motion — the geometry pass and no other.</param>
    /// <remarks>
    /// <para>
    /// The attachment handlers (<c>0x4600C0</c>, <c>player.md</c> §13) do exactly two things: they add the
    /// object's own position to the bone's, and they <b>copy the bone's rotation whole</b>. There is no
    /// per-weapon offset anywhere in the engine, so the model's own origin is where the hand grips it.
    /// </para>
    /// <para>
    /// The transform itself — hand bone through the body's heading, interpolated between ticks — was
    /// resolved by <see cref="ResolveFrameState"/>. The composed rotation is <c>heading · bone</c>,
    /// applied as <c>M·v</c>, which is the convention
    /// <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/> and the bone palette both use —
    /// <b>not</b> <see cref="Matrix3f.Transform(Vector3f)"/>'s row-vector one, which is its inverse.
    /// </para>
    /// <para>
    /// A gun is rigid, so unlike the body its previous <em>transform</em> reprojects every vertex
    /// exactly; last frame's is reported as its previous pose on the geometry pass.
    /// </para>
    /// </remarks>
    private void DrawWeapon(Member member, in Camera3D camera, bool reportMotion)
    {
        if (member.Weapon is not { } weapon || !member.HasWeaponFrameState)
        {
            return;
        }

        Matrix4f modelView = camera.CreateModelView(member.DrawnWeaponOrigin, member.DrawnWeaponRotation);

        if (reportMotion && member.PreviousWeapon is { } previous)
        {
            _device.SetPreviousPose(camera.CreateModelView(previous.Origin, previous.Rotation));
        }

        foreach (MeshSubset subset in weapon.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && weapon.Instance is { } instance
                && weapon.Pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = GetTexture(texture);
            }

            _device.DrawMesh(
                weapon.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }

        if (reportMotion)
        {
            // The body reports its own previous pose next; the gun's must not leak onto it.
            _device.ClearPreviousPose();
        }
    }

    /// <summary>Offers a round to every soldier and scores it against the nearest one it meets.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>.</param>
    /// <param name="category">The firing weapon's category, which decides how a body it kills falls.</param>
    /// <param name="hit">The nearest soldier struck.</param>
    /// <returns><see langword="false"/> when the round meets nobody.</returns>
    /// <remarks>
    /// <b>The damage is applied here</b>, because the health belongs to the soldier and not to the
    /// round. A soldier already dead is skipped rather than shot again — the original stops a corpse
    /// taking damage through a flag on the human, and this is the same effect with the state that
    /// exists.
    /// </remarks>
    public bool TryShoot(
        Vector3d origin,
        Vector3d end,
        float damageFactor,
        WeaponCategory category,
        out HumanHit hit)
    {
        hit = default;

        // Every soldier in earshot hears the shot, whether or not it hits anyone -- this is the fix for
        // "they hear nothing when I shoot". The range gate lives in AiSoldier.HearGunshot.
        NotifyGunshot(origin, silenced: false);

        HumanHealth? struck = null;
        SoldierView? struckView = null;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView soldier = member.View;
                if (!_health.TryGetValue(soldier, out HumanHealth? health))
                {
                    float maximum = _combat.TryGetValue(soldier, out AiCombatParameters? row)
                        ? row.MaximumHealth
                        : HumanHitZoneTable.MaximumHealth;

                    health = new HumanHealth(HumanHitZoneTable.Soldier, maximum);
                    _health[soldier] = health;
                }

                if (health.IsDead)
                {
                    continue;
                }

                // The facing is the soldier's own declared orientation, whose +Y is forward -- the same
                // column 0x462E50 takes the shot direction out of.
                Matrix3f m = soldier.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);

                // The feet the round is cast at must be the SAME feet the body is drawn on — grounded onto
                // the floor — and the bones sit a root height above them, exactly as the draw lifts the
                // pelvis. Passing the raw declared feet with the pelvis-rooted pose put the hit skeleton a
                // metre (and any un-snapped drop) below the visible guard, so aimed shots missed the zones.
                double snappedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                Vector3d feet = new(soldier.Position.X, soldier.Position.Y, snappedZ);

                // The faithful path: trace the round at the soldier's skinned collision mesh and score it by
                // the bone the round actually went through (BoneDynCubeObj_TraceLinesHandler). Only if the
                // model ships no collision mesh does it fall back to the cylinder-and-nearest-bone stand-in.
                bool met = group.CollisionMesh is { } collision
                    ? HumanHitTest.TryCastMesh(
                        origin, end, feet, facing, collision, soldier.Pose(), soldier.RootSample.Z, out HumanHit candidate)
                    : HumanHitTest.TryCast(
                        origin, end, feet, facing, soldier.Pose(), out candidate, soldier.RootSample.Z);

                if (!met)
                {
                    continue;
                }

                if (struck is null || candidate.Distance < hit.Distance)
                {
                    hit = candidate;
                    struck = health;
                    struckView = soldier;
                }
            }
        }

        if (struck is null)
        {
            return false;
        }

        // The soldier's brain carries two flags the damage handler 0x45D3C0 reads: invulnerability skips
        // the whole damage block (the round still MEETS the body, it just does nothing), and instant-death
        // being disabled makes a head/neck hit take the plain damage factor rather than the ×100 lethal
        // multiplier. Both default off, so an ordinary guard is unaffected.
        AiSoldier? brain = struckView is not null && _brains.TryGetValue(struckView, out AiSoldier? b) ? b : null;

        bool invulnerable = brain?.IsInvulnerable ?? false;
        if (!invulnerable)
        {
            struck.Apply(hit.Zone, damageFactor, brain?.IsInstantDeathDisabled ?? false);

            if (struck.IsDead && struckView is not null)
            {
                Die(struckView, hit, origin, end, category);
            }
        }

        // Deliberately the same shape as the probe's [dmg] line from the retail game (probes.cpp
        // OnDamageHandler), so a run of the port and a capture of the original diff directly. The port
        // resolves the zone from the pose (HumanHitTest.NearestBone) where the engine reads a part index
        // the bullet physics recorded, so a zone that disagrees for the same shot is the one to chase.
        _logger.Information(LogCategory,
            $"[dmg] port zone={hit.Zone} ({HumanDamage.ZoneName(hit.Zone)}) bone={hit.Bone} dmgFactor={damageFactor:0.000} "
            + $"invuln={(invulnerable ? 1 : 0)} instDeathDisabled={(brain?.IsInstantDeathDisabled ?? false ? 1 : 0)} "
            + $"accum={struck.Damage:0.000} max={struck.MaximumHealth:0.000} dead={(struck.IsDead ? 1 : 0)}");

        return true;
    }

    /// <summary>Puts a soldier into the general death state — <c>0x45C440</c>, joined up.</summary>
    /// <remarks>
    /// <para>
    /// Four things happen here and every one of them is read. The body <b>turns to the round</b>
    /// (<c>0x45DAA0</c>); the death <b>animation is chosen</b> from the killing weapon's category, the zone
    /// struck and which way the round came (<see cref="SoldierDeath.Choose"/>); the <b>fall solver</b>
    /// fans its nine rays and returns the corrective velocity (<see cref="SoldierDeathFall"/>); and the
    /// brain is told, so it steps the body every tick from then on instead of freezing it.
    /// </para>
    /// <para>
    /// The soldier's own <c>AIFunction_SetDeathAnimation</c> override is not wired: no shipped script in
    /// level 1 calls it, and the port has nowhere to keep <c>HumanAI+15056</c> yet. Recorded in
    /// <c>TODO.md</c> rather than guessed at.
    /// </para>
    /// </remarks>
    private void Die(SoldierView view, HumanHit hit, Vector3d origin, Vector3d end, WeaponCategory category)
    {
        Matrix3f orientation = view.Orientation;
        Vector3f forward = new(orientation.M12, orientation.M22, orientation.M32);

        Vector3d travel = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        float facing = GammaOf(view);
        bool front = SoldierDeath.TurnToTheShot(travel, forward, ref facing);

        // The engine's own rand(2). Alternating is not the same distribution, but the two retail head-row
        // entries are equal and the two crumple clips are a pair, so what the flip has to do is vary them.
        int flip = _deathsSoFar++ & 1;
        int animation = SoldierDeath.Choose(
            SoldierDeath.ModeFor(category),
            hit.Zone,
            HumanHitZoneTable.Soldier,
            front,
            flip);

        Vector3d fall = default;
        if (animation >= 0 && view.Bones.Set.TryGet(animation, out BoneAnimation clip) && _cast is { } cast)
        {
            fall = SoldierDeathFall.Solve(
                clip,
                view.Position,
                facing,
                (from, to) => cast(from, to, out WorldRayHit blocked)
                    ? new SoldierDeathRay(true, blocked.Position, Length(from, blocked.Position))
                    : default);
        }

        view.PlaceAt(view.Position, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));
        view.Kill(animation);

        if (_brains.TryGetValue(view, out AiSoldier? brain))
        {
            brain.Kill(fall, facing);
        }
    }

    /// <summary>Gets a delegate that offers rounds to these soldiers.</summary>
    /// <returns>The delegate the player takes.</returns>
    public HumanShot Shooter() => TryShoot;

    /// <summary>Gets how many soldiers have been killed.</summary>
    public int Killed
    {
        get
        {
            int dead = 0;
            foreach (HumanHealth health in _health.Values)
            {
                if (health.IsDead)
                {
                    dead++;
                }
            }

            return dead;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();

        foreach (TextureHandle texture in _textures.Values)
        {
            _device.DestroyTexture(texture);
        }

        _textures.Clear();
    }

    private Group? BuildGroup(LoadedMission mission, string modelName, SoldierView posed)
    {
        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            _logger.Warning(LogCategory, $"'{posed.Soldier.Name}' names model '{modelName}', which no pack holds.");
            return null;
        }

        if (!MefSkinner.IsSkinned(model))
        {
            _logger.Warning(LogCategory, $"'{modelName}' is {model.Format}, not a skinned model.");
            return null;
        }

        MeshGeometry? geometry = MefMeshBuilder.Build(model, 1f);
        if (geometry is null)
        {
            _logger.Warning(LogCategory, $"'{modelName}' has nothing to draw.");
            return null;
        }

        Vertex3D[] skinned = [.. geometry.Vertices];
        int written;
        try
        {
            // The unrotated pose: the facing is applied per soldier in the model matrix instead.
            written = MefSkinner.Skin(model, posed.UnrotatedPose(), (float)(1.0 / WorldScene.UnitsPerMetre), skinned);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(LogCategory, $"'{modelName}' could not be posed: {ex.Message}");
            return null;
        }

        // The lowest point of the posed skin, in metres below the skeleton root. Z rotation cannot
        // change it, so it is a property of the (model, skeleton, animation) triple, and it is what
        // Stand() rests on the ground. Only the base vertices count - the array's tail holds the
        // extra-influence records, which no draw record indexes.
        float lowest = 0f;
        for (int i = 0; i < written; i++)
        {
            lowest = Math.Min(lowest, skinned[i].Position.Z);
        }

        MeshHandle mesh;
        try
        {
            mesh = _device.CreateMesh(skinned, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"'{modelName}' did not upload: {ex.Message}");
            return null;
        }

        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
        return new Group(geometry, mesh, instance, pack, lowest) { Model = model };
    }

    private static IEnumerable<HumanSoldierTask> Walk(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            // A soldier inside a ConditionalContainer that is not running is not registered into the world
            // -- the original creates the task but never fires its DynCubeObj register event until the
            // scene runs. Level 1's intro and outro cut-scenes park several Jones-model soldiers this way
            // (the "Jones drive truck" outro actor among them); without this the port drew them standing in
            // the base at mission start. See ConditionalContainerGate.
            if (ConditionalContainerGate.IsDormant(child))
            {
                continue;
            }

            if (child is HumanSoldierTask soldier)
            {
                yield return soldier;
            }

            foreach (HumanSoldierTask deeper in Walk(child))
            {
                yield return deeper;
            }
        }
    }

    private static (LoadedPack? Pack, MtpInstance? Instance) FindInstance(LoadedMission mission, string name)
    {
        foreach (LoadedPack pack in new[] { mission.LevelPack, mission.CommonPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return (pack, instance);
                }
            }
        }

        return (null, null);
    }

    private void Release()
    {
        foreach (Group group in _groups)
        {
            if (group.Mesh.IsValid)
            {
                _device.DestroyMesh(group.Mesh);
            }

            // Each soldier owns a mesh of its own now, and every one of them has to go back.
            foreach (Member member in group.Soldiers)
            {
                if (member.Mesh.IsValid)
                {
                    _device.DestroyMesh(member.Mesh);
                }
            }
        }

        foreach (HeldWeapon weapon in _weapons)
        {
            if (weapon.Mesh.IsValid)
            {
                _device.DestroyMesh(weapon.Mesh);
            }
        }

        _weapons.Clear();
        _groups.Clear();
        _health.Clear();
        ReleasePlayerBody();
        Count = 0;
    }

    private TextureHandle GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = TextureHandle.None;
        if (_resources.TryLoad(LoadedPack.TextureResourceName(name), out ReadOnlyMemory<byte> bytes))
        {
            try
            {
                TextureImage image = TexFile.Load(bytes.Span);
                handle = _device.CreateTexture(
                    new TextureDescription(
                        image.Width,
                        image.Height,
                        image.MipLevels,
                        TextureFilter.Linear,
                        TextureAddressMode.Repeat),
                    image.Pixels);
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Texture '{name}' did not load: {ex.Message}");
            }
        }

        _textures[name] = handle;
        return handle;
    }

    private sealed class Group(MeshGeometry geometry, MeshHandle mesh, MtpInstance? instance, LoadedPack? pack, float lowestVertexZ)
    {
        public MeshGeometry Geometry { get; } = geometry;

        public MeshHandle Mesh { get; set; } = mesh;

        public MtpInstance? Instance { get; } = instance;

        public LoadedPack? Pack { get; } = pack;

        public float LowestVertexZ { get; } = lowestVertexZ;

        public List<Member> Soldiers { get; } = [];

        /// <summary>The model every soldier in the group draws, kept so the group can be re-skinned each tick.</summary>
        public MefModel? Model { get; set; }

        private MefCollisionMesh? _collisionMesh;
        private bool _collisionMeshRead;

        /// <summary>The model's skinned collision mesh, read once — the geometry a round is scored against.</summary>
        public MefCollisionMesh? CollisionMesh
        {
            get
            {
                if (!_collisionMeshRead)
                {
                    _collisionMeshRead = true;
                    if (Model is not null && MefCollisionMesh.TryRead(Model.Container, out MefCollisionMesh? read))
                    {
                        _collisionMesh = read;
                    }
                }

                return _collisionMesh;
            }
        }
    }

    /// <summary>
    /// One soldier drawn from the group's geometry, with <b>its own skinned mesh</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group used to share a single skinned mesh, posed from its first member. That was only ever
    /// defensible while every soldier in a group ran the same clip from the same start; once they were given
    /// real patrol paths they diverged, and the whole group rendered whichever pose the representative
    /// happened to be in. With most guards standing and a few walking, that read as <b>no soldier
    /// animating at all</b>.
    /// </para>
    /// <para>
    /// Hooking the original's skinner (<c>0x49F700</c>) settles what it does: it is called through a
    /// stack-allocated descriptor about once per soldier per frame, so the original skins <b>every soldier
    /// separately, every frame</b>. This does the same. It costs one mesh upload per soldier per tick
    /// instead of one per group, which is the price of each soldier having its own pose.
    /// </para>
    /// </remarks>
    private sealed class Member(SoldierView view, Vector3d position, MeshHandle mesh)
    {
        public SoldierView View { get; } = view;

        public Vector3d Position { get; } = position;

        public MeshHandle Mesh { get; set; } = mesh;

        /// <summary>What this soldier holds, or <see langword="null"/> when the level armed it with nothing.</summary>
        public HeldWeapon? Weapon { get; set; }

        /// <summary>The weapon's table row, which the AI reads its clip size and burst length from.</summary>
        public WeaponType? WeaponType { get; set; }

        /// <summary>How many rounds are in the clip. The AI watches this change to count its burst.</summary>
        public int AmmoInClip { get; set; }

        /// <summary>Ticks left before the weapon's cadence lets another round out.</summary>
        public int FireCooldown { get; set; }

        /// <summary>Ticks left of a reload.</summary>
        public int ReloadTicks { get; set; }

        /// <summary>The persistent buffer this soldier's pose is skinned into, allocated once.</summary>
        public Vertex3D[]? SkinScratch { get; set; }

        /// <summary>Whether the animated mesh took a new pose since the last frame's bookkeeping.</summary>
        /// <remarks>
        /// When it did, the previous-pose stream now holds what was on screen last frame, and the
        /// blend that described last frame is expressed against the OLD stream pair — see
        /// <see cref="ResolveFrameState"/> for how each case is reported.
        /// </remarks>
        public bool PoseFlippedSinceFrame { get; set; }

        // ---- Captured on the simulation tick: the two states the presentation draws between. ----

        /// <summary>Whether the tick states below have been captured at least once.</summary>
        public bool HasTickState { get; set; }

        /// <summary>Where the body is drawn for the current tick, in metres.</summary>
        public Vector3d TickWhere { get; set; }

        /// <summary>Where it was drawn the tick before.</summary>
        public Vector3d PrevTickWhere { get; set; }

        /// <summary>This tick's world 3×3, verbatim — what an unblended frame draws, to the bit.</summary>
        public Matrix3f TickOrientation { get; set; }

        /// <summary>This tick's heading in radians, for interpolating.</summary>
        public float TickFacing { get; set; }

        /// <summary>The tick before's heading.</summary>
        public float PrevTickFacing { get; set; }

        /// <summary>Whether the hand-bone states below are captured (only armed soldiers need them).</summary>
        public bool HasHandState { get; set; }

        /// <summary>The hand bone's position in body space this tick, in world units.</summary>
        public Vector3f TickHandPosition { get; set; }

        /// <summary>The tick before's.</summary>
        public Vector3f PrevTickHandPosition { get; set; }

        /// <summary>The hand bone's rotation this tick.</summary>
        public Matrix3f TickHandRotation { get; set; } = Matrix3f.Identity;

        /// <summary>The tick before's.</summary>
        public Matrix3f PrevTickHandRotation { get; set; } = Matrix3f.Identity;

        // ---- Resolved once per rendered frame by ResolveFrameState; every pass draws from these. ----

        /// <summary>Whether a frame has been resolved at all.</summary>
        public bool HasFrameState { get; set; }

        /// <summary>Where the body is drawn this frame — between the two tick states when smoothing.</summary>
        public Vector3d DrawnWhere { get; set; }

        /// <summary>Its world 3×3 this frame.</summary>
        public Matrix3f DrawnOrientation { get; set; } = Matrix3f.Identity;

        /// <summary>Where it was drawn LAST frame — the rigid half of its motion vector.</summary>
        public (Vector3d Where, Matrix3f Orientation)? PreviousDrawn { get; set; }

        /// <summary>This frame's share of the previous-pose vertex stream in the drawn position.</summary>
        public float PoseBlend { get; set; }

        /// <summary>The share that reconstructs LAST frame's position from the same streams.</summary>
        public float PreviousPoseBlend { get; set; }

        /// <summary>Whether this frame is the body's first — nothing on screen is its history.</summary>
        public bool NoHistoryThisFrame { get; set; }

        /// <summary>Whether the weapon transform below is resolved.</summary>
        public bool HasWeaponFrameState { get; set; }

        /// <summary>The held weapon's world origin this frame, in metres.</summary>
        public Vector3d DrawnWeaponOrigin { get; set; }

        /// <summary>Its world rotation this frame — heading times hand bone.</summary>
        public Matrix3f DrawnWeaponRotation { get; set; } = Matrix3f.Identity;

        /// <summary>Last frame's weapon transform — a rigid model's exact previous pose.</summary>
        public (Vector3d Origin, Matrix3f Rotation)? PreviousWeapon { get; set; }
    }

    /// <summary>
    /// One weapon model, uploaded once and drawn by every soldier carrying that weapon.
    /// </summary>
    /// <remarks>
    /// Unlike a body, a gun is a rigid model: it is not skinned and its pose never changes, so a single
    /// mesh serves every soldier and only the transform differs. That transform is the hand bone's, which
    /// is why the body mesh has to be posed but this one does not.
    /// </remarks>
    private sealed class HeldWeapon(MeshGeometry geometry, MeshHandle mesh, MtpInstance? instance, LoadedPack? pack)
    {
        public MeshGeometry Geometry { get; } = geometry;

        public MeshHandle Mesh { get; } = mesh;

        public MtpInstance? Instance { get; } = instance;

        public LoadedPack? Pack { get; } = pack;
    }
}

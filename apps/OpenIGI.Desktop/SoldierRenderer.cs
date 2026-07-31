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
    private readonly HashSet<string> _reportedChannels = new(StringComparer.Ordinal);
    private int _deathsSoFar;
    private int _walkAnimation = -1;
    private int _tick;
    private int _travelSamples;
    private double _travelTotal;
    private double _travelMax;
    private bool _disposed;

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

            // Its own mesh, so it can hold its own pose (see Member).
            MeshHandle own;
            try
            {
                own = _device.CreateMesh(group.Geometry.Vertices, group.Geometry.Indices);
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
        if (graphs.Count == 0)
        {
            _logger.Warning(LogCategory, "The level ships no nav graph, so no soldier can patrol.");
            return 0;
        }

        // The walk clip is the locomotion table's forward-travelling state (state 1 travels +Y), read from
        // 0x538870 -- not a chosen id.
        _walkAnimation = HumanLocomotionStates.For(1).AnimationId;

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

                // Each soldier walks the graph ITS OWN HumanAI names, not the level's biggest one.
                HumanAiTask? brainTask = FindMind(view);
                int graphId = brainTask?.GraphId ?? 0;
                if (!graphs.TryGetValue(graphId, out GraphFile? graph) || graph.RouteTable is null)
                {
                    if (brainTask is not null)
                    {
                        unroutable++;
                    }

                    index++;
                    continue;
                }

                AiSoldier brain = new(graph, view.Position, GammaOf(view), index & 3, index);
                brain.SetPatrolPaths(paths);
                brain.SetGroundQuery(MakeGroundQuery());
                graphUse[graphId] = graphUse.GetValueOrDefault(graphId) + 1;
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

        _logger.Information(
            LogCategory,
            $"{_brains.Count} soldier(s) with brains ({patrolling} scripted, {perTask} from their own " +
            $"MISSION:AI script), walk animation {_walkAnimation}.");
        _logger.Information(
            LogCategory,
            $"Soldiers per nav graph: " +
            string.Join(", ", graphUse.OrderBy(p => p.Key).Select(p => $"graph{p.Key}:{p.Value}")) +
            (unroutable > 0 ? $"; {unroutable} soldier(s) name a graph the level does not ship" : string.Empty));
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

    /// <summary>Which locomotion state a fighting soldier is in.</summary>
    /// <remarks>
    /// <para>
    /// The body's clip comes from the <b>soldier's own state-to-animation table</b> and nowhere else.
    /// An earlier pass played the weapon table's <c>Stand</c>, <c>Fire</c> and <c>Reload Animation</c>
    /// ids on a soldier's rig, which was a guess and a wrong one: those ids index the <i>player's</i>
    /// animation set, and the same number in a soldier's set is a different clip entirely. Guards threw
    /// themselves on the ground, got up and did it again for ever.
    /// </para>
    /// <para>
    /// The three states here were <b>measured in a live retail mission</b>: a guard's body sits in state
    /// 6 while its burst counter drains, state 12 the moment that counter reaches zero, and state 0
    /// <i>only</i> when its target is out of sight. Through the table those read as animations 56, 55
    /// and 2. What performs the transition is the soldier's own state machine reacting to the input
    /// slots the AI writes, which the port does not run — so this reproduces the correlation and says so.
    /// </para>
    /// </remarks>
    private static int CombatState(AiSoldier brain)
    {
        // Out of sight is the ONLY thing the capture ever showed drop a fighting guard back to standing.
        if (!brain.SeesEnemy)
        {
            return AiCombatChannels.StateOf(brain.Type, AiStanceChannel.Standing);
        }

        // A live burst is state 6; a spent one -- and a reload, which is a spent one that stays spent --
        // is state 12. Reading "burst spent" as standing instead put a single tick of the stand clip
        // between every two bursts, and back-to-back bursts made that a flicker.
        return brain.RoundsLeftInBurst > 0
            ? HumanSoldierStates.FiringState
            : HumanSoldierStates.ReloadState;
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
        }

        if (seeing == 0 && engaging == 0 && RoundsFired == 0)
        {
            return;
        }

        string report = $"{seeing} soldier(s) see the player, {engaging} engaging, {RoundsFired} round(s) fired.";
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
    private void FireIfAsked(SoldierView view, AiSoldier brain, Vector3d enemy)
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

        _onRound?.Invoke(brain.EyePosition(), enemy, brain.HitProbability, weapon);

        // Every other soldier near the shot hears it -- the same AIEVENT_GUNSHOT a player's shot raises.
        NotifyGunshot(brain.EyePosition(), silenced: false, except: view);
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

                if (_brains.TryGetValue(member.View, out AiSoldier? brain))
                {
                    Member held = member;
                    brain.SetCombat(type, row, () => new AiCombatWeaponState(
                        held.AmmoInClip,
                        held.WeaponType?.RoundsPerClip ?? 0,
                        held.WeaponType?.MaximumRoundsPerBurst ?? 0,
                        held.WeaponType?.Category ?? WeaponCategory.Knife));
                }
            }
        }

        _logger.Information(
            LogCategory,
            $"AI: {bound} soldier(s) configured at {HumanAiConfigRegistry.NameOf(difficulty)} ({difficulty}).");
    }

    /// <summary>The <c>HumanAI</c> child a soldier's body carries, if the level gave it one.</summary>
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
    public void Tick(Vector3d enemy = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _tick++;

        // EVERY soldier's rig is advanced, not just each group's representative. Ticking only the
        // representative left every other soldier's BoneObject frozen, so its RootMotion() was zero and the
        // AI never moved it -- while the shared group mesh, posed from the representative, showed it walking
        // on the spot. Exactly one soldier per group actually moved: that group's representative.
        foreach ((SoldierView view, AiSoldier _) in _brains)
        {
            view.Tick();
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

            brain.Tick(_tick, enemy, travel);
            FireIfAsked(view, brain, enemy);
            ReportCombat();
            ReportChannels(brain);

            // A patrol ANIMATION command names a clip outright; a soldier in a fight plays its WEAPON's
            // own clips; otherwise the gait picks between the walk cycle and the soldier's stand
            // animation.
            if (brain.RequestedAnimation >= 0)
            {
                view.SetMoving(true, brain.RequestedAnimation);
            }
            else if (brain.Activity != AiSoldierActivity.Engaging
                || !view.SetMoving(true, HumanSoldierStates.AnimationOf(CombatState(brain))))
            {
                view.SetMoving(brain.Activity == AiSoldierActivity.Patrolling, _walkAnimation);
            }

            view.PlaceAt(brain.Position, brain.Orientation);
        }

        // Each soldier is skinned from ITS OWN rig, which is what the original does (0x49F700 is called
        // through a stack descriptor about once per soldier per frame). Sharing one skinned mesh per group
        // made every soldier wear the first one's pose, so once guards diverged onto real patrol paths
        // nothing appeared to animate at all.
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

                Vertex3D[] skinned = [.. group.Geometry.Vertices];
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

                MeshHandle replacement;
                try
                {
                    replacement = _device.CreateMesh(skinned, group.Geometry.Indices);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (member.Mesh.IsValid)
                {
                    _device.DestroyMesh(member.Mesh);
                }

                member.Mesh = replacement;
            }
        }
    }

    /// <summary>Draws every soldier.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform — soldiers use no other.</param>
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

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView soldier = member.View;
                Vector3d metres = member.Position;

                // A dead soldier keeps being drawn -- a body stays on the ground in the original, and
                // vanishing was only ever the port having no death animation to play. Each soldier now
                // carries its own skinned mesh, so a corpse is posed by its own death clip.

                // A soldier with a brain is drawn where its AI put it, lifted by the CLIP'S OWN ROOT
                // HEIGHT. That is the original's Human+1300 (see SoldierView.RootSample): every retail
                // skeleton's bone 0 rest translation is zero, so the pose puts the pelvis at the object
                // origin and the feet a metre below it, and the root track's Z is what bridges the two.
                //
                // It replaces "rest the skin's lowest vertex on the ground", which was a recorded
                // stand-in for exactly this and agrees with it to a couple of centimetres while a
                // soldier is standing. The difference is a corpse: a death clip takes its root from
                // 3877 units down to about 600, and the stand-in had no way to know.
                //
                // Only the Z is taken. The horizontal part of the root track is what MOVES the body -
                // the mover consumes the same track's per-tick delta - so adding it again would run the
                // model a stride ahead of its own feet every cycle. A heading is a rotation about Z, so
                // dropping the horizontal part costs nothing in the vertical.
                Vector3d where;
                if (_brains.TryGetValue(soldier, out AiSoldier? soldierBrain) && soldierBrain.HasGroundQuery)
                {
                    where = new Vector3d(
                        soldier.Position.X / WorldScene.UnitsPerMetre,
                        soldier.Position.Y / WorldScene.UnitsPerMetre,
                        (soldier.Position.Z + soldier.RootSample.Z) / WorldScene.UnitsPerMetre);
                }
                else if (_brains.ContainsKey(soldier))
                {
                    where = new Vector3d(
                        soldier.Position.X / WorldScene.UnitsPerMetre,
                        soldier.Position.Y / WorldScene.UnitsPerMetre,
                        metres.Z);
                }
                else
                {
                    where = metres;
                }

                Matrix4f modelView = camera.CreateModelView(where, soldier.Orientation);

                DrawWeapon(member, where, camera);

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
            }
        }
    }

    /// <summary>Draws the weapon a soldier is holding, at its hand bone's own world transform.</summary>
    /// <param name="member">The soldier.</param>
    /// <param name="where">Where the body is drawn, in metres — the same place its skin goes.</param>
    /// <param name="camera">The eye.</param>
    /// <remarks>
    /// <para>
    /// The attachment handlers (<c>0x4600C0</c>, <c>player.md</c> §13) do exactly two things: they add the
    /// object's own position to the bone's, and they <b>copy the bone's rotation whole</b>. There is no
    /// per-weapon offset anywhere in the engine, so the model's own origin is where the hand grips it.
    /// </para>
    /// <para>
    /// The pose is the unrotated one, for the same reason the body's skin uses it — the heading is a
    /// rotation about the root, so it can be left in the model matrix and applied to bone and vertex
    /// together. The composed rotation is <c>heading · bone</c>, and it is applied as <c>M·v</c>, which is
    /// the convention <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/> and the bone palette both
    /// use — <b>not</b> <see cref="Matrix3f.Transform(Vector3f)"/>'s row-vector one, which is its inverse.
    /// </para>
    /// </remarks>
    private void DrawWeapon(Member member, Vector3d where, in Camera3D camera)
    {
        if (member.Weapon is not { } weapon)
        {
            return;
        }

        BonePose pose = member.View.UnrotatedPose();
        if (HumanAttachPoints.ThirdPersonWeapon >= pose.BoneCount)
        {
            return;
        }

        Matrix3f heading = member.View.Orientation;
        Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];

        // M . v, so the transpose of the row-vector Transform.
        Vector3f turned = heading.Transposed().Transform(hand);
        Vector3d origin = new(
            where.X + (turned.X / WorldScene.UnitsPerMetre),
            where.Y + (turned.Y / WorldScene.UnitsPerMetre),
            where.Z + (turned.Z / WorldScene.UnitsPerMetre));

        Matrix4f modelView = camera.CreateModelView(
            origin,
            heading * pose.Rotations[HumanAttachPoints.ThirdPersonWeapon]);

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

                if (!HumanHitTest.TryCast(
                        origin, end, soldier.Position, facing, soldier.Pose(), out HumanHit candidate))
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
            $"[dmg] port zone={hit.Zone} bone={hit.Bone} dmgFactor={damageFactor:0.000} "
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

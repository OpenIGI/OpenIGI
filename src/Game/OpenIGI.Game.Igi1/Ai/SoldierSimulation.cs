using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The level's soldiers as the game runs them: their brains, their health and weapons, what they hear and see, and
/// the player's own body where it is a soldier's.
/// </summary>
/// <remarks>
/// <para>
/// What draws them is <c>SoldierRenderer</c>, which holds one of these and reads it; this holds nothing it draws. The
/// soldiers are walked through <see cref="Roster"/>, group by group in the order the level made them, which is the
/// order their brains tick in.
/// </para>
/// <para>
/// This is the coordinator: it runs each soldier's update in the original's order and hands the parts of it to the
/// pieces that own them -- the brains' making (<see cref="SoldierBrainBinder"/>), what the soldiers see
/// (<see cref="SoldierSightQueries"/>) and hear (<see cref="SoldierNoiseBroadcaster"/>), their weapons
/// (<see cref="SoldierWeaponFire"/>) and rounds (<see cref="SoldierGunShotRounds"/>), what hits do to them
/// (<see cref="SoldierDamage"/>) and the generators that replace them (<see cref="GuardGeneratorSpawner"/>). All of
/// them share one <see cref="SoldierContext"/>, so every part reads the same roster, brains and random stream.
/// </para>
/// </remarks>
public sealed partial class SoldierSimulation : IHumanEvents
{
    /// <summary>The damage factor a knife stab carries into a soldier with no fight on (<c>0x46DFB0</c>: <c>0x447A0000</c>).</summary>
    public const float KnifeUnawareDamageFactor = 1000f;

    /// <summary>HumanAI+15680: how long a dead body stays after its death clip before it may leave the world, in ticks.</summary>
    /// <remarks>The constructor (<c>0x44F060</c>) writes 300 and nothing else in the image writes it.</remarks>
    public const int GeneratedBodyLingerTicks = Igi1HumanAi.BodyStayTicks;

    private const string LogCategory = SoldierContext.LogCategory;

    private readonly IOpenIgiLogger _logger;

    private readonly SoldierContext _context;

    private readonly SoldierNoiseBroadcaster _noise;

    private readonly SoldierSightQueries _sight;

    private readonly SoldierBinocularScan _binoculars;

    private readonly SoldierBodyQueries _bodies;

    private readonly SoldierCutsceneStaging _staging;

    private readonly SoldierFootsteps _footsteps;

    private readonly SoldierDamage _damage;

    private readonly SoldierGunShotRounds _rounds;

    private readonly SoldierWeaponFire _fire;

    private readonly GuardGeneratorSpawner _generators;

    private readonly SoldierBrainBinder _brains;

    private readonly SoldierRagdolls _ragdolls;

    private readonly SoldierDiagnostics _diagnostics;

    private readonly SoldierPlayerBody _playerBody = new();

    /// <summary>Gets the player's body's state, which its renderer and the soldiers' code share.</summary>
    internal SoldierPlayerBody PlayerBodyState => _playerBody;

    private readonly HashSet<SoldierView> _combatClipMissing = [];

    private readonly HashSet<GameTask> _unwalkedSoldiers = new(ReferenceEqualityComparer.Instance);

    private bool _disposed;

    private bool _alarmWasOn;

    private GameTask? _levelRoot;
    private IReadOnlyDictionary<int, GraphFile>? _graphs;

    // The level walk this renderer's tasks update from, once AttachTo has put them in it; null when standalone.
    private LevelUpdateWalker? _walker;

    /// <summary>Initializes a new instance of the <see cref="SoldierSimulation"/> class.</summary>
    /// <param name="logger">Where the soldiers report.</param>
    public SoldierSimulation(IOpenIgiLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _context = new SoldierContext(_logger);

        // The parts play a script's sound through whatever sink is set when it plays, not the one set when they were made.
        Action<string, Vector3d, Func<Vector3d>?> scriptSound = (name, at, follow) => ScriptSound?.Invoke(name, at, follow);
        _noise = new SoldierNoiseBroadcaster(_context);
        _sight = new SoldierSightQueries(_context, scriptSound);
        _binoculars = new SoldierBinocularScan(_context);
        _bodies = new SoldierBodyQueries(_context);
        _staging = new SoldierCutsceneStaging(_context);
        _footsteps = new SoldierFootsteps(_context);
        _damage = new SoldierDamage(_context, _footsteps, scriptSound);
        _footsteps.LandSound = (sound, at) => scriptSound(sound, at, null);
        _rounds = new SoldierGunShotRounds(_context, _sight, _noise);
        _fire = new SoldierWeaponFire(_context, _rounds, _noise);
        _generators = new GuardGeneratorSpawner(_context, _sight, Enlist);
        _brains = new SoldierBrainBinder(_context, _sight, Enlist);
        _ragdolls = new SoldierRagdolls(_context);
        _diagnostics = new SoldierDiagnostics(_context);
    }

    /// <summary>Gets or sets where the player is, read by each soldier as it updates (<see cref="BeginTick"/>).</summary>
    internal Func<SoldierEnemy>? Enemy { get; set; }

    /// <summary>Gets where the soldier updating now stands, which its enemy is read for: of a co-op session's players, the nearest.</summary>
    public Vector3d TickingSoldierPosition { get; private set; }

    /// <summary>Gets the bodies that have updated this tick, so none updates twice and the tick's close can find any the walk missed.</summary>
    internal HashSet<SoldierView> Ticked { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets or sets whether this tick's diagnostics are collected, decided once as it opens.</summary>
    internal bool CollectDiagnostics { get; set; }

    /// <summary>Gets or sets the tick the last detection shout was sounded on, or -1 for none this mission.</summary>
    internal int LastDetectionShoutTick { get; set; } = -1;

    /// <summary>Marks the simulation as gone with its renderer.</summary>
    internal void Retire() => _disposed = true;

    /// <summary>One soldier's update: its rig, then its AI and movement.</summary>
    internal void TickSoldier(SoldierView view, Igi1HumanAi brain)
    {
        if (!ConditionalContainerGate.IsTaskActive(view.Soldier) || !Ticked.Add(view))
        {
            return;
        }

        if (TickPuppet(view))
        {
            return;
        }

        TickingSoldierId = view.Soldier.Id;
        TickingSoldierPosition = view.Position;

        bool collectDiagnostics = CollectDiagnostics;
        SoldierEnemy enemy = Enemy?.Invoke() ?? default;
        bool enemyAlive = enemy.Alive;
        bool enemyCrouching = enemy.Crouching;

        // Vision watches the mark; combat aims at the feet + 0.5 m (sub_451380). With no body to speak of,
        // the point given is the mark itself.
        Vector3d feet = enemy.Feet ?? enemy.Eye;
        Vector3d playerMark = enemy.Feet is null
            ? enemy.Eye
            : new Vector3d(
                feet.X,
                feet.Y,
                feet.Z + Igi1HumanAi.DetectionMarkAboveFeet(enemyCrouching));

        // Every soldier's own rig advances (a group's shared mesh is only the model) -- after the origin it leaves is noted.
        if (_context.FindAgent(view) is { } ticking)
        {
            ticking.OriginBeforeTick = SoldierContext.OccupantOrigin(view);
        }

        view.Tick();

        // Read here, not after the clip selection below: the event belongs to the interval this
        // Tick just advanced, and SetMoving may replace the clip further down. A fresh BoneClip has
        // no interval yet, so asking afterwards would quietly swallow the step. It is REPORTED after the
        // brain, though: the HumanAI is a child the HumanSoldier's update runs first, and the step's pitch
        // (0x45A7F0, at the end of the Human's own 0x45D080) is drawn from the level's stream after
        // whatever that AI drew -- on the probe each walker's draws went action delay, then step.
        BoneClip? clipBefore = view.Bones.SourceOf(0);
        int animationBefore = view.CurrentAnimation;
        if (!view.IsDead && view.Bones.SourceOf(0) is { } bone0)
        {
            PlayClipEvents(view, brain, bone0);
        }

        // Child Train collision starts at its authored placeholder and receives its derived consist
        // transform before this update. Retry registration here so the first live mesh pose can reproduce
        // retail's pre-scene Human_SetParent call. This is deliberately before animation root motion: the
        // authored Human position is the parent origin.
        SoldierAgent? member = _context.FindAgent(view);
        if (member is { IsCutsceneActor: true })
        {
            _staging.AttachStagedActor(member);
        }

        // The AI runs second, so the root motion it is handed is this tick's, not last tick's. A clip that ran out in
        // this advance is told to the brain only after it has run, though: the original's body machine leaves the
        // animation state in the Human's update, after its AI, so the AI sees it on the next tick. On the probe mission
        // 4's patrol 411 stood one tick after its clip 50 ended before taking its next command; telling the brain at
        // once had it walk off a tick early.
        bool requestedClipEnded = brain.RequestedAnimation >= 0 && view.Bones.SourceOf(0)?.EndedThisTick == true;

        if (member is { IsCutsceneActor: true })
        {
            RideStagedGround(view, brain, member);
        }

        if (view.IsDead)
        {
            TickCorpse(view, brain);
            return;
        }

        (double travel, double lateral) = ClipTravel(view, brain, collectDiagnostics);

        // The alarm the soldier watches is the one ITS OWN +15044 names -- resolved per AI, as the
        // retail per-tick resolves the control task each tick. First sight of an id lazily makes
        // the state, since the ids arrive only once the CREATE dispatch has run the script.
        bool alarmOn = false;
        bool alarmWasOn = false;
        if (brain.AlarmControlId != -1)
        {
            if (AlarmControls?.TryGet(brain.AlarmControlId, out AiAlarmControlState? watched) == true)
            {
                alarmOn = watched!.IsOn;
                alarmWasOn = watched.WasOn;
            }
        }

        // The detection scan is offered every human -- the player and every soldier -- and the brain
        // fights whoever the event that started its fight named; it aims at that human.
        //
        // The brain is given the level's counter as the original reads it during a tick (Level+52), which is
        // incremented AFTER the tick's handlers: 0 on the level's first tick, where this simulation's own count is
        // already 1. Everything a brain times by the counter's value rather than by a difference -- its sweep
        // phase (counter mod 4), its first tick's CREATE -- lands on retail's tick only with this. Probed on
        // mission 4: level-start patrols dispatched CREATE on counter 0, and the squad's casts ran one tick later,
        // by count, than the port's had.
        brain.HumanDead = _context.HealthOf(view).IsDead;
        brain.TickAmong(
            TickCount - 1,
            _sight.CandidatesFor(view, brain, feet, playerMark, enemyAlive, enemyCrouching),
            travel,
            alarmOn,
            alarmWasOn,
            lateral);
        Vector3d aimFeet = brain.TargetFeet;
        bool aimCrouching = brain.TargetCrouching;

        // The machine sees the dead bit the killing round set and enters the death state (0x45C440). The entry only
        // starts the clip: the death state's per-tick (0x45C700), which moves the body, runs from the next tick.
        if (_damage.TryDieOfPendingRound(view))
        {
            view.PlaceAt(brain.Position, view.Orientation);
            if (_context.FindAgent(view) is { } fallen)
            {
                fallen.FiredThisTick = false;
                WeaponSound?.Invoke(brain.RuntimeId, false, fallen.WeaponType, brain.Position);
            }

            return;
        }

        if (member is not null && collectDiagnostics && brain.Arm == AiCombatArm.Gunner)
        {
            _diagnostics.ReportGunner(brain, ScriptVariables);
        }

        if (collectDiagnostics)
        {
            _diagnostics.ReportDrained(brain);
        }

        if (member is not null)
        {
            member.FiredThisTick = false;
        }

        int? combatState = TickBody(view, brain, collectDiagnostics);
        ReleaseOnClipEvents(view, brain, member, clipBefore, combatState);
        SoldierClipSelection.Play(view, brain, combatState, _combatClipMissing);
        SoldierClipSelection.StepBody(view, brain, clipBefore, animationBefore, travel, lateral);
        FireAndSound(view, brain, member, aimFeet, aimCrouching);
        if (requestedClipEnded)
        {
            brain.ReportAnimationFinished();
        }

        // The step, after the brain and after the clip this tick chose, from the clip playing now (0x45A7F0 takes only
        // an event of the current animation): the interval the original's clock has just covered, which this clock
        // covers next (BoneClip.UpcomingEvent) -- and on the tick a clip starts, its start too. On the probe mission
        // 4's patrols stepped on the tick they set off (tick 1) and every 17 ticks after.
        if (!view.IsDead
            && view.Bones.SourceOf(0) is { } clipNow
            && (clipNow.UpcomingEvent(FootstepSounds.EventId)
                || (!ReferenceEquals(clipNow, clipBefore) && clipNow.HasEventAtCurrentTime(FootstepSounds.EventId))))
        {
            _footsteps.ReportFootstep(view, brain.Body.CrouchByte);
        }

        if (brain.Body.LandedThisTick)
        {
            _footsteps.ReportLanding(view);
        }

        view.PlaceAt(brain.Position, brain.Orientation);

        // The body turns to look along the view's pitch, recoil and all (0x489F70), while its machine leaves the look on.
        view.LookAlong(brain.Body.LooksAlongView ? brain.ViewPitch + brain.RecoilPitch : 0f);
    }

    /// <summary>Gives every soldier an AI that patrols the level's nav graph and reacts to the player.</summary>
    /// <param name="graphs">
    /// The level's nav graphs, keyed by the id of the <c>AIGraph</c> task that loads each. A soldier is
    /// bound to the one its own <c>HumanAI</c>'s <c>Graph ID</c> names; one that names a graph the level
    /// does not ship gets no brain rather than being routed over somebody else's node ids.
    /// </param>
    /// <param name="sight">The world trace used for line of sight, or <see langword="null"/> to see through walls.</param>
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
    /// <param name="variables">The live level variables used by AlarmControl expressions.</param>
    /// <returns>How many soldiers got a brain.</returns>
    /// <remarks>
    /// The phase is the soldier's index modulo four, which is what staggers the level's detection sweeps
    /// across the four ticks the original spreads them over.
    /// </remarks>
    public int Animate(
        IReadOnlyDictionary<int, GraphFile> graphs,
        WorldTraceCast? sight = null,
        ResourceRegistry? scripts = null,
        NativeRegistry? natives = null,
        GameTask? levelRoot = null,
        HumanAiConfigRegistry? config = null,
        AiDifficulty difficulty = AiDifficulty.Normal,
        ScriptVarRegistry? variables = null)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _context.Sight = sight is null ? null : WorldTrace.AsRayCast(sight, WorldTraceMask.World);
        _context.SightTrace = sight;
        _sight.SightTraceForTesting = sight;
        Brains.Clear();
        _rounds.Clear();
        _damage.ClearPendingDeaths();
        BrainsByRuntimeId.Clear();
        _generators.GuardGenerators.Clear();
        _generators.GuardExpressions = variables is null ? null : new LevelExpressionRuntime(variables);
        ScriptVariables = variables;
        LastDetectionShoutTick = -1;
        _context.Combat.Clear();
        Health.Clear();
        _brains.ClearActivations();
        _context.PlayerTarget = new AiCombatTarget("HumanPlayer");
        _levelRoot = levelRoot;
        _graphs = graphs;
        IReadOnlyDictionary<GameTask, int> visionPhases = levelRoot is null
            ? new Dictionary<GameTask, int>()
            : AiVisionPhases.Assign(levelRoot).Phases;
        // The player's team decides which brains may fire on it. A same-team human is never an enemy, so
        // a friendly patrol (team 0, the same as the player) is told not to engage. Resolved once per level
        // from the HumanPlayer task the mission declares -- the same source the spawn uses -- and handed to
        // each brain as its PlayerIsEnemy gate below.
        _context.PlayerTeam = levelRoot is not null && PlayerSpawn.TryFindGameplay(levelRoot, out PlayerSpawnPoint spawn)
            ? spawn.Team
            : 0;
        _context.PlayerTask = levelRoot is null ? null : PlayerSpawn.FindGameplayTask(levelRoot);
        Humans.Clear();
        if (levelRoot is not null)
        {
            Humans.AddRange(levelRoot.DescendantsAndSelf().OfType<IHumanAiEventTarget>());
        }
        AlarmControls = levelRoot is not null && variables is not null
            ? AiAlarmControlRegistry.Build(levelRoot, variables)
            : null;

        (int Patrolling, int PerTask, int Unroutable, Dictionary<int, int> GraphUse) census = _brains.BindBrains(
            graphs, sight, scripts, natives, levelRoot, visionPhases, config, difficulty, Doors, AlarmControls);
        _generators.PrepareGuardGenerators(levelRoot);
        _brains.LogCensus(census);
        return Brains.Count;
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
        _diagnostics.LogAiSummary();
    }

    /// <summary>Opens a tick: the tick count, and where the enemy (the player) is read from.</summary>
    /// <param name="enemy">
    /// Reads the player as a soldier sees it at the moment the soldier updates -- live, so a soldier ahead of the
    /// player in the tree sees last tick's pose.
    /// </param>
    public void BeginTick(Func<SoldierEnemy> enemy)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(enemy);
        TickCount++;
        Enemy = enemy;
        Ticked.Clear();
        CollectDiagnostics = _logger.IsEnabled(LogLevel.Information);
        bool collectDiagnostics = CollectDiagnostics;
        if (collectDiagnostics)
        {
            _diagnostics.Heartbeat();
        }
    }

    /// <summary>Publishes the living soldiers' collision bodies where they stand now.</summary>
    /// <remarks>
    /// The player's wall sweep is their only reader, and the player updates after every soldier, so publishing
    /// here before the player moves is what re-registering each soldier as it moves would give.
    /// </remarks>
    public void PublishCollisionBodies()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bodies.SyncCollisionBodies();
    }

    /// <summary>Puts the soldiers, their guard generators, the doors and the alarm controls into the level's walk.</summary>
    /// <param name="walker">The level walk.</param>
    /// <remarks>
    /// <para>
    /// The original has no soldier pass: each <c>HumanSoldier</c> updates where the walk reaches it, its
    /// children -- HumanView, Shadow, the gun, then the HumanAI
    /// whose CHANNELS packet moves it -- before the next soldier starts. A soldier therefore sees the soldiers
    /// before it in the tree where they are this tick, and those after it (and the player, late in
    /// <c>ConditionalContainer_560</c>) where they were last tick.
    /// </para>
    /// <para>
    /// A <c>GuardGenerator</c> (<c>0x487EE0</c>) decides and clones first, then walks its soldier; a <c>Door</c>
    /// (<c>0x446D10</c>) and an <c>AlarmControl</c> (<c>0x4408A0</c>) walk their children first, then update.
    /// Call <see cref="BeginTick"/> before the walk and <c>EndTick</c> after it.
    /// </para>
    /// </remarks>
    public void AttachTo(LevelUpdateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _walker = walker;
        HashSet<GameTask> soldiers = new(ReferenceEqualityComparer.Instance);
        foreach (SoldierView view in Brains.Keys)
        {
            if (soldiers.Add(view.Soldier))
            {
                walker.Bind(view.Soldier, task =>
                {
                    TickSoldierTask(task);
                    walker.RunChildren(task);
                });
            }
        }

        foreach (GuardGeneratorTask generator in _generators.AllGenerators)
        {
            walker.Bind(generator, task =>
            {
                _generators.TickGuardGenerator((GuardGeneratorTask)task);
                walker.RunChildren(task);
            });
        }

        Doors?.AttachTo(walker, DoorTicked);
        AlarmControls?.AttachTo(walker, () => TickCount, _ => AlarmControlTicked());
    }

    /// <summary>Runs one tick outside a level walk: the same per-task updates, in one pass.</summary>
    /// <param name="enemy">The player as the soldiers see it this tick.</param>
    internal void TickStandalone(SoldierEnemy enemy)
    {
        BeginTick(() => enemy);
        // Runs the native generator decision before any live soldier is advanced this tick.
        foreach (GuardGeneratorTask generator in _generators.AllGenerators.ToArray())
        {
            _generators.TickGuardGenerator(generator);
        }

        if (Doors is { } doors)
        {
            foreach (PlacedDoor door in doors.Doors)
            {
                doors.Tick(door);
                DoorTicked(door);
            }
        }

        if (AlarmControls is { } alarmControls)
        {
            alarmControls.Tick(TickCount);
            AlarmControlTicked();
        }

        foreach ((SoldierView view, Igi1HumanAi brain) in Brains)
        {
            TickSoldier(view, brain);
        }

    }

    /// <summary>Closes the tick's simulation before the corpses move: the bodies the walk missed, the staged actors, the collision.</summary>
    internal void FinishTick()
    {
        bool collectDiagnostics = CollectDiagnostics;
        if (_walker is not null)
        {
            // A registered, live soldier the walk never reached sits under a task the port does not walk the way
            // the original does; tick it so it cannot freeze, and say so once.
            foreach ((SoldierView view, Igi1HumanAi brain) in Brains)
            {
                if (Ticked.Contains(view) || !ConditionalContainerGate.IsTaskActive(view.Soldier))
                {
                    continue;
                }

                if (_unwalkedSoldiers.Add(view.Soldier))
                {
                    _logger.Warning(
                        LogCategory,
                        $"{view.Soldier.Name} is live but the level walk did not reach it; ticking it after the walk.");
                }

                TickSoldier(view, brain);
            }
        }

        // The level's dynamic container ticks after its static objects: every gun's GunShot task, then the chips.
        TickingSoldierId = -1;
        _rounds.MarchRounds();

        // Un-brained cutscene actors ride their vehicles the same way; the brained ones did as they updated.
        _staging.AttachStagedActorsToVehicles();

        // Every soldier has moved; re-publish their bodies so the player's wall sweep treats each
        // living, active soldier as solid.
        _bodies.SyncCollisionBodies();

        // This scans the complete roster. Calling it from inside the roster loop made diagnostics O(N²)
        // every tick, including when the logger discarded the result. One final-state scan is sufficient.
        if (collectDiagnostics)
        {
            _diagnostics.ReportCombat(RoundsFired);
        }

        // Simulated corpses advance on the tick, alongside the rigs they replaced -- and only until they
        // settle, or until their HumanAI takes them out of the world (Igi1HumanAi.TickDeadAi).
    }

    /// <summary>After the corpses moved: the dropped weapons, and the poses of the bodies no brain drives.</summary>
    internal void TickAfterCorpses()
    {
        DroppedWeaponPickups?.Tick(RagdollWorldForCorpses());
        foreach (SoldierModelGroup group in Roster)
        {
            if (group.Body.Model is null)
            {
                continue;
            }

            foreach (SoldierAgent agent in group.Agents)
            {
                if (!Brains.ContainsKey(agent.View))
                {
                    // Un-brained soldiers still need their own rig advanced.
                    agent.View.Tick();
                }
            }
        }
    }

    /// <summary>A door's collision child follows the same live transform as the drawn door.</summary>
    /// <remarks>
    /// A half-open door remains a blocker at its half-open angle instead of disappearing from sight, bullets and
    /// movement.
    /// </remarks>
    internal void DoorTicked(PlacedDoor door)
    {
        if (_context.Solids is not { } solids)
        {
            return;
        }

            // A fully-open door is passable regardless of how its leaf is authored to clear the
            // opening. Sliding leaves (nonzero Stop*) are moved out of the way by UpdateTransform
            // below, but cab doors that animate inside their model (Stop* == 0, e.g. Elevator_330's
            // leaves) never change placement, so their static mesh would keep the doorway solid for
            // ever; toggling passability on the open state makes both kinds behave like retail.
            solids.SetPassable(door.Task, door.State.IsFullyOpen);

            // Closed/open doors spend nearly all their life stationary, unless they ride a
            // moving elevator: keep collision synchronized with the rendered displacement.
            Vector3d elevatorOffset = _elevators?.GetCurrentDisplacement(door.Task) ?? default;
            if (door.State.TransformChangedThisTick || elevatorOffset != default)
            {
                solids.UpdateTransform(
                    door.Task, door.DrawPosition + elevatorOffset, door.DrawOrientation);
            }

            // Sight treats building portals as solid. Once this leaf is off its shut pose,
            // open the portal it filled so a guard in the doorway can see through.
            solids.SetSightOpening(door.Task, !door.State.IsFullyClosed, elevatorOffset);
    }

    /// <summary>An alarm control's update has run: the mission alarm is logged as it turns on or off.</summary>
    internal void AlarmControlTicked()
    {
        bool alarmIsOn = AlarmControls?.AnyOn ?? false;
        if (alarmIsOn != _alarmWasOn)
        {
            _alarmWasOn = alarmIsOn;
            _logger.Information(
                LogCategory + ".Alarm",
                $"mission alarm {(alarmIsOn ? "ON" : "OFF")} at tick {TickCount}.");
        }
    }

    /// <inheritdoc/>
    void IHumanEvents.SendEvent(HumanTask human, in HumanAiEvent aiEvent) => _noise.Deliver(human, in aiEvent);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human</c>'s evaluate (<c>0x45E3E0</c>), for a soldier: never the shooter or what the shot ignores, and a round from
    /// someone on the soldier's side passes through it. The probability roll is the player's alone.
    /// </remarks>
    bool IHumanEvents.EvaluateWeaponCollision(HumanTask soldier, in WeaponCollisionQuery query)
    {
        if (ReferenceEquals(soldier, query.Shooter) || ReferenceEquals(soldier, query.Ignore))
        {
            return false;
        }

        return query.Shooter is not HumanTask shooter || shooter.Team != soldier.Team;
    }

    /// <inheritdoc/>
    void IHumanEvents.WeaponHit(HumanTask soldier, in WeaponHit hit)
    {
        if (!_context.ViewsByTask.TryGetValue(soldier, out SoldierView? view))
        {
            return;
        }

        // Another game's round: where it meets the soldier's body is this game's to find.
        Matrix3f m = view.Orientation;
        _damage.TakeRound(view, hit.Context as Igi1WeaponHit
            ?? Igi1WeaponHit.FromShared(hit, Igi1WeaponHit.Meet(hit, view.Position, new Vector3f(m.M12, m.M22, m.M32))));
    }

    /// <inheritdoc/>
    void IHumanEvents.ExplosionHit(HumanTask soldier, in ExplosionHit hit)
    {
        if (!_context.ViewsByTask.TryGetValue(soldier, out SoldierView? view))
        {
            return;
        }

        Igi1ExplosionHit blast = hit.Context as Igi1ExplosionHit ?? Igi1ExplosionHit.FromShared(hit);

        _damage.TakeExplosion(view, blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);
    }

    /// <summary>Gets the soldier a task stands for.</summary>
    /// <param name="task">The soldier's task.</param>
    /// <returns>The soldier, or <see langword="null"/> when the task is not one of these soldiers.</returns>
    public SoldierView? ViewOf(HumanTask task) => _context.ViewsByTask.TryGetValue(task, out SoldierView? view) ? view : null;

    // A new soldier's gun's GunShot task, after every gun made before it, and this simulation's handlers for its hit events.
    private void Enlist(SoldierView view)
    {
        _rounds.AddGunShotTask(view);
        TakeEvents(view);
    }

    // Gives a soldier's task this simulation's handlers for its hit events, and its live body for any game's AI to perceive.
    private void TakeEvents(SoldierView view)
    {
        _context.ViewsByTask[view.Soldier] = view;
        view.Soldier.Events = this;
        view.Soldier.Body = new Player.Igi1SoldierBody(view, () => Brains.TryGetValue(view, out Igi1HumanAi? brain) && brain.IsCrouching);
    }

    /// <summary>Updates every body a soldier task owns -- a generator's dead guard and its replacement share one.</summary>
    private void TickSoldierTask(GameTask task)
    {
        foreach ((SoldierView view, Igi1HumanAi brain) in Brains)
        {
            if (ReferenceEquals(view.Soldier, task))
            {
                TickSoldier(view, brain);
            }
        }
    }

    // The authored animation triggers the clip crossed this tick, and the melee strike its impact frame delivers.
    private void PlayClipEvents(SoldierView view, Igi1HumanAi brain, BoneClip bone0)
    {
        // Authored animation triggers: event 1052 plays "gunpunch_01" (animation 70 swing),
        // event 1040 plays "bodyfall_9", etc.
        if (AnimationTriggers is not null)
        {
            foreach (AnimTriggerDefinition trigger in AnimationTriggers.Definitions.Values)
            {
                if (bone0.CrossedEvent(trigger.EventId))
                {
                    ScriptSound?.Invoke(trigger.Resource, view.Position, trigger.Attached ? () => view.Position : null);
                }
            }
        }
        else if (bone0.CrossedEvent(1052))
        {
            ScriptSound?.Invoke("gunpunch_01", view.Position, null);
        }
    }

    // The throw (state 8, 0x45B2F0) and close-combat (state 9, 0x45B530) states act on their clip's own event: the grenade
    // leaves the hand on event 11 and the blow lands on event 12, each once a state entry and only on the clip the body
    // is playing. The states run after the AI, so their draws follow its.
    private void ReleaseOnClipEvents(
        SoldierView view, Igi1HumanAi brain, SoldierAgent? member, BoneClip? clip, int? combatState)
    {
        if (combatState is not { } state)
        {
            _releasedThisEntry.Remove(view);
            return;
        }

        if (_releasedState.TryGetValue(view, out int latched) && latched != state)
        {
            _releasedThisEntry.Remove(view);
        }

        _releasedState[view] = state;
        if (clip is null || _releasedThisEntry.Contains(view) || !ReferenceEquals(clip, view.Bones.SourceOf(0)))
        {
            return;
        }

        if (state == brain.SoldierClass.GrenadeThrowState
            && brain.Target is not null
            && clip.CrossedEvent(ThrowReleaseEvent)
            && Grenades is { } lobbed)
        {
            _releasedThisEntry.Add(view);
            Vector3d hand = SoldierContext.OccupantOrigin(view) + (member?.ThrowOffset ?? member?.GunOffset ?? default);
            _fire.ThrowAiGrenade(brain, hand, lobbed, view.Orientation);
        }
        else if (state == HumanSoldierStates.CloseCombatState && clip.CrossedEvent(CloseCombatHitEvent))
        {
            _releasedThisEntry.Add(view);
            // 0x45B530 hands the blow to whatever the soldier is fighting, by its punch handler: the player's, or another
            // soldier's.
            if (brain.StrikeCloseCombat(out float damage))
            {
                if (brain.TargetIsPlayer)
                {
                    if (brain.PlayerIsEnemy)
                    {
                        // The player this soldier is fighting: the one it was handed as it updated.
                        MeleeStrikeSink?.Invoke(Enemy?.Invoke().Player, brain.Position, damage);
                    }
                }
                else if (SoldierById(brain.TargetId) is { } struck)
                {
                    _damage.TakePunch(struck, view, damage);
                }
            }
        }
    }

    /// <summary>
    /// Carries what rides a lift's cab as it moves: the graphs authored in its frame (<c>0x4F9770</c>) and the soldiers
    /// grounded on them, whom <c>0x462390</c> parents to it (<c>Human_UpdatePlatformMove</c>, <c>0x460CF0</c>).
    /// </summary>
    /// <param name="cab">The lift.</param>
    /// <param name="displacement">How far its cab moved this tick, in engine units.</param>
    public void CarryOnPlatform(GameTask cab, Vector3d displacement)
    {
        ArgumentNullException.ThrowIfNull(cab);
        if (displacement == Vector3d.Zero || _levelRoot is null || _graphs is null)
        {
            return;
        }

        foreach ((int id, GraphFile graph) in _graphs)
        {
            if (graph.InParentFrame && ReferenceEquals(_levelRoot.Tree.FromId(id)?.Parent, cab))
            {
                graph.MoveParent(displacement);
            }
        }

        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView view = member.View;
                if (Brains.TryGetValue(view, out Igi1HumanAi? brain)
                    && SoldierContext.FindMind(view) is { GraphId: > 0 } mind
                    && ReferenceEquals(_levelRoot.Tree.FromId(mind.GraphId)?.Parent, cab))
                {
                    brain.SeparateTo(brain.Position + displacement);
                    view.PlaceAt(brain.Position, view.Orientation);
                }
            }
        }
    }

    internal SoldierView? SoldierById(int id)
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                if (member.View.Soldier.Id == id)
                {
                    return member.View;
                }
            }
        }

        return null;
    }

    // The clip events the throw and close-combat states act on (HumanAnimEvent.sound 0xB and 0xC).
    private const int ThrowReleaseEvent = 11;
    private const int CloseCombatHitEvent = 12;

    // The soldiers whose throw or blow has gone this state entry, and the state they were last in.
    private readonly HashSet<SoldierView> _releasedThisEntry = [];
    private readonly Dictionary<SoldierView, int> _releasedState = [];

    // A staged actor's authored clip carries it: on the vehicle it rides, or by the clip's own vertical delta.
    private void RideStagedGround(SoldierView view, Igi1HumanAi brain, SoldierAgent member)
    {
        AiPatrolCommandKind? patrolCommand = brain.Route?.Current?.Kind;
        bool authoredAnimation = brain.RequestedAnimation >= 0
            || patrolCommand == AiPatrolCommandKind.Animation;
        if (authoredAnimation)
        {
            _staging.AttachCutsceneActorToMovingGround(
                view,
                brain.Position,
                member.Position * WorldScene.UnitsPerMetre);
            Vector3f rootDelta = view.RootMotion();
            if (Cutscenes?.AdvanceAttachedActor(view.Soldier, rootDelta) == true)
            {
                // Native attached movement carries Human+32 with the parent while the local
                // frame keeps the relative pose. Track the ride on the brain so a later
                // detach (Level 1's 1201 consist unregisters at the scene boundary) continues
                // from the wagon instead of the stale pre-attach spawn 10 m behind it.
                if (Cutscenes.TryGetAttachedDrawTransform(
                    view.Soldier, out Vector3d attachedNow, out _))
                {
                    brain.SeparateTo(attachedNow * WorldScene.UnitsPerMetre);
                }
            }
            else if (rootDelta.X != 0f || rootDelta.Y != 0f || rootDelta.Z != 0f)
            {
                // The ordinary Human mover consumes the clip's root delta into Human+32 even
                // when the AI action itself is a non-locomotion PlayAnimation. Cutscene jumps
                // use exactly that path; keeping the task's authored spawn here froze Jones in
                // mid-air while EditCamera continued targeting the moving Human.
                // The brain's own body step (0x45D080 -> 0x4614B0) already carries the horizontal
                // part this tick, so only the vertical part is added here. Adding both doubled
                // every staged actor's sway: mission 8's guard 3336 drifted 61 cm where the
                // original's drifts 30.
                brain.SeparateTo(brain.Position + new Vector3d(0d, 0d, rootDelta.Z));
                member.HasCutsceneRootMotion = true;
            }

        }

        if (patrolCommand is { } command && command.IsMovement())
        {
            member.HasCutsceneGameplayMovement = true;
        }
    }

    // A corpse's update.
    private void TickCorpse(SoldierView view, Igi1HumanAi brain)
    {
        // A corpse is still stepped. The death state's per-tick (0x45C700) adds the fall solver's
        // corrective velocity, the soldier update moves the body by the death clip's own root
        // motion, and the ground query catches it. Freezing the body here is what left it standing.
        if (brain.Activity != AiSoldierActivity.Dead)
        {
            brain.Kill();
        }

        SoldierAgent? corpse = _context.FindAgent(view);
        if (brain.TickDeadAi(corpse?.DrawnLastRender ?? false, _generators.IsGenerated(view)))
        {
            // 0x44F410 unregisters the body (0x4609A0) and the Human's own update no longer runs.
            _context.RemoveBody(view);
            return;
        }

        brain.TickDeadBody(view.RootMotion(), view.Bones.SourceOf(0)?.EndedThisTick ?? false);
        view.PlaceAt(brain.Position, view.Orientation);
        if (corpse is not null)
        {
            corpse.FiredThisTick = false;
        }
        WeaponSound?.Invoke(brain.RuntimeId, false, corpse?.WeaponType, brain.Position);
    }

    // The clip's own delta in the body's frame this tick: forward and sideways, signed.
    private (double Travel, double Lateral) ClipTravel(SoldierView view, Igi1HumanAi brain, bool collectDiagnostics)
    {
        // The clip's own delta in the body's frame: forward and sideways, signed.
        Vector3f clipDelta = view.RootMotion();
        double travel = clipDelta.Y;
        double lateral = clipDelta.X;
        if (brain.SoldierClass.ScalesEkkRootMotion && brain.Type == AiType.Ekk)
        {
            // 0x4550B0 scales a female soldier's delta translation by flt_56E1E8 when her brain is Ekk.
            double scale = PlayerDeltaTranslationScale?.Invoke() ?? HumanMotion.DefaultDeltaTranslationScale;
            travel *= scale;
            lateral *= scale;
        }

        // Retail ground truth, measured by hooking 0x48E920 in the original: a walking guard covers
        // 160-185 units a tick, about 1.28 m/s, varying through the gait cycle. Recorded here so the
        // port's own figure can be compared without another instrumented run.
        if (collectDiagnostics && brain.Activity == AiSoldierActivity.Patrolling && travel > 0.0)
        {
            _diagnostics.RecordTravel(travel);
        }

        return (travel, lateral);
    }

    // The body machine's tick, and what its state lets go of: the grenade, the detection shout and the alert.
    private int? TickBody(SoldierView view, Igi1HumanAi brain, bool collectDiagnostics)
    {
        // The body machine consumes this tick's AI channel writes before the weapon consumes the
        // body's forwarded trigger. Running it after FireIfAsked let a round leave while the body
        // was still in rack/ready/recover and skipped state 18 entirely during a running burst.
        // 0x45D080 ticks the body every tick whatever the AI is doing, which is what lets a patrolling
        // guard flinch; its state drives the clip in a fight, and a flinch whatever the activity.
        // A gunner on its gun is in the use-object state (36), which only a fresh press of slot 24 -- the
        // dismount -- or death leaves.
        int? combatState = null;
        bool animationDone = (view.Bones.SourceOf(0)?.EndedThisTick ?? false)
            || _combatClipMissing.Contains(view);
        int bodyState = brain.TickCombatState(animationDone);
        if (!brain.IsMounted
            && (brain.Activity is AiSoldierActivity.Engaging or AiSoldierActivity.FiringAtTask
                || (brain.Body is HumanSoldierBodyMachine && HumanSoldierStates.IsBodyOwned(bodyState))))
        {
            combatState = bodyState;
        }

        // sub_48A920, IGI.exe: detected_01..06, once per combat-action argument block. The
        // dword_5416CC cooldown is process/mission-wide and requires strictly more than 90 ticks.
        if (brain.DetectionShoutRequestedThisTick
            && (LastDetectionShoutTick == -1 || TickCount - LastDetectionShoutTick > 90))
        {
            int variant = checked((int)AiRandom.NextBounded(6) + 1);
            ScriptSound?.Invoke($"detected_{variant:00}", brain.Position, () => brain.Position);
            LastDetectionShoutTick = TickCount;
        }

        // An engaged guard pulls in the guards beside it: AIEVENT_ALERT with a 7 m reach and filter
        // 0x26, so only its own side admits it (sub_48A920 -> sub_48AB10). This is how one guard's
        // fight wakes the room even before anybody fires.
        if (brain.AlertPingDueThisTick)
        {
            foreach ((SoldierView neighbourView, Igi1HumanAi neighbour) in Brains)
            {
                if (!ReferenceEquals(neighbour, brain)
                    && neighbourView.Soldier.Team == view.Soldier.Team
                    && ConditionalContainerGate.IsTaskActive(neighbourView.Soldier))
                {
                    neighbour.ReceiveAlert(brain.Position, brain.RuntimeId, brain.TargetId);
                }
            }
        }
        if (collectDiagnostics)
        {
            _diagnostics.ReportChannels(brain);
        }

        return combatState;
    }

    // The weapon's turn, after the body has turned and moved: the recoil, the mounted gun, the round and its sound.
    private void FireAndSound(SoldierView view, Igi1HumanAi brain, SoldierAgent? member, Vector3d aimFeet, bool aimCrouching)
    {
        // 0x462E50, after the turn and the move: the last round's kick joins the view's recoil -- every tick, decaying
        // when nothing fired -- and then the trigger the machine forwards goes to the weapon, which fires along the view
        // as it now stands. The kick it returns waits for the next tick.
        brain.SettleRecoil();
        _fire.KickThisTick = default;

        // 0x474190 with an occupant: the gun turns by the gunner's own turn inputs, shows its aim, and decays the last
        // kick, before the trigger reaches it. The port's gunner aims at the point it fires at.
        if (brain.MountedGun is { } steered)
        {
            steered.AimAt(brain.Combat.TurnTo ?? SoldierWeaponFire.SprayTarget(brain, aimFeet, aimCrouching));
            steered.TickOccupied();
            brain.RideMountedGun();
        }

        bool fired = _fire.FireIfAsked(view, brain, aimFeet, aimCrouching);
        if (fired)
        {
            brain.TakeWeaponKick(_fire.KickThisTick.Pitch, _fire.KickThisTick.Yaw);
        }

        WeaponType? soundingWeapon = brain.MountedGun?.Weapon ?? member?.WeaponType;
        Vector3d soundPosition = brain.MountedGun?.MuzzlePosition ?? brain.EyePosition();
        WeaponSound?.Invoke(brain.RuntimeId, fired, soundingWeapon, soundPosition);
    }
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// Gives every soldier its brain: its own graph, its sweep phase and its runtime id, the world it queries, the script it
/// runs, and the combat row its <c>AIType</c> names.
/// </summary>
/// <remarks>
/// The brains are made in the roster's order, which is the order they draw from the level's stream and tick in; the
/// scripts are read once per path and shared by every soldier that runs them.
/// </remarks>
/// <param name="context">The soldiers, their brains, combat rows and the level's stream.</param>
/// <param name="sight">Gives each brain its eye.</param>
/// <param name="enlist">Gives each soldier its gun's <c>GunShot</c> task and this simulation's hit handlers.</param>
internal sealed class SoldierBrainBinder(SoldierContext context, SoldierSightQueries sight, Action<SoldierView> enlist)
{
    private readonly SoldierContext _context = context;

    private readonly SoldierSightQueries _sight = sight;

    private readonly Action<SoldierView> _enlist = enlist;

    // The alarm chain: the level-wide activation claims (dword_5BDA28), the tasks that have actually
    // been pressed, and one AlarmControl state (0x4408A0's update) per control id the soldiers name.
    // The control turns on when any soldier's named trigger has been pressed -- a recorded stand-in
    // for the control's two authored condition expressions, which the port does not evaluate yet.
    private readonly AiActivationClaims _activationClaims = new();

    private readonly HashSet<int> _activatedTasks = [];

    /// <summary>Gets or sets what an AI's input-channel activation reaches in the live world.</summary>
    public Action<int>? TaskActivator { get; set; }

    /// <summary>Gets or sets the level's live StationaryGun tasks, which gunner AIs are connected to.</summary>
    public WorldInteractionRegistry? Interactions { get; set; }

    /// <summary>Publishes a player-used task to alarm and AI activation conditions.</summary>
    public void ActivateTask(int taskId) => _activatedTasks.Add(taskId);

    /// <summary>
    /// Uses a task as one of the level's guards uses it on reaching it: published to the alarm and activation conditions, and
    /// passed to what the activation reaches in the live world (<see cref="TaskActivator"/>), a switch pressed among them.
    /// </summary>
    /// <param name="taskId">The task's id.</param>
    public void ActivateAsGuard(int taskId)
    {
        _activatedTasks.Add(taskId);
        TaskActivator?.Invoke(taskId);
    }

    /// <summary>Forgets the tasks pressed so far, for a new level.</summary>
    public void ClearActivations() => _activatedTasks.Clear();

    /// <summary>Makes every soldier's brain, in the roster's order, and gives each its combat row.</summary>
    /// <param name="graphs">The level's nav graphs, keyed by the id of the <c>AIGraph</c> task that loads each.</param>
    /// <param name="sight">The world trace used for line of sight, or <see langword="null"/> to see through walls.</param>
    /// <param name="scripts">Where the archetype behaviour script is read from, or <see langword="null"/> for none.</param>
    /// <param name="natives">The registry the behaviour script resolves its AI natives against.</param>
    /// <param name="levelRoot">The level's task tree, which the authored patrol paths are collected from.</param>
    /// <param name="visionPhases">Each <c>HumanAI</c>'s sweep phase (<see cref="AiVisionPhases"/>).</param>
    /// <param name="config">The combat table out of <c>common/ai/settings.qsc</c>, or <see langword="null"/>.</param>
    /// <param name="difficulty">Which of the table's three columns to read.</param>
    /// <param name="doors">The level's doors, which each walker's door hop queries.</param>
    /// <param name="alarmControls">The level's alarm controls, which each brain asks whether its alarm sounds.</param>
    /// <returns>How many brains run a script, how many their own task's, how many name a missing graph, and the graphs' use.</returns>
    public (int Patrolling, int PerTask, int Unroutable, Dictionary<int, int> GraphUse) BindBrains(
        IReadOnlyDictionary<int, GraphFile> graphs,
        WorldTraceCast? sight,
        ResourceRegistry? scripts,
        NativeRegistry? natives,
        GameTask? levelRoot,
        IReadOnlyDictionary<GameTask, int> visionPhases,
        HumanAiConfigRegistry? config,
        AiDifficulty difficulty,
        DoorRegistry? doors,
        AiAlarmControlRegistry? alarmControls)
    {
        if (graphs.Count == 0)
        {
            // No graphs means no patrolling -- but a brain does not need a node to see or to shoot, so
            // the soldiers are still armed and still fight from where they stand.
            _context.Logger.Warning(SoldierContext.LogCategory, "The level ships no nav graph, so no soldier can patrol.");
        }

        // The level's authored patrol paths, keyed by the PatrolPath task's own id -- what AIAction_Patrol's
        // first argument names.
        AiPatrolPathRegistry? paths = levelRoot is null ? null : AiPatrolPathRegistry.Build(levelRoot);
        if (paths is not null)
        {
            _context.Logger.Information(SoldierContext.LogCategory, $"{paths.Count} authored patrol path(s) in this level.");
        }

        AiScriptHost? host = scripts is not null && natives is not null
            ? new AiScriptHost(natives, _context.Logger, _context.AiRandom)
            : null;
        Dictionary<string, QvmProgram?> scriptCache = new(StringComparer.Ordinal);

        // One occupancy registry per level -- the global claim list at 0x57D768. Every brain shares it,
        // which is what stops a squad's free-position searches all electing the same node.
        AiNodeOccupancy occupancy = new();

        int index = 0;
        int patrolling = 0;
        int perTask = 0;
        int unroutable = 0;
        Dictionary<int, int> graphUse = [];
        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView view = member.View;

                // Each soldier walks the graph ITS OWN HumanAI names, not the level's biggest one. A
                // soldier whose graph the level does not ship still gets a BRAIN -- nothing in detection
                // or the stand-and-fight arm needs a node, so leaving it brainless made an inert prop
                // that neither saw nor fought where retail's would still shoot back. It just cannot
                // patrol or reposition, which is what the graph is actually for.
                HumanAiTask? brainTask = SoldierContext.FindMind(view);
                int graphId = brainTask?.GraphId ?? 0;
                if (!graphs.TryGetValue(graphId, out GraphFile? graph) || graph.RouteTable is null)
                {
                    graph = null;
                    if (brainTask is not null)
                    {
                        unroutable++;
                    }
                }

                Vector3d initialPosition = view.Position;
                if (!CinematicActor.IsStaged(view.Soldier))
                {
                    double groundedZ = _context.SnapToGround(initialPosition.X, initialPosition.Y, initialPosition.Z);
                    if (groundedZ != initialPosition.Z)
                    {
                        _context.Logger.Information(
                            SoldierContext.LogCategory + ".Ground",
                            $"soldier={view.Soldier.Id} declaredZ={initialPosition.Z:0.0} " +
                            $"groundedZ={groundedZ:0.0} delta={(groundedZ - initialPosition.Z) / WorldScene.UnitsPerMetre:0.000}m");
                        initialPosition = new Vector3d(initialPosition.X, initialPosition.Y, groundedZ);
                        view.PlaceAt(initialPosition, view.Orientation);
                    }
                }

                // The sweep phase is the global counter's value when this HumanAI initialised (AiVisionPhases).
                int phase = brainTask is not null && visionPhases.TryGetValue(brainTask, out int assigned)
                    ? assigned
                    : index & 3;
                Igi1HumanAi brain = new(graph, initialPosition, GammaOf(view), phase, index, _context.AiRandom, view.Soldier.Class)
                {
                    StepsBodyAfterClip = true,
                };
                // A soldier on the player's own team must not engage the player. The brain's noise and
                // vision gates read this: detection and every player-origin gunshot/impact/walk event are
                // refused for a same-team player, so the friendly patrol stands down instead of shooting us.
                brain.PlayerIsEnemy = view.Soldier.Team != _context.PlayerTeam;
                _sight.BindViewTracking(brain, view);
                brain.RuntimeId = index + 1;
                brain.SetResponseRouter(source =>
                {
                    if (_context.BrainsByRuntimeId.TryGetValue(source, out Igi1HumanAi? recipient))
                    {
                        recipient.ReceiveAlertResponse();
                    }
                });
                brain.SetDefaultTarget(_context.PlayerTarget);
                brain.SetAlarmStatusResolver(alarmControls is { } controls ? controls.IsOn : null);
                brain.SetStationaryGunResolver(
                    Interactions is { } interactions ? interactions.FindStationaryGun : null);
                brain.SetPatrolPaths(paths);
                brain.SetNodeOccupancy(occupancy);
                brain.SetGroundQuery(_context.MakeGroundQuery());
                if (doors is { } levelDoors)
                {
                    brain.SetDoorQuery(levelDoors.Nearest);
                }
                if (levelRoot is not null)
                {
                    GameTask taskRoot = levelRoot;
                    brain.SetActivateSupport(
                        _activationClaims,
                        (id, out position) =>
                        {
                            return TryGetTaskPosition(taskRoot, id, out position);
                        },
                        id =>
                        {
                            _activatedTasks.Add(id);
                            if (brain.Arm == AiCombatArm.Gunner
                                || Interactions?.FindStationaryGun(id) is not null)
                            {
                                _context.Logger.Information(
                                    SoldierContext.LogCategory + ".Gunner",
                                    $"runtime={brain.RuntimeId} activation reached task={id} "
                                        + $"gunnerId={brain.GunnerId}");
                            }
                            TaskActivator?.Invoke(id);
                        });
                }
                if (graph is not null)
                {
                    graphUse[graphId] = graphUse.GetValueOrDefault(graphId) + 1;
                }
                if (sight is not null)
                {
                    // HumanAI and the binocular overlay share the same transparent-surface rule. A raw
                    // nearest-hit query stops at Level 4's material-14 window and made the village gunner
                    // blind to a player immediately outside, even though the remainder of the ray was clear.
                    // Step past retail Glass/Water/Fence/Ladder faces, while an ordinary wall or physical
                    // closed-door leaf remains cover.
                    brain.SetLineOfSight((from, to) => SoldierSightQueries.HasLineOfSight(sight, from, to, Igi1HumanAi.SightObstructionTolerance));
                }

                // Which script this soldier runs: its own MISSION:AI/<taskid>.qvm when the level ships one,
                // otherwise the archetype its AIType selects. The per-task script is
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

                _context.Brains[view] = brain;
                _context.BrainsByRuntimeId[brain.RuntimeId] = brain;
                _enlist(view);
                index++;
            }
        }

        // AFTER the brains exist: BindCombatRows installs the behaviour on each, so running it before the
        // loop would have found no brains to install it on and every guard would have gone on standing
        // there looking at the player.
        BindCombatRows(config, difficulty);
        return (patrolling, perTask, unroutable, graphUse);
    }

    /// <summary>Logs how the brains came out: the scripted ones, the run clips, and the soldiers on each graph.</summary>
    /// <param name="census">What <see cref="BindBrains"/> counted.</param>
    public void LogCensus((int Patrolling, int PerTask, int Unroutable, Dictionary<int, int> GraphUse) census)
    {
        (int patrolling, int perTask, int unroutable, Dictionary<int, int> graphUse) = census;
        int hasRun = 0, total = 0;
        foreach (SoldierView v in _context.Roster.SelectMany(g => g.Agents).Select(a => a.View))
        {
            total++;
            if (v.Bones.Set.TryGet(v.Soldier.Class.RunAnimation, out _))
            {
                hasRun++;
            }
        }

        _context.Logger.Information(
            SoldierContext.LogCategory,
            $"{_context.Brains.Count} soldier(s) with brains ({patrolling} scripted, {perTask} from their own " +
            $"MISSION:AI script); {hasRun} of {total} soldier set(s) hold their class's run clip, the rest " +
            "fall back to the walk when chasing.");
        _context.Logger.Information(
            SoldierContext.LogCategory,
            $"Soldiers per nav graph: " +
            string.Join(", ", graphUse.OrderBy(p => p.Key).Select(p => $"graph{p.Key}:{p.Value}")) +
            (unroutable > 0
                ? $"; {unroutable} soldier(s) name a graph the level does not ship (they fight, but cannot patrol)"
                : string.Empty));
    }

    /// <summary>The authored heading a soldier was placed with.</summary>
    private static float GammaOf(SoldierView view) => view.Soldier.Gamma;

    /// <summary>A level task's position by id — what the activate walk aims at.</summary>
    /// <param name="task">The subtree to search.</param>
    /// <param name="id">The task id the script named.</param>
    /// <param name="position">Receives the task position when present.</param>
    /// <returns>The position, or default when the id names nothing with one.</returns>
    private static bool TryGetTaskPosition(GameTask task, int id, out Vector3d position)
    {
        if (task.Id == id)
        {
            if (task is DeclaredTask declared
                && declared.TryGetValue("Position", out object? pos)
                && pos is Vector3d p)
            {
                position = p;
                return true;
            }

            position = default;
            return false;
        }

        foreach (GameTask child in task.Children)
        {
            if (TryGetTaskPosition(child, id, out position))
            {
                return true;
            }
        }

        position = default;
        return false;
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

        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                if (SoldierContext.FindMind(member.View)?.ResolvedType is not { } type)
                {
                    continue;
                }

                AiCombatParameters row = config.Parameters(type, difficulty);
                _context.Combat[member.View] = row;
                bound++;

                (int count, AiCombatParameters _) = accuracyByType.GetValueOrDefault(type);
                accuracyByType[type] = (count + 1, row);

                if (_context.Brains.TryGetValue(member.View, out Igi1HumanAi? brain))
                {
                    SoldierAgent held = member;
                    brain.SetCombat(type, row, () => SoldierWeaponFire.CombatWeaponState(brain, held));
                    arms[brain.Arm] = arms.GetValueOrDefault(brain.Arm) + 1;
                }
            }
        }

        // The arm decides whether a soldier can chase at all: only the patrol (move-and-fight) arm hunts and
        // advances; the guard arm stands its ground. Logged so a "they don't chase" report can be told apart.
        string split = string.Join(", ", arms.OrderBy(a => a.Key).Select(a => $"{a.Key}:{a.Value}"));
        _context.Logger.Information(
            SoldierContext.LogCategory,
            $"AI: {bound} soldier(s) configured at {HumanAiConfigRegistry.NameOf(difficulty)} ({difficulty}); arms {split}.");

        // One line per AI type: the hit chance a guard of that type actually fires with, at this difficulty.
        // "hit@0m" is point-blank (where you die), "hit@far" is the cone's end; the round handler rolls a
        // uniform against the range-lerped value between them. clip% is the fraction of a magazine a burst
        // spends. 1.000/0.250 = the default, i.e. settings.qsc gave no row for this pair.
        foreach ((AiType type, (int count, AiCombatParameters row)) in accuracyByType.OrderBy(e => e.Key))
        {
            string name = HumanAiConfigRegistry.NameOf(difficulty);
            _context.Logger.Information(
                SoldierContext.LogCategory,
                FormattableString.Invariant(
                    $"AI accuracy [{type}] x{count} @ {name}: hit@0m={row.HitProbabilityAtMinimumRange:F3} hit@far={row.HitProbabilityAtMaximumRange:F3} clip%={row.MinimumClipFraction:F2}+{row.RandomClipFraction:F2} aimPrec={row.AimPrecision:F2}"));
        }
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
            if (scripts.TryLoad(path, out ReadOnlyMemory<byte> bytes))
            {
                program = QvmProgram.Read(bytes);
            }
            else
            {
                // Most HumanAI tasks have an override, but it is not required: an absent one runs the
                // archetype selected by AIType instead.
                _context.Logger.Debug(SoldierContext.LogCategory, $"No behaviour script at '{path}'.");
            }
        }
        catch (Exception ex) when (ex is IOException or QvmFormatException or InvalidOperationException)
        {
            _context.Logger.Warning(SoldierContext.LogCategory, $"Could not read behaviour script at '{path}': {ex.Message}");
        }

        cache[path] = program;
        return program;
    }
}

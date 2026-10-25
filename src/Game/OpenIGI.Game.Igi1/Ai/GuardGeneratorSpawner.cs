using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The level's <c>GuardGenerator</c>s (<c>0x487EE0</c>): each one's authored soldier, the body that leaves the world after
/// it dies, and the replacement cloned from it.
/// </summary>
/// <remarks>
/// A generator whose template another game drives only decides; that game's lives spawn. Every other one owns one
/// authored soldier subtree, which it replaces -- up to its bound -- once the dead body has left the world.
/// </remarks>
/// <param name="context">The soldiers, their brains, health and combat rows.</param>
/// <param name="sight">Gives a replacement its eye.</param>
/// <param name="enlist">Gives a replacement its gun's <c>GunShot</c> task and this simulation's hit handlers.</param>
internal sealed class GuardGeneratorSpawner(SoldierContext context, SoldierSightQueries sight, Action<SoldierView> enlist)
{
    private readonly SoldierContext _context = context;

    private readonly SoldierSightQueries _sight = sight;

    private readonly Action<SoldierView> _enlist = enlist;

    // The generators whose template another game drives.
    private readonly List<GuardGeneratorTask> _drivenGenerators = [];

    // The tick a generator's dead guard finished its death clip on (Human+828, 0x45C740), and the body its HumanAI has since
    // taken out of the world.
    private readonly Dictionary<GuardGeneratorTask, int> _generatedDeathEnded = [];
    private readonly Dictionary<GuardGeneratorTask, (SoldierAgent Agent, SoldierModelGroup Group)> _generatedBodyGone = [];

    /// <summary>Gets each generator this game drives, by the soldier it owns now.</summary>
    public Dictionary<GuardGeneratorTask, SoldierView> GuardGenerators { get; } = [];

    /// <summary>Gets every generator, this game's then the driven ones, in the order the walk binds and ticks them.</summary>
    public IEnumerable<GuardGeneratorTask> AllGenerators => GuardGenerators.Keys.Concat(_drivenGenerators);

    /// <summary>Gets or sets the expressions a generator's <c>Generate guards</c> condition is evaluated with.</summary>
    public LevelExpressionRuntime? GuardExpressions { get; set; }

    /// <summary>Gets or sets what prepares a generator's replacement guard to be drawn, and says whether it could.</summary>
    public Func<GuardGeneratorTask, SoldierModelGroup, SoldierAgent, SoldierAgent, bool>? SpawnedBody { get; set; }

    /// <summary>Gets or sets what is told when a generated guard's body leaves the world, for whoever draws it.</summary>
    public Action<SoldierAgent>? BodyRemoved { get; set; }

    /// <summary>Connects each retail GuardGenerator to the one authored soldier subtree it owns.</summary>
    public void PrepareGuardGenerators(GameTask? levelRoot)
    {
        GuardGenerators.Clear();
        _drivenGenerators.Clear();
        if (levelRoot is null)
        {
            return;
        }

        foreach (GuardGeneratorTask generator in FindGuardGenerators(levelRoot))
        {
            HumanSoldierTask? authored = generator.Children.OfType<HumanSoldierTask>().FirstOrDefault();
            SoldierView? view = authored is null
                ? null
                : _context.Brains.Keys.FirstOrDefault(candidate => ReferenceEquals(candidate.Soldier, authored));
            if (view is null && authored is { Driver: not null })
            {
                // Another game drives the template: the generator decides, that game's lives spawn.
                _drivenGenerators.Add(generator);
                continue;
            }

            if (view is null)
            {
                _context.Logger.Warning(
                    SoldierContext.LogCategory,
                    $"{generator.Name} has no resolved HumanSoldier child; its authored spawn cannot run.");
                continue;
            }

            GuardGenerators[generator] = view;
        }

        if (GuardGenerators.Count > 0)
        {
            _context.Logger.Information(
                SoldierContext.LogCategory,
                $"{GuardGenerators.Count} GuardGenerator(s) own bounded authored soldier templates.");
        }
    }

    /// <summary>A GuardGenerator's update (<c>0x487EE0</c>): its expression, and a replacement for a dead guard.</summary>
    public void TickGuardGenerator(GuardGeneratorTask generator)
    {
        bool isOn = GuardExpressions?.EvaluateCondition(generator.GenerateGuards, emptyValue: false, generator)
            ?? generator.GenerateGuards.Trim() is "1" or "TRUE" or "true";
        if (generator.DrivenLives is { } lives)
        {
            // 0x487EE0 for another game's template: the first life when it has none, the next as the last lies dead.
            if (generator.Advance(isOn, childIsDead: lives.IsDead) is GuardSpawnDecision.FirstSpawn or GuardSpawnDecision.Respawn)
            {
                lives.Spawn();
            }

            return;
        }

        if (!GuardGenerators.TryGetValue(generator, out SoldierView? view))
        {
            return;
        }

        // Publish the first authored life immediately, but do not consume a bounded replacement
        // until its mesh and runtime subtree have actually been created. A transient upload failure
        // must leave the generator eligible to retry on the next tick.
        GuardSpawnDecision decision = generator.Advance(isOn, childIsDead: false);

        // A REPLACEMENT WAITS FOR THE BODY TO LEAVE, and the body leaves ten seconds after it lands. The generator replaces
        // its guard only once he is dead (Human+3317) AND out of the world (+3318 clear, 0x463190), deleting the body as it
        // clones (0x487EE0 -> 0x4015F0, 0x488000). What takes him out is his own HumanAI (0x44F410): once his death clip has
        // run (+828, which 0x45C740 sets on the tick after the clip ends) it counts AI+15680 -- 300, written only by the
        // constructor -- down past nought, and then, because his parent is a GuardGenerator (0x488610), unregisters him
        // (0x4609A0). So: clip ends on tick E, the countdown runs E+1..E+301, the body goes on E+302 whether the generator is
        // on or spent, and the replacement comes on E+303. Probed on mission 9's generator 700.
        if (decision == GuardSpawnDecision.FirstSpawn || !view.IsDead)
        {
            return;
        }

        // This runs before the soldier's own tick, so a clip that ended is the last tick's.
        if (!_generatedDeathEnded.ContainsKey(generator) && view.ClipEndedThisTick)
        {
            _generatedDeathEnded[generator] = _context.TickCount - 1;
        }

        if (!_generatedDeathEnded.TryGetValue(generator, out int ended))
        {
            return;
        }

        if (!_generatedBodyGone.ContainsKey(generator)
            && _context.TickCount >= ended + SoldierSimulation.GeneratedBodyLingerTicks + 2
            && RemoveGeneratedBody(view) is { } removed)
        {
            _generatedBodyGone[generator] = removed;
        }

        if (_generatedBodyGone.TryGetValue(generator, out (SoldierAgent Agent, SoldierModelGroup Group) body)
            && _context.TickCount >= ended + SoldierSimulation.GeneratedBodyLingerTicks + 3
            && isOn
            && generator.SpawnCount < generator.MaxSpawns
            && SpawnGeneratedGuard(generator, view, body.Agent, body.Group) is { } fresh)
        {
            _ = generator.Advance(isOn: true, childIsDead: true);
            GuardGenerators[generator] = fresh;
            _generatedDeathEnded.Remove(generator);
            _generatedBodyGone.Remove(generator);
        }
    }

    private static IEnumerable<GuardGeneratorTask> FindGuardGenerators(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is GuardGeneratorTask generator)
            {
                yield return generator;
            }

            foreach (GuardGeneratorTask nested in FindGuardGenerators(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Clones a fresh Human/HumanAI subtree while leaving the killed body in the world.</summary>
    /// <remarks>
    /// The replacement is made only once its renderer, when there is one, has its drawing ready (<see cref="SpawnedBody"/>):
    /// a transient upload failure leaves the generator free to try again next tick.
    /// </remarks>
    private SoldierView? SpawnGeneratedGuard(GuardGeneratorTask generator, SoldierView deadView, SoldierAgent deadAgent, SoldierModelGroup group)
    {
        if (!_context.Brains.TryGetValue(deadView, out Igi1HumanAi? deadBrain))
        {
            _context.Logger.Warning(SoldierContext.LogCategory, $"{generator.Name} could not clone its missing rendered Human subtree.");
            return null;
        }

        SoldierView freshView = new(deadView.Soldier, deadView.Bones.Set);
        Vector3d spawn = new(
            deadAgent.Position.X * WorldScene.UnitsPerMetre,
            deadAgent.Position.Y * WorldScene.UnitsPerMetre,
            deadAgent.Position.Z * WorldScene.UnitsPerMetre);
        freshView.PlaceAt(spawn, Matrix3f.FromEngineEulerAngles(0f, 0f, deadView.Soldier.Gamma));

        SoldierAgent freshAgent = new(freshView, deadAgent.Position)
        {
            IsCutsceneActor = false,
            WeaponType = deadAgent.WeaponType,
            AmmoInClip = deadAgent.WeaponType?.RoundsPerClip ?? 0,
        };
        if (SpawnedBody is { } drawing && !drawing(generator, group, freshAgent, deadAgent))
        {
            return null;
        }

        group.Add(freshAgent);

        if (_context.DetectorMarkerScaleMetres.TryGetValue(deadView, out float markerScale))
        {
            _context.DetectorMarkerScaleMetres[freshView] = markerScale;
        }
        if (_context.Combat.TryGetValue(deadView, out AiCombatParameters? combat))
        {
            _context.Combat[freshView] = combat;
        }
        _context.Health[freshView] = _context.NewHealth(freshView);

        Igi1HumanAi freshBrain = deadBrain.Respawned(
            freshView.Position,
            freshView.Soldier.Gamma,
            brain => SoldierWeaponFire.CombatWeaponState(brain, freshAgent));
        // Each live instance has its own runtime ID, so nothing keyed by the old guard's -- its weapon sound, the replies to its
        // alerts -- is taken for the replacement's. Keep the authored Soldier.Id for mission queries.
        freshBrain.RuntimeId = checked(_context.BrainsByRuntimeId.Keys.DefaultIfEmpty(0).Max() + 1);
        _sight.BindViewTracking(freshBrain, freshView);
        freshBrain.SetDefaultTarget(_context.PlayerTarget);
        _context.Brains[freshView] = freshBrain;
        _context.BrainsByRuntimeId[freshBrain.RuntimeId] = freshBrain;
        _enlist(freshView);

        // 0x487EE0 deletes the dead child (0x4015F0) as it clones the next; its body has already left the world. The task it
        // stood for is the replacement's, so the human list keeps it.
        _context.Brains.Remove(deadView);
        _context.Health.Remove(deadView);
        _context.Combat.Remove(deadView);
        _context.DetectorMarkerScaleMetres.Remove(deadView);

        _context.Logger.Information(
            SoldierContext.LogCategory,
            $"{generator.Name} spawned {generator.SpawnCount}/{generator.MaxSpawns}: "
                + $"{freshView.Soldier.Name} cloned at its authored position.");
        return freshView;
    }

    /// <summary>Takes a generated guard's body out of the world, as his HumanAI's unregister does.</summary>
    /// <remarks>
    /// Every draw, shadow, hit test and sweep walks the roster, so leaving it is leaving the world. The brain stays until the
    /// replacement is cloned from it.
    /// </remarks>
    private (SoldierAgent Agent, SoldierModelGroup Group)? RemoveGeneratedBody(SoldierView view)
    {
        if (_context.FindAgent(view) is not { } agent
            || _context.Roster.FirstOrDefault(candidate => candidate.Agents.Contains(agent)) is not { } group)
        {
            return null;
        }

        group.Remove(agent);
        if (_context.Brains.TryGetValue(view, out Igi1HumanAi? brain))
        {
            _context.BrainsByRuntimeId.Remove(brain.RuntimeId);
        }

        BodyRemoved?.Invoke(agent);
        return (agent, group);
    }
}

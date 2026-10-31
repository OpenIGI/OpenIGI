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

    /// <summary>Gets each generator this game drives, by the soldier it owns now.</summary>
    public Dictionary<GuardGeneratorTask, SoldierView> GuardGenerators { get; } = [];

    /// <summary>Gets every generator, this game's then the driven ones, in the order the walk binds and ticks them.</summary>
    public IEnumerable<GuardGeneratorTask> AllGenerators => GuardGenerators.Keys.Concat(_drivenGenerators);

    /// <summary>Gets or sets the expressions a generator's <c>Generate guards</c> condition is evaluated with.</summary>
    public LevelExpressionRuntime? GuardExpressions { get; set; }

    /// <summary>Gets or sets what prepares a generator's replacement guard to be drawn, and says whether it could.</summary>
    public Func<GuardGeneratorTask, SoldierModelGroup, SoldierAgent, SoldierAgent, bool>? SpawnedBody { get; set; }

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

        // A REPLACEMENT WAITS FOR THE BODY TO LEAVE. The generator replaces its guard only once he is dead (Human+3317) AND
        // out of the world (+3318 clear, 0x463190), deleting the body as it clones (0x487EE0 -> 0x4015F0, 0x488000). What
        // takes him out is his own HumanAI (Igi1HumanAi.TickDeadAi): a generator's body goes the tick its stay runs out,
        // in sight or not. Clip ends on tick E, the body goes on E+302, and -- the generator updating before its child --
        // the replacement comes on E+303. Probed on mission 9's generator 700.
        if (decision == GuardSpawnDecision.FirstSpawn
            || !view.IsDead
            || !_context.GoneBodies.TryGetValue(view, out (SoldierAgent Agent, SoldierModelGroup Group) body))
        {
            return;
        }

        if (isOn
            && generator.SpawnCount < generator.MaxSpawns
            && SpawnGeneratedGuard(generator, view, body.Agent, body.Group) is { } fresh)
        {
            _ = generator.Advance(isOn: true, childIsDead: true);
            GuardGenerators[generator] = fresh;
        }
    }

    /// <summary>Whether a soldier is a generator's: its body leaves the world in sight or not (<c>0x488610</c> parent test).</summary>
    public bool IsGenerated(SoldierView view) => GuardGenerators.ContainsValue(view);

    /// <summary>Gets the generator whose current life a soldier is, or <see langword="null"/>.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>The generator.</returns>
    public GuardGeneratorTask? GeneratorOf(SoldierView view)
    {
        foreach ((GuardGeneratorTask generator, SoldierView life) in GuardGenerators)
        {
            if (ReferenceEquals(life, view))
            {
                return generator;
            }
        }

        return null;
    }

    /// <summary>A co-op client's replacement for a generator's dead guard, made when the server made its own.</summary>
    /// <param name="generatorId">The generator's task id.</param>
    /// <param name="runtimeId">The runtime id the server gave its replacement, which this one takes.</param>
    /// <returns>The replacement, or <see langword="null"/> when there is nothing to replace.</returns>
    /// <remarks>
    /// A client's guards are the server's puppets and run no mind, so a dead one never takes its own body out of the world
    /// and the client's generator never replaces it: the server's snapshot naming a guard the client hasn't is the cue.
    /// </remarks>
    public SoldierView? ReplaceAsServerDid(int generatorId, int runtimeId)
    {
        GuardGeneratorTask? generator = GuardGenerators.Keys.FirstOrDefault(g => g.Id == generatorId);
        if (generator is null || !GuardGenerators.TryGetValue(generator, out SoldierView? dead))
        {
            return null;
        }

        // The server's dead guard left the world the tick before it cloned the next; this client's, which runs no mind to
        // take it out, goes now -- or its body would lie on in the world beside its replacement, both standing for one task.
        // Gone already when a clone that failed is tried again.
        if (!_context.GoneBodies.ContainsKey(dead))
        {
            _context.RemoveBody(dead);
        }

        if (!_context.GoneBodies.TryGetValue(dead, out (SoldierAgent Agent, SoldierModelGroup Group) body)
            || SpawnGeneratedGuard(generator, dead, body.Agent, body.Group, runtimeId) is not { } fresh)
        {
            return null;
        }

        _ = generator.Advance(isOn: true, childIsDead: true);
        GuardGenerators[generator] = fresh;
        return fresh;
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
    private SoldierView? SpawnGeneratedGuard(GuardGeneratorTask generator, SoldierView deadView, SoldierAgent deadAgent, SoldierModelGroup group, int? runtimeId = null)
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

        // The clone's task is the one the body left; putting it in the world sets +3318 again (0x460950).
        deadView.Soldier.IsOutOfWorld = false;

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
        freshBrain.RuntimeId = runtimeId ?? checked(_context.BrainsByRuntimeId.Keys.DefaultIfEmpty(0).Max() + 1);
        _sight.BindViewTracking(freshBrain, freshView);
        freshBrain.SetDefaultTarget(_context.PlayerTarget);
        _context.Brains[freshView] = freshBrain;
        _context.BrainsByRuntimeId[freshBrain.RuntimeId] = freshBrain;
        _enlist(freshView);

        // 0x487EE0 deletes the dead child (0x4015F0) as it clones the next; its body has already left the world. The task it
        // stood for is the replacement's, so the human list keeps it.
        _context.Brains.Remove(deadView);
        _context.GoneBodies.Remove(deadView);
        _context.Health.Remove(deadView);
        _context.Combat.Remove(deadView);
        _context.DetectorMarkerScaleMetres.Remove(deadView);

        _context.Logger.Information(
            SoldierContext.LogCategory,
            $"{generator.Name} spawned {generator.SpawnCount}/{generator.MaxSpawns}: "
                + $"{freshView.Soldier.Name} cloned at its authored position.");
        return freshView;
    }
}

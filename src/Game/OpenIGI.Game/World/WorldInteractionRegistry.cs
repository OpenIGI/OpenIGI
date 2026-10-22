using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Level;

#pragma warning disable CS1591 // Public data members are documented by the owning interaction contract.

namespace OpenIGI.Game.World;

/// <summary>
/// All direct retail world interactions, with the original two-metre, most-head-on resolver.
/// </summary>
public sealed class WorldInteractionRegistry : IPlacementVariant
{
    /// <summary><c>Human_UpdateActivate</c>'s regular query radius: two metres.</summary>
    public const double ActivateRadius = 8192.0;

    private readonly List<WorldInteraction> _items = [];
    private readonly List<GeneratorPowerState> _generators = [];
    private int _updates;
    private Dictionary<GameTask, WorldInteraction>? _byTask;
    private bool _inWalk;
    private readonly List<RadioRuntime> _radios = [];

    // The renderer asks after every placement each frame; only Mission 6 has anything to answer.
    private bool _hasPlacedTasks;

    public IReadOnlyList<WorldInteraction> Items => _items;

    /// <summary>Gets the level's <c>Radio</c> tasks.</summary>
    public IReadOnlyList<RadioRuntime> Radios => _radios;

    public static WorldInteractionRegistry Build(
        GameTask root,
        LoadedMission mission,
        DoorRegistry doors,
        ScriptVarRegistry variables,
        ElevatorRegistry? elevators = null,
        IgiRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(variables);

        Dictionary<string, MefModel> models = WorldInteractionCollector.ModelsByName(mission);
        LevelExpressionRuntime expressions = new(variables);
        WorldInteractionRegistry registry = new();
        WorldInteractionCollector.CollectGenerators(root, registry, variables, expressions);
        WorldInteractionCollector.CollectRadios(root, registry, variables, expressions, random ?? new IgiRandom());
        WorldInteractionCollector.Collect(root, registry, models, doors, variables, expressions, elevators);
        return registry;
    }

    /// <summary>Adds a collected interaction, in the order the task tree holds it.</summary>
    internal void Add(WorldInteraction item)
    {
        _items.Add(item);
        _hasPlacedTasks |= item.PlacedTaskId >= 0;
    }

    /// <summary>Adds a collected generator.</summary>
    internal void AddGenerator(GeneratorPowerState generator) => _generators.Add(generator);

    /// <summary>Adds a collected radio.</summary>
    internal void AddRadio(RadioRuntime radio) => _radios.Add(radio);

    /// <summary>Settles every generator whose ExplodeObject exploded after this tick's <see cref="Tick"/>.</summary>
    public void SettleGenerators()
    {
        foreach (GeneratorPowerState generator in _generators)
        {
            if (generator.IsInWorld())
            {
                generator.Settle();
            }
        }
    }

    /// <summary>Runs every radio's own update, <c>0x43AF90</c>.</summary>
    /// <param name="sounds">Where their sounds go, or <see langword="null"/> when nothing can play.</param>
    /// <remarks>
    /// After the destructibles have ticked: a Radio's update calls the ExplodeObject update first, so an
    /// explosion and the radio going quiet share a tick.
    /// </remarks>
    public void TickRadios(IRadioSoundPlayer? sounds)
    {
        foreach (RadioRuntime radio in _radios)
        {
            radio.Tick(sounds);
        }
    }

    /// <summary>
    /// Puts every task into the level's update walk, each where the tree places it and in its own update's order: a switch,
    /// terminal, cabinet and generator run their children first (an ExplodeObject's update is its children, then its blast);
    /// a GenericTBA runs them only when it is Explodable, as only then does it run ExplodeObject's update (<c>0x4480B0</c>); a
    /// fence (<c>0x43FC30</c>) and a stationary gun (<c>0x474190</c>) run them last; a wire (<c>0x43F340</c>) not at all. A
    /// door is its own registry's.
    /// </summary>
    /// <param name="walker">The level walk.</param>
    /// <remarks>Once attached, <see cref="Tick"/> does nothing: the walk is the update.</remarks>
    public void AttachTo(LevelRun walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        foreach (GeneratorPowerState generator in _generators)
        {
            walker.Bind(generator.Task, task =>
            {
                walker.RunChildren(task);
                generator.Tick();
            });
        }

        foreach (WorldInteraction item in _items)
        {
            switch (item.Type)
            {
                case InteractionActivateType.Door:
                    break;
                case InteractionActivateType.Fence:
                    walker.Bind(item.Task, task =>
                    {
                        item.Run(Now);
                        walker.RunChildren(task);
                    });
                    break;
                case InteractionActivateType.StationaryGun:
                    walker.Bind(item.Task, task =>
                    {
                        item.Run(Now);
                        if (item.StationaryGun is { IsOccupied: false } emptyGun)
                        {
                            emptyGun.TickEmpty();
                        }

                        walker.RunChildren(task);
                    });
                    break;
                case InteractionActivateType.Wire:
                    walker.Bind(item.Task, () => item.Run(Now));
                    break;
                case InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive when !item.IsExplodable:
                    walker.Bind(item.Task, () => item.Run(Now));
                    break;
                default:
                    walker.Bind(item.Task, task =>
                    {
                        walker.RunChildren(task);
                        item.Run(Now);
                    });
                    break;
            }
        }

        // The level counts its tick once its tree has run; a registry without the level's clock counts with it.
        walker.AddTickEnd(() => _updates++);
        _inWalk = true;
    }

    /// <summary>Advances task pulses and <c>Last</c> script shadows once at 30 Hz, for a level whose walk does not run them.</summary>
    public void Tick()
    {
        if (_inWalk)
        {
            return;
        }

        _updates++;
        int now = Now;

        // A task in a container that is not running is not in the world: the walk does not reach it, so it does not
        // update, and nothing can be activated on it.
        foreach (GeneratorPowerState generator in _generators)
        {
            if (generator.IsInWorld())
            {
                generator.Tick();
            }
        }

        foreach (WorldInteraction item in _items)
        {
            if (!item.IsInWorld)
            {
                continue;
            }

            item.Run(now);

            // An empty gun's aim sinks on its own tick (0x474190). An occupied one is ticked by
            // whoever is on it, with their look.
            if (item.StationaryGun is { IsOccupied: false } emptyGun)
            {
                emptyGun.TickEmpty();
            }
        }
    }

    /// <summary>
    /// Gets or sets the level's tick, LevelFlow's <c>nTick</c> (<c>+104</c> of the task <c>flt_BC2360</c> points at), which
    /// the tasks stamp their pulses with; <see langword="null"/>, or a clock that answers <see langword="null"/>, counts this
    /// registry's own updates instead.
    /// </summary>
    public Func<int?>? Clock { get; set; }

    /// <summary>Gets or sets where the tasks' sounds go: the switches' clicks, the generators' hum, the electric fences' buzz.</summary>
    public Action<InteractionSoundEvent>? Sounds { get; set; }

    // The tick a press or a use made between two updates is stamped with: the level's, or the updates counted so far.
    internal int Now => Clock?.Invoke() ?? _updates;

    /// <summary>Applies the task activation produced by an AI body's activate input channel.</summary>
    /// <param name="taskId">The level task id named by <c>AIAction_Activate</c>.</param>
    /// <returns><see langword="true"/> when the id names a direct interaction.</returns>
    public bool ActivateFromAi(int taskId)
    {
        WorldInteraction? item = _items.FirstOrDefault(candidate => candidate.Task.Id == taskId);
        if (item is null)
        {
            return false;
        }

        switch (item.Type)
        {
            case InteractionActivateType.Switch:
                item.Press();
                break;
            case InteractionActivateType.Door:
                item.Door?.Toggle();
                break;
            case InteractionActivateType.StationaryGun:
                // AIAction_Activate reaches the gunner's own mount state. Player interaction's
                // BeginUse/CompleteUse pair uses a private player sentinel and would dismount it.
                break;
            default:
                // 0x454DA0 / 0x459D50 press a switch and toggle a door, and otherwise only use what they hold: an AI's
                // activate never hacks a terminal, searches a cabinet or finishes a GenericTBA.
                item.Use();
                break;
        }

        return true;
    }

    /// <summary>Resolves the live stationary-gun task a HumanAI's gunner id names.</summary>
    public AiStationaryGun? FindStationaryGun(int taskId) =>
        _items.FirstOrDefault(item => item.Task.Id == taskId)?.StationaryGun;

    /// <summary>Finds the interaction owned by a placed task.</summary>
    public bool TryGet(GameTask task, out WorldInteraction? interaction)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_byTask is null)
        {
            // The first item a task owns, as a search of the list in its order would find.
            _byTask = new Dictionary<GameTask, WorldInteraction>(ReferenceEqualityComparer.Instance);
            foreach (WorldInteraction item in _items)
            {
                _byTask.TryAdd(item.Task, item);
            }
        }

        return _byTask.TryGetValue(task, out interaction);
    }

    /// <inheritdoc/>
    /// <remarks>A switch wears the model its update last picked (<see cref="WorldInteraction.ModelSlot"/>).</remarks>
    public int VariantOf(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return placement.Variants is not null
            && placement.Owner is { } owner
            && TryGet(owner, out WorldInteraction? item)
            && item!.Type == InteractionActivateType.Switch
                ? item.ModelSlot
                : -1;
    }

    /// <summary>Hands a round's <c>WeaponHit</c> to the interaction a task is, if it is one.</summary>
    /// <param name="task">The task the round struck.</param>
    /// <returns>Whether the round broke it.</returns>
    public bool WeaponHit(GameTask? task) =>
        task is not null && TryGet(task, out WorldInteraction? item) && item!.WeaponHit();

    /// <summary>Where a plant moved the task a PlaceExplosiveTBA places, if one has.</summary>
    /// <param name="task">The placed task (a charge model's EditRigidObj).</param>
    /// <param name="position">Its position, in world units.</param>
    /// <param name="orientation">Its orientation.</param>
    /// <returns>Whether a plant has moved this task.</returns>
    public bool TryGetPlacedPose(GameTask? task, out Vector3d position, out Matrix3f orientation)
    {
        if (task is not null && _hasPlacedTasks)
        {
            foreach (WorldInteraction item in _items)
            {
                if (item.PlacedTaskId == task.Id && item.PlacedPosition is { } placed)
                {
                    position = placed;
                    orientation = item.PlacedOrientation;
                    return true;
                }
            }
        }

        position = default;
        orientation = Matrix3f.Identity;
        return false;
    }

    /// <summary>Finds a powered electric fence touched by a player movement segment.</summary>
    public WorldInteraction? FindElectrifiedFenceContact(Vector3d from, Vector3d to) =>
        _items.FirstOrDefault(item => item.IsInWorld && item.SegmentTouchesFence(from, to));

    /// <summary>Finds the powered fence whose collision volume contains a nearby player centre.</summary>
    public WorldInteraction? FindElectrifiedFenceNear(Vector3d point) =>
        _items.FirstOrDefault(item => item.IsInWorld && item.PointTouchesFence(point));

    /// <summary>Finds the eligible task nearest the centre of the player's aim.</summary>
    public WorldInteraction? Resolve(Vector3d eye, float yaw, float pitch = 0f)
        => Resolve(eye, yaw, pitch, out _);

    /// <summary>Finds the eligible task and returns its off-axis angle for cross-family arbitration.</summary>
    public WorldInteraction? Resolve(Vector3d eye, float yaw, float pitch, out double resolvedAngle)
    {
        Vector3d look = new(
            Math.Sin(yaw) * Math.Cos(pitch),
            Math.Cos(yaw) * Math.Cos(pitch),
            Math.Sin(pitch));
        WorldInteraction? best = null;
        double bestAngle = double.MaxValue;

        foreach (WorldInteraction item in _items)
        {
            if (!item.IsInWorld || !item.IsAvailable || !item.CanOfferFrom(eye))
            {
                continue;
            }

            double radius = item.Type == InteractionActivateType.PlaceExplosive
                ? Math.Max(
                    ActivateRadius,
                    DeclaredTaskValues.RealFromNumber(
                        item.Task,
                        "Activate radius (meters)",
                        DeclaredTaskValues.RealFromNumber(item.Task, "Activate radius(meters)", 2f)) * WorldScene.UnitsPerMetre)
                : ActivateRadius;
            Consider(item, item.CandidatePoint(eye), radius);
            if (item.Type == InteractionActivateType.Door)
            {
                Consider(item, item.Anchor, radius);
            }
        }

        resolvedAngle = bestAngle;
        return best;

        void Consider(WorldInteraction item, Vector3d point, double radius)
        {
            Vector3d delta = point - eye;
            double distance = delta.Length;
            if (distance <= 0.0 || distance >= radius)
            {
                return;
            }

            double cosine = Vector3d.Dot(delta / distance, look);
            if (cosine <= 0.0)
            {
                return;
            }

            double angle = Math.Acos(Math.Clamp(cosine, -1.0, 1.0));
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = item;
            }
        }
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>
/// An I.G.I. 2 level of squads and soldiers hosted inside another game's level: its tasks made in a guest session of
/// I.G.I. 2, then brought up as I.G.I. 2 brings its own -- the bodies, the detectors, the minds and the level's walk.
/// </summary>
/// <remarks>
/// <para>
/// The host decides who stands where; this does what I.G.I. 2's own level build does with them, in its order
/// (<see cref="Igi2ScriptSystems"/>): the soldiers spawned (<see cref="Igi2Gameplay.SpawnSoldiers"/>), the detectors listing
/// them and the player, the minds given their scripts and graphs (<see cref="Igi2LevelAi.Build"/>), every human's AI events
/// sent to the level's AI, and the level run that walks it.
/// </para>
/// <para>
/// Each guest task keeps the id the host asks for when the guest has not given it out, else the next spare id from the top:
/// a mind finds its script by its id, and one without an id would run none.
/// </para>
/// </remarks>
public sealed class Igi2GuestLevel
{
    private const int TopId = 4095;

    private readonly Igi2GuestContent _content;
    private readonly GuestTitle _guest;
    private readonly List<Interaction.Igi2AlarmControlTask> _alarms = [];
    private int _spare = TopId;

    private Igi2GuestLevel(Igi2GuestContent content, LevelTask root, GameTask dynamic)
    {
        _content = content;
        _guest = content.Guest;
        Root = root;
        Dynamic = dynamic;
    }

    /// <summary>Gets the level's root, in the guest's task tree.</summary>
    public LevelTask Root { get; }

    /// <summary>Gets the level's dynamic container, which its squads are made under.</summary>
    public GameTask Dynamic { get; }

    /// <summary>Opens a level in a guest session of I.G.I. 2: its root and its dynamic container.</summary>
    /// <param name="content">What the level's soldiers are made of, in the guest session it was read in.</param>
    /// <returns>The level, empty.</returns>
    public static Igi2GuestLevel Open(Igi2GuestContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Engine.Flow.GameLoop loop = content.Guest.Loop;
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        GameTask dynamic = loop.Tasks.New(root, loop.Types.Get(EngineTaskTypes.Dynamic));
        return new Igi2GuestLevel(content, root, dynamic);
    }

    /// <summary>Adds a squad to the level, under its dynamic container, at a spare id.</summary>
    /// <returns>The squad.</returns>
    public Igi2AiSquadTask AddSquad()
    {
        Igi2AiSquadTask squad = (Igi2AiSquadTask)New(Dynamic, Igi2AiSquadTask.TypeName);
        Bind(squad, -1);
        return squad;
    }

    /// <summary>Adds a soldier to a squad, at the id the host asks for where it is free.</summary>
    /// <param name="squad">The squad.</param>
    /// <param name="preferredId">The id wanted, or a negative one for any.</param>
    /// <returns>The soldier, for the host to place, dress and arm.</returns>
    public Igi2HumanSoldierTask AddSoldier(Igi2AiSquadTask squad, int preferredId)
    {
        ArgumentNullException.ThrowIfNull(squad);
        Igi2HumanSoldierTask soldier = (Igi2HumanSoldierTask)New(squad, Igi2HumanSoldierTask.TypeName);
        Bind(soldier, preferredId);
        return soldier;
    }

    /// <summary>Adds a soldier's mind, at the id the host asks for where it is free.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <param name="preferredId">The id wanted, or a negative one for any.</param>
    /// <returns>The mind, for the host to give its type and graph.</returns>
    public Igi2HumanAiTask AddMind(Igi2HumanSoldierTask soldier, int preferredId)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        Igi2HumanAiTask mind = (Igi2HumanAiTask)New(soldier, Igi2HumanAiTask.TypeName);
        Bind(mind, preferredId);
        return mind;
    }

    /// <summary>
    /// Adds an alarm of the host's to the level, as I.G.I. 2's soldiers raise and hear their own: a control whose alarm is the
    /// host's, and, where the host's has one, a trigger where the host's is, which a soldier the squad sends presses
    /// (<c>AISquad_TriggerAlarm</c>).
    /// </summary>
    /// <param name="controlId">The host's alarm control's id, which the squads that run for it name (<c>AlarmControlId</c>).</param>
    /// <param name="isOn">Whether the host's alarm is sounding this tick.</param>
    /// <param name="trigger">
    /// The host's alarm trigger: its id, which the squads press (<c>AlarmTriggerId</c>), where it is in world units, and what
    /// presses the host's own, as one of its guards presses it; <see langword="null"/> for an alarm the soldiers only hear.
    /// </param>
    /// <returns>Whether the alarm was added; not where an id is the guest's already.</returns>
    /// <remarks>
    /// The control's alarm expression reads the host's alarm, so as it starts and stops the control tells every soldier
    /// (<c>AIEVENT_ALARMON</c>, <c>AIEVENT_ALARMOFF</c>), as I.G.I. 2's own does, whoever raised it.
    /// </remarks>
    public bool AddAlarm(int controlId, Func<bool> isOn, (int Id, Vector3d Position, Action Pressed)? trigger = null)
    {
        ArgumentNullException.ThrowIfNull(isOn);
        TaskTree tasks = _guest.Loop.Tasks;
        if (tasks.FromId(controlId) is not null || trigger is { } named && (named.Id == controlId || tasks.FromId(named.Id) is not null))
        {
            return false;
        }

        Interaction.Igi2AlarmControlTask control = (Interaction.Igi2AlarmControlTask)New(Root, Interaction.Igi2AlarmControlTask.TypeName);
        string variable = $"HostAlarm_{controlId}.isOn";
        _guest.Loop.Natives.RegisterVariable(variable, new QvmIntegerVariable(() => isOn() ? 1 : 0, static _ => { }));
        control.AlarmScript = variable;
        if (!tasks.TryBindId(controlId, control))
        {
            tasks.Delete(control);
            return false;
        }

        if (trigger is { } pressed)
        {
            Interaction.Igi2SwitchTask button = (Interaction.Igi2SwitchTask)New(Root, Interaction.Igi2SwitchTask.TypeName);
            button.Position = pressed.Position;
            button.Pressed = pressed.Pressed;
            control.Position = pressed.Position;
            if (!tasks.TryBindId(pressed.Id, button))
            {
                tasks.Delete(button);
                tasks.Delete(control);
                return false;
            }
        }

        _alarms.Add(control);
        return true;
    }

    /// <summary>The ticks a host's patrol animation pauses its soldier for: two seconds, about what a retail one takes.</summary>
    public const int AnimationPauseTicks = 60;

    /// <summary>
    /// Adds a patrol path to the level under the id the host gives it, which a soldier's script sends it on by
    /// (<c>AIAction_Patrol</c>): its commands in order, each as I.G.I. 2 keeps one.
    /// </summary>
    /// <param name="id">The path's id.</param>
    /// <param name="commands">Its commands, in the host's terms.</param>
    /// <returns>The path; <see langword="null"/> where the id is the guest's already, and no script could reach the path by it.</returns>
    /// <remarks>
    /// I.G.I. 2 reads a command's node, its delay or its animation from its <c>Command Parameter</c>
    /// (<c>HumanAIContent_PatrolRunCB</c>), as Project I.G.I. reads its operand, so each command keeps its kind and operand. An
    /// animation is the host's own number, which names nothing of I.G.I. 2's: it is a pause of about the length a retail
    /// patrol's animation takes instead (<see cref="AnimationPauseTicks"/>).
    /// </remarks>
    public Igi2PatrolPathTask? AddPatrolPath(int id, IEnumerable<AiPatrolCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        TaskTree tasks = _guest.Loop.Tasks;
        if (tasks.FromId(id) is not null)
        {
            return null;
        }

        Igi2PatrolPathTask path = (Igi2PatrolPathTask)New(Root, Igi2PatrolPathTask.TypeName);
        if (!tasks.TryBindId(id, path))
        {
            tasks.Delete(path);
            return null;
        }

        foreach (AiPatrolCommand command in commands)
        {
            Igi2PatrolPathCommandTask step = (Igi2PatrolPathCommandTask)New(path, Igi2PatrolPathCommandTask.TypeName);
            bool animation = command.Kind == AiPatrolCommandKind.Animation;
            step.CommandValue = animation ? (int)Igi2PatrolCommand.Delay : (int)command.Kind;
            step.Parameter = animation ? AnimationPauseTicks : command.Operand;
        }

        return path;
    }

    /// <summary>Brings the level up, as I.G.I. 2 brings its own: the bodies, the detectors, the minds and the run.</summary>
    /// <param name="start">What the host gives it.</param>
    /// <returns>The level, running, for the host to have its soldiers stand for its humans and then <see cref="Igi2HostedLevel.Begin"/>.</returns>
    public Igi2HostedLevel Start(Igi2GuestLevelStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        Engine.Flow.GameLoop loop = _guest.Loop;
        IServiceCatalog services = _guest.Session.Services;
        Igi2ScriptFunctions functions = services.Require<Igi2ScriptFunctions>();
        Igi2HostedLevel? hosted = null;
        Func<int> ticks = () => hosted?.Ticks ?? 0;

        Igi2Sounds sounds = new(_content.SoundDefinitions, start.Audio);
        Igi2GameMaterials materials = services.Require<Igi2GameMaterials>();
        Igi2AiWorld levelAi = services.Require<Igi2AiWorld>();
        Igi2DynamicTasks dynamic = new();

        // The level's world as I.G.I. 2 brings its own up (Igi2Gameplay.CreatePlayer): the blasts and what flies through
        // them, the pickups a soldier drops, and the casings its weapon throws.
        Igi2ExplosionWorld explosions = new(start.Random, start.World, materials, dynamic)
        {
            Effects = new Igi2LevelExplosionEffects(sounds, levelAi),
        };
        Igi2Grenades? grenades = null;
        if (services.TryGet(out PhysicsObjectTypes? physicsTypes))
        {
            Physics.Igi2PhysicsWorld flight = new(start.World, materials, static owner => !Collision.Igi2ObjectCollision.PassesThrough(owner));
            grenades = new Igi2Grenades(physicsTypes, _content.Models, flight, dynamic, start.Logger)
            {
                Effects = explosions,
                Explosions = explosions,
            };
            if (services.TryGet(out MissileRandom? missileStream))
            {
                grenades.MissileStream = missileStream.Random;
            }
        }

        Pickups.Igi2PickupList? pickups = null;
        if (services.TryGet(out Igi2HumanWeapons? weaponTables))
        {
            pickups = Pickups.Igi2PickupList.Build(Root, weaponTables.Tables, _content.Models, start.World, materials);
            pickups.Collision = start.World;
            pickups.Dynamic = dynamic;
            pickups.Random = start.Random;
            pickups.PlaySound = (name, at) => sounds.New(name, at);
        }

        Igi2GunCasings casings = new(_content.Models);
        Soldiers.Igi2Soldiers soldiers = Igi2Gameplay.SpawnSoldiers(
            Root,
            services,
            start.World,
            _content.Clips,
            _content.Models,
            services.TryGet(out Igi2PlayerTuning? tuning) ? tuning : null,
            sounds,
            start.Random,
            start.Difficulty,
            pickups,
            grenades,
            loop.Natives,
            functions,
            worn: null,
            casings);

        // Human_ptQTaskList: every human of the level, the bodies each move is kept out of; and one particle system for all.
        List<Humans.Igi2Human> humans = [.. soldiers.Bodies];
        Igi2Particles particles = new(start.World, dynamic);
        explosions.Humans = humans;
        explosions.Particles = particles;
        foreach (Humans.Igi2Human human in humans)
        {
            if (human.Surroundings is Igi2LevelSurroundings surroundings)
            {
                surroundings.Humans = humans;
                surroundings.Particles = particles;
            }
        }

        // A soldier out of the world -- held by its host's container, or a generator's life not yet spawned -- is struck by no blast.
        // And one whose mind has no graph of the host's to stand it on finds the host's ground by casting, as a body does.
        foreach (Soldiers.Igi2HumanSoldier body in soldiers.Bodies)
        {
            explosions.AddTarget(new ActiveBody(body));
            body.CastsForGroundOffGraph = true;
        }

        // The host's player is struck by the soldiers' blasts as by their rounds.
        explosions.AddTarget(start.Player);

        // What the host's own level adds to the guest's world, after the guest's bodies: its player among them.
        foreach (IWorldLineCollider collider in start.Colliders)
        {
            start.World.AddCollider(collider);
        }

        Igi2DetectionWorld detection = new(start.World, services.Require<Igi2GameMaterials>())
        {
            FlowTicks = ticks,
            LevelTick = ticks,
        };
        detection.Detectables.Add(start.Player);
        foreach (IAiDetectable other in start.Others)
        {
            detection.Detectables.Add(other);
        }
        foreach (Soldiers.Igi2HumanSoldier body in soldiers.Bodies)
        {
            detection.Detectables.Add(body);
            Igi2Gameplay.SeeThrough(body, detection);
        }

        // HumanSoldier_UpdateInternalDataAfterLoadHandler: a living soldier of team 1 is no business of the detectors; it is listed
        // again as it dies.
        foreach (Soldiers.Igi2HumanSoldier body in soldiers.Bodies)
        {
            if (body is { Team: 1, IsDead: false })
            {
                detection.Remove(body);
            }
        }

        Igi2AiGraphs graphs = Igi2AiGraphs.Of(start.Graphs.Select(entry =>
        {
            // A graph on something that moves is held in its frame and rides it.
            Igi2AiGraph walked = Igi2AiGraph.From(
                entry.Value,
                text => Igi2LineScript.Evaluate(text, loop.Natives, functions),
                start.CarrierOf?.Invoke(entry.Key));
            walked.Id = entry.Key;

            // The host ships no cover for its graphs: it is felt for in the host's world, as the client does for a graph flagged to.
            walked.UpdateCover(start.World);
            return walked;
        }));
        Igi2AiWorld ai = Igi2LevelAi.Build(new Igi2LevelAiSetup(
            Root,
            _guest.Resources,
            loop.Natives,
            functions,
            start.Player,
            [.. soldiers.Bodies.Cast<Humans.Igi2Human>()],
            start.Random,
            ticks)
        {
            Detection = detection,
            FireTaskTypes = services.TryGet(out Igi2HumanWeapons? armoury) ? armoury.FireTaskTypes : null,
            Config = services.TryGet(out Igi2HumanAiConfig? config) ? config : null,
            Difficulty = (int)start.Difficulty,
            Logger = start.Logger,
            World = services.Require<Igi2AiWorld>(),
            Graphs = graphs,
        });
        ai.Doors = [.. ai.Doors, .. start.Doors];
        Igi2Gameplay.HearAiEvents(soldiers.Bodies, () => ai);
        foreach (Interaction.Igi2AlarmControlTask alarm in _alarms)
        {
            alarm.AiEvents = e => ai.SendEvent(e);
        }

        Igi2LevelRun run = new(Root, loop.Natives, functions) { Dynamic = dynamic };

        // The soldiers see the player as I.G.I. 2 sees its own, not at their cones' full length -- from their first look.
        Igi2HostPlayerVisibility? visibility = start.Visibility is { } seen ? new(start.Player, seen, start.Light) : null;
        visibility?.Update();
        hosted = new Igi2HostedLevel(loop.Tasks, Root, run, sounds, soldiers, detection, ai, visibility)
        {
            Pickups = pickups,
            Grenades = grenades,
            Casings = casings,
            Alarms = [.. _alarms],
        };
        return hosted;
    }

    private GameTask New(GameTask parent, string typeName) => _guest.Loop.Tasks.New(parent, _guest.Loop.Types.Get(typeName));

    private void Bind(GameTask task, int preferred)
    {
        TaskTree tasks = _guest.Loop.Tasks;
        if (preferred >= 0 && tasks.FromId(preferred) is null && tasks.TryBindId(preferred, task))
        {
            return;
        }

        while (tasks.FromId(_spare) is not null)
        {
            _spare--;
        }

        tasks.TryBindId(_spare--, task);
    }

    // A soldier as a blast finds it: only while it takes part in the level.
    private sealed class ActiveBody(Soldiers.Igi2HumanSoldier body) : IExplosionHitTarget
    {
        public void ExplosionHit(in ExplosionHit hit)
        {
            if (body.IsActive)
            {
                ((IExplosionHitTarget)body).ExplosionHit(hit);
            }
        }
    }
}

using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
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
            natives: loop.Natives,
            functions: functions);

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
        Igi2Gameplay.HearAiEvents(soldiers.Bodies, () => ai);

        Igi2LevelRun run = new(Root, loop.Natives, functions) { Dynamic = new Igi2DynamicTasks() };

        // The soldiers see the player as I.G.I. 2 sees its own, not at their cones' full length -- from their first look.
        Igi2HostPlayerVisibility? visibility = start.Visibility is { } seen ? new(start.Player, seen, start.Light) : null;
        visibility?.Update();
        hosted = new Igi2HostedLevel(loop.Tasks, Root, run, sounds, soldiers, detection, ai, visibility);
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
}

using OpenIGI.Game.Ai;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The AI module's state, shared by every soldier: what <c>HumanAI_Open</c> sets up and the globals the natives work through
/// (<c>HumanAI_ptCurrentHumanAI</c>, <c>HumanAI_ptCurrentEventStack</c>, <c>HumanAI_nHandlerPriority</c>,
/// <c>HumanAI_ptLocalActionList</c>, <c>HumanAI_ptDefaultScript</c>).
/// </summary>
/// <remarks>
/// It lives for the session, as the module does; the level hands it what the level owns (<see cref="BeginLevel"/>).
/// </remarks>
public sealed class Igi2AiWorld
{
    /// <summary>The script every soldier falls back on, <c>LOCAL:common/ai/default.qsc</c>.</summary>
    public const string DefaultScript = "LOCAL:common/ai/default.qvm";

    /// <summary>The script every squad falls back on, <c>LOCAL:common/ai/squaddefault.qsc</c>.</summary>
    public const string SquadDefaultScript = "LOCAL:common/ai/squaddefault.qvm";

    private readonly List<Igi2AiActionItem> _localActions = [];

    /// <summary>Gets or sets the namespace the scripts run in, the session's natives.</summary>
    public NativeRegistry? Natives { get; set; }

    /// <summary>Gets the default soldier script, <c>HumanAI_ptDefaultScript</c>, once loaded.</summary>
    public QvmProgram? Default { get; private set; }

    /// <summary>Gets the default squad script, <c>AISquad_ptDefaultScript</c>, once loaded.</summary>
    public QvmProgram? SquadDefault { get; private set; }

    /// <summary>Gets the pool every A* search claims from.</summary>
    public Igi2AiSearchPool SearchPool { get; } = new();

    /// <summary>Gets or sets the run stream, the level's (<c>Game_ptGame->tRunRandomState</c>).</summary>
    public IgiRandom Random { get; set; } = new();

    /// <summary>Gets or sets the humans the world holds, <c>Human_ptQTaskList</c>, which every broadcast event reaches.</summary>
    public IReadOnlyList<Igi2Human> Humans { get; set; } = [];

    /// <summary>Gets or sets the level's graphs, by task id.</summary>
    public Igi2AiGraphs? Graphs { get; set; }

    /// <summary>Gets or sets the level's tick, <c>LevelFlow_ptLevelFlow-&gt;nTick</c>.</summary>
    public Func<int> LevelTick { get; set; } = static () => 0;

    /// <summary>Gets or sets the session's difficulty, nought to two (<c>eDifficultyLevel</c>).</summary>
    public int Difficulty { get; set; } = 1;

    /// <summary>Gets or sets where the level's scripts are read from.</summary>
    public ResourceRegistry? Resources { get; set; }

    /// <summary>Gets the mind whose script is running, <c>HumanAI_ptCurrentHumanAI</c>, or <see langword="null"/>.</summary>
    public Igi2HumanAi? Current { get; internal set; }

    /// <summary>Gets the kind of event the running script handles, <c>HumanAI_ptCurrentEventStack</c>.</summary>
    public int CurrentEventKind { get; internal set; } = -1;

    /// <summary>Gets the rank actions the running script pushes take, <c>HumanAI_nHandlerPriority</c>.</summary>
    public int HandlerPriority { get; internal set; }

    /// <summary>Gets the actions the running script has asked for, <c>HumanAI_ptLocalActionList</c>.</summary>
    internal List<Igi2AiActionItem> LocalActions => _localActions;

    /// <summary>Gets the squads, in the order they were made, <c>AISquad_ptQTaskList</c>.</summary>
    public List<Igi2AiSquad> Squads { get; } = [];

    /// <summary>Gets the squad whose script is running, <c>AISquad_ptCurrentAISquad</c>, or <see langword="null"/>.</summary>
    public Igi2AiSquad? CurrentSquad { get; internal set; }

    /// <summary>Gets the tick each squad voice line was last said on, <c>AISQuad_anVoiceLastPlayedTickTable</c>.</summary>
    public int[] VoiceLastPlayed { get; } = new int[20];

    /// <summary>Gets what soldiers are using, <c>HumanAI_tActivationList</c>.</summary>
    public Igi2AiActivations Activations { get; } = new();

    /// <summary>Gets or sets the detection world soldiers see through.</summary>
    public Detection.Igi2DetectionWorld? Detection { get; set; }

    /// <summary>Gets or sets the fire task types, which tell a bullet from a rocket.</summary>
    public Weapons.Igi2FireTaskTypes FireTaskTypes { get; set; }

    /// <summary>Gets or sets the player, whom a soldier at a jammed door listens for.</summary>
    public IAiPlayer? Player { get; set; }

    /// <summary>Gets or sets the level's task with an id, <c>Level_GetQTaskFromLevelQTaskID</c>.</summary>
    public Func<int, object?> TaskById { get; set; } = static _ => null;

    /// <summary>Gets or sets where the level's placed task with an id is, its object position, or <see langword="null"/>.</summary>
    public Func<int, Vector3d?> TaskPosition { get; set; } = static _ => null;

    /// <summary>Gets or sets the level's patrol paths, by task id.</summary>
    public Func<int, Igi2PatrolPathTask?> PatrolPath { get; set; } = static _ => null;

    /// <summary>Gets or sets how a patrol command's expression is evaluated, <c>PatrolPathCommand_isExpression</c>.</summary>
    public Func<Igi2PatrolPathCommandTask, bool> EvaluateCommand { get; set; } = static _ => false;

    /// <summary>Gets or sets the doors, which a soldier on its way opens.</summary>
    public IReadOnlyList<Interaction.Igi2DoorTask> Doors { get; set; } = [];

    /// <summary>Gets or sets the level's mounted guns, by task id, or <see langword="null"/>.</summary>
    public Func<int, IIgi2AiMountedGun?> StationaryGun { get; set; } = static _ => null;

    /// <summary>Gets or sets whether an indoor sector holds a soldier's view in, by sector.</summary>
    public Func<object?, bool> IsIndoors { get; set; } = static sector => sector is not null;

    /// <summary>Gets or sets the ambient noise at a point, which muffles what soldiers hear (<c>AmbientArea</c>), nought to one.</summary>
    public Func<Vector3d, float> NoiseAt { get; set; } = static _ => 0f;

    /// <summary>Gets or sets which side the next look-around starts to, <c>dword_834DBC4</c>.</summary>
    public int LookAroundSide { get; set; }

    /// <summary>Gets or sets the script variable <c>HumanAI_nEvent</c> (<c>HumanAI_nScriptEvent</c>).</summary>
    public int ScriptEvent { get; set; }

    /// <summary>Gets or sets the script variable <c>AISquad_nSquadState</c> (<c>AISquad_nScriptSquadState</c>).</summary>
    public int SquadScriptState { get; set; }

    /// <summary>Gets or sets the script variable <c>AISquad_nEvent</c> (<c>AISquad_nScriptEvent</c>).</summary>
    public int SquadScriptEvent { get; set; }

    /// <summary>Gets or sets the visual sweep pattern next used, <c>HumanAIContent_VisualSweepPattern</c>.</summary>
    public int VisualSweepPattern { get; set; }

    /// <summary>Gets or sets the tick a detection line last sounded, <c>HumanAIContent_nLastDetectionSoundPlayedTick</c>.</summary>
    public int LastDetectionSoundTick { get; set; }

    /// <summary>The door nearest a point, as each door answers <c>HumanAIFindActivate</c> (<c>Door_AIFindActivateHandler</c>).</summary>
    /// <param name="position">The point.</param>
    /// <returns>The door, or <see langword="null"/>.</returns>
    public Interaction.Igi2DoorTask? DoorNearest(Vector3d position)
    {
        Interaction.Igi2DoorTask? best = null;
        double bestSquared = -1.0;
        foreach (Interaction.Igi2DoorTask door in Doors)
        {
            Vector3d at = door.CurrentPosition;
            double dz = position.Z - at.Z, dy = position.Y - at.Y, dx = position.X - at.X;
            double squared = ((dz * dz) + (dy * dy)) + (dx * dx);
            if (bestSquared < 0.0 || squared < bestSquared)
            {
                best = door;
                bestSquared = squared;
            }
        }

        return best;
    }

    /// <summary>Whether a squad other than one shares an enemy, <c>AISquad_TargetSeenByOtherSquads</c>.</summary>
    /// <param name="squad">The squad left out.</param>
    /// <param name="enemy">The enemy.</param>
    /// <returns><see langword="true"/> when another does.</returns>
    public bool IsTargetSeenByOtherSquads(Igi2AiSquad squad, IAiDetectable enemy) =>
        Squads.Exists(other => !ReferenceEquals(other, squad) && other.IsTargetSeen(enemy));

    /// <summary>Gets the oldest event of the kind the running script handles, or <see langword="null"/>.</summary>
    public Igi2AiEventRecord? CurrentEvent => Current is { } ai && CurrentEventKind >= 0 ? ai.Events.Head(CurrentEventKind) : null;

    /// <summary>Forgets the last level's soldiers and squads, as a level's start leaves the module empty.</summary>
    public void BeginLevel()
    {
        Squads.Clear();
        Activations.Clear();
        Array.Clear(VoiceLastPlayed);
        Current = null;
        CurrentSquad = null;
        CurrentEventKind = -1;
        LookAroundSide = 0;
        VisualSweepPattern = 0;
        LastDetectionSoundTick = 0;
    }

    /// <summary>Loads the module's scripts, as <c>HumanAI_Open</c> and <c>AISquad_Open</c> do at start-up.</summary>
    /// <param name="resources">Where to read them.</param>
    public void LoadScripts(ResourceRegistry resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        Default = Load(resources, DefaultScript);
        SquadDefault = Load(resources, SquadDefaultScript);
    }

    /// <summary>Loads a script, or <see langword="null"/> when the data has none (<c>Script_New</c>).</summary>
    /// <param name="resources">Where to read it.</param>
    /// <param name="resource">Its name; a <c>.qsc</c> is read from its compiled <c>.qvm</c>.</param>
    /// <returns>The program.</returns>
    public static QvmProgram? Load(ResourceRegistry resources, string resource)
    {
        ArgumentNullException.ThrowIfNull(resources);
        string compiled = resource.EndsWith(".qsc", StringComparison.OrdinalIgnoreCase) ? resource[..^4] + ".qvm" : resource;
        if (!resources.TryLoad(compiled, out ReadOnlyMemory<byte> bytes))
        {
            return null;
        }

        try
        {
            return QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {resource}", ex);
        }
    }

    /// <summary>Runs a script from its start in the session's namespace, <c>Script_Execute</c>.</summary>
    /// <param name="program">The script.</param>
    /// <exception cref="EngineFatalException">The script faults.</exception>
    public void Execute(QvmProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        QvmInterpreter interpreter = new(program, Natives ?? throw new InvalidOperationException("The AI has no namespace."));
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException($"AI script faulted with {error}: {interpreter.ErrorMessage}");
        }
    }

    /// <summary>
    /// Raises an event every human in the world hears, <c>HumanAI_SendEvent</c>: each soldier's mind weighs it
    /// (<see cref="Igi2HumanAi.Receive"/>); other humans ignore it.
    /// </summary>
    /// <param name="aiEvent">The event.</param>
    public void SendEvent(in Igi2AiEvent aiEvent)
    {
        foreach (Igi2Human human in Humans)
        {
            if (human is Soldiers.Igi2HumanSoldier { Mind: Igi2HumanAi ai })
            {
                ai.Receive(aiEvent);
            }
        }
    }

    /// <summary>
    /// Every human weighs a shot's bullets before they fly, <c>human.c Human_EvaluateGunShotHandler</c>: a soldier with a mind hears
    /// the bullets that pass within two metres of its head, one event each.
    /// </summary>
    /// <param name="shot">The shot.</param>
    /// <param name="shooter">Who holds the weapon that made it.</param>
    /// <remarks>
    /// <para>
    /// The shot's creator is the weapon itself (<c>Weapon_CreateHandler</c>), never a human, so the handler's test of the creator's
    /// team always compares with team one: a human of team one hears nothing, whoever fired. A human holding the shot's creator
    /// would be skipped, but none does.
    /// </para>
    /// <para>
    /// Retail's quirk is kept too: the n-th event is told of the n-th bullet of the shot, from its muzzle, whichever bullets were the
    /// ones heard. The event's originator is the shot's ignored task, a mounted gun's holder, and its team; with none, team nought.
    /// </para>
    /// </remarks>
    public void EvaluateGunShot(Weapons.Igi2GunShot shot, object shooter)
    {
        ArgumentNullException.ThrowIfNull(shot);
        const int CreatorTeam = 1;
        IAiHuman? ignored = shot.Ignore as IAiHuman;
        Span<Vector3d> heard = stackalloc Vector3d[32];
        foreach (Igi2Human listener in Humans)
        {
            if (listener.Team == CreatorTeam
                || listener is not Soldiers.Igi2HumanSoldier { Mind: Igi2HumanAi ai } soldier)
            {
                continue;
            }

            Vector3d head = soldier.CheapHeadBonePosition();
            int count = shot.FindHearableIntersections(head, heard);
            for (int i = 0; i < count && i < shot.Instants.Count; i++)
            {
                Vector3d muzzle = shot.Instants[i].Position;
                float dx = ((Fpu)head.X - muzzle.X).F;
                float dy = ((Fpu)head.Y - muzzle.Y).F;
                float dz = ((Fpu)head.Z - muzzle.Z).F;
                float radius = (Fpu.Sqrt(((Fpu)dx * dx) + ((Fpu)dy * dy) + ((Fpu)dz * dz)) + 4096.0).F;
                ai.Receive(new Igi2AiEvent(
                    Igi2AiEvent.GunShotMiss, ignored?.Team ?? 0, 1090, radius, muzzle, new Vector3f(dx, dy, dz), ignored, shot));
            }
        }
    }

    /// <summary>The soldier whose top action is going to use a task, <c>HumanAI_GetActivatingAI</c>.</summary>
    /// <param name="taskId">The task's id.</param>
    /// <returns>The mind, or <see langword="null"/>.</returns>
    public Igi2HumanAi? ActivatingAi(int taskId)
    {
        foreach (Igi2Human human in Humans)
        {
            if (human is Soldiers.Igi2HumanSoldier { Mind: Igi2HumanAi ai }
                && ai.Actions.Top is { Init: var init, Context: Igi2AiContent.ActivateContext { TaskId: var id } }
                && init == Igi2AiContent.ActivateInit && id == taskId)
            {
                return ai;
            }
        }

        return null;
    }

    /// <summary>Points the natives at a mind about to run its script for a kind of event, at a rank.</summary>
    /// <param name="ai">The mind.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="priority">The rank actions take.</param>
    internal void Enter(Igi2HumanAi ai, int kind, int priority)
    {
        Current = ai;
        CurrentEventKind = kind;
        HandlerPriority = priority;
        _localActions.Clear();
    }
}

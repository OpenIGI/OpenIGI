using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>What a soldier's mind sees with, <c>HumanAIView_s</c>: where its eyes are and the two cones it looks through.</summary>
public sealed class Igi2AiView
{
    /// <summary>Gets or sets the head's position, <c>tPosition</c>.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets where it looks from, <c>tViewPosition</c>.</summary>
    public Vector3d ViewPosition { get; set; }

    /// <summary>Gets or sets the head's orientation, <c>tOrientation</c>, nine floats row by row.</summary>
    public float[] Orientation { get; set; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Gets or sets the orientation it looks with, <c>tViewOrientation</c>, nine floats row by row.</summary>
    public float[] ViewOrientation { get; set; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Gets or sets the main cone's half-angle up and down, <c>vViewConeAlpha</c>.</summary>
    public double ConeAlpha { get; set; } = 0.7853981852531433;

    /// <summary>Gets or sets the main cone's half-angle across, <c>vViewConeGamma</c>.</summary>
    public double ConeGamma { get; set; } = 0.8726646304130554;

    /// <summary>Gets or sets the main cone's reach, <c>vViewConeLength</c>.</summary>
    public double ConeLength { get; set; } = 4096.0;

    /// <summary>Gets or sets the second cone's half-angle up and down.</summary>
    public double SecondaryConeAlpha { get; set; } = 0.7853981852531433;

    /// <summary>Gets or sets the second cone's half-angle across.</summary>
    public double SecondaryConeGamma { get; set; } = 1.3962634801864624;

    /// <summary>Gets or sets the second cone's reach.</summary>
    public double SecondaryConeLength { get; set; } = 4096.0;

    /// <summary>
    /// Whether a position lies in either cone (<c>HumanAI_IsPositionInsideView</c>): in front of the eyes, then within the main
    /// cone's reach and angles, else the second's.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="ignoreDistance">Whether the cones reach any distance.</param>
    /// <returns><see langword="true"/> when seen.</returns>
    public bool Contains(Vector3d position, bool ignoreDistance)
    {
        float[] o = ViewOrientation;
        Fpu dx = (Fpu)position.X - ViewPosition.X, dy = (Fpu)position.Y - ViewPosition.Y, dz = (Fpu)position.Z - ViewPosition.Z;
        Fpu lx = (((Fpu)o[6] * dz) + ((Fpu)o[3] * dy)) + ((Fpu)o[0] * dx);
        Fpu ly = (((Fpu)o[7] * dz) + ((Fpu)o[4] * dy)) + ((Fpu)o[1] * dx);
        Fpu lz = (((Fpu)o[8] * dz) + ((Fpu)o[5] * dy)) + ((Fpu)o[2] * dx);
        if (ly.Value < 0.0)
        {
            return false;
        }

        double length = Fpu.Sqrt(((ly * ly) + (lz * lz)) + (lx * lx)).Value;
        float alpha = Math.Abs(Detection.Igi2AiAngles.CalcAlpha(ly.Value, lz.Value));
        float gamma = Math.Abs(World.Igi2Pose.CalcGamma(lx.Value, ly.Value));
        if (ignoreDistance || !(length > ConeLength))
        {
            if (alpha < ConeAlpha && gamma < ConeGamma)
            {
                return true;
            }

            if (ignoreDistance)
            {
                return alpha < SecondaryConeAlpha && gamma < SecondaryConeGamma;
            }
        }

        return !(SecondaryConeLength < length) && alpha < SecondaryConeAlpha && gamma < SecondaryConeGamma;
    }
}

/// <summary>
/// An enemy soldier's mind, I.G.I. 2's <c>HumanAI</c> (<c>humanai.c</c>): it hears events, runs its script on the most important
/// kind, keeps a stack of actions and runs the top one, looks for enemies, and writes the channels that drive its body.
/// </summary>
/// <remarks>
/// <para>
/// A tick (<see cref="Run"/>, <c>HumanAI_RunHandler</c>): the idle sweep of the view, the sector it stands in, the animation speed,
/// timers, whether it sees a target, its queued lines, its alert state, which view it uses, where its eyes are; then, once its script
/// and graph are there, on the first tick its creation event, after that its death checks and an idle event when it has nothing to
/// do; then its script, its top action and its detection; last, its channels go to its body and are cleared.
/// </para>
/// </remarks>
public sealed partial class Igi2HumanAi : IIgi2SoldierMind
{
    /// <summary>How many channels a mind writes, <c>avChannel[63]</c>.</summary>
    public const int ChannelCount = 63;

    /// <summary>A turn limit's start, 4.67 degrees a tick, <c>0x3DA6CE94</c>.</summary>
    public const float DefaultTurnLimit = 0.081448734f;

    private readonly float[] _channels = new float[ChannelCount];

    /// <summary>Initializes a new instance of the <see cref="Igi2HumanAi"/> class, as <c>HumanAI_CreateHandler</c> makes one.</summary>
    /// <param name="world">The AI module.</param>
    /// <param name="task">The level's <c>HumanAI</c> task.</param>
    /// <param name="soldier">The body it drives.</param>
    public Igi2HumanAi(Igi2AiWorld world, Igi2HumanAiTask task, Igi2HumanSoldier soldier)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Task = task ?? throw new ArgumentNullException(nameof(task));
        Soldier = soldier ?? throw new ArgumentNullException(nameof(soldier));
        PathSearch = new Igi2AiPathSearch(world.SearchPool);
        soldier.Mind = this;
        task.Mind = this;
    }

    /// <summary>Gets the AI module.</summary>
    public Igi2AiWorld World { get; }

    /// <summary>Gets the level's <c>HumanAI</c> task.</summary>
    public Igi2HumanAiTask Task { get; }

    /// <summary>Gets the body it drives, its parent.</summary>
    public Igi2HumanSoldier Soldier { get; }

    /// <summary>Gets or sets the squad it belongs to, its body's parent, or <see langword="null"/>.</summary>
    public Igi2AiSquad? Squad { get; set; }

    /// <summary>Gets what it sees with.</summary>
    public Igi2AiView View { get; } = new();

    /// <summary>Gets its heard events.</summary>
    public Igi2AiEventList Events { get; } = new();

    /// <summary>Gets its actions.</summary>
    public Igi2AiActionList Actions { get; } = new();

    /// <summary>Gets its A* search.</summary>
    public Igi2AiPathSearch PathSearch { get; }

    /// <summary>Gets its script, <c>MISSION:AI/&lt;id&gt;.qsc</c>, once loaded.</summary>
    public QvmProgram? Script { get; private set; }

    /// <summary>Gets the graph it walks, or <see langword="null"/>.</summary>
    public Igi2AiGraph? Graph { get; private set; }

    /// <summary>Gets the channels it writes this tick, <c>avChannel</c>.</summary>
    public float[] Channels => _channels;

    /// <summary>Gets its tuning, <c>tTweakParameters</c>: 54 values in the retail order (<see cref="Igi2AiTweakIndex"/>).</summary>
    public float[] Tweaks { get; } = new float[Igi2AiTweakIndex.Count];

    /// <summary>Gets or sets the clip a stand action plays, <c>zStandActionAnimation</c>.</summary>
    public string StandActionAnimation { get; set; } = "stand";

    /// <summary>Gets or sets its flags, <c>bFlags</c>: 1 sidekick, 2 has seen a dead friend, 4 alerted, 8 activating, 0x10 combat-idle member, 0x20 view not limited indoors.</summary>
    public int Flags { get; set; }

    /// <summary>Gets or sets the soldier type, <c>nAIType</c>.</summary>
    public int AiType { get; set; }

    /// <summary>Gets or sets the animation set, <c>nAnimType</c>.</summary>
    public int AnimType { get; set; } = 2;

    /// <summary>Gets or sets the ticks a dead body stays before it is removed, <c>nDeadRemoveTimeOut</c>.</summary>
    public int DeadRemoveTimeOut { get; set; } = 300;

    /// <summary>Gets or sets the stationary gun it mans, by task id, <c>nGunnerID</c>.</summary>
    public int GunnerId { get; set; } = -1;

    /// <summary>Gets or sets when it may raise the alarm, <c>eAlarmAccessMode</c> (set, never read).</summary>
    public int AlarmAccessMode { get; set; }

    /// <summary>Gets or sets the death animation it is told to play, <c>eDeathAnimation</c>.</summary>
    public int DeathAnimation { get; set; } = -1;

    /// <summary>Gets the script's four integers, <c>anScriptIntegerValues</c>.</summary>
    public int[] ScriptIntegers { get; } = new int[4];

    /// <summary>Gets the script's four reals, <c>avScriptRealValues</c>.</summary>
    public double[] ScriptReals { get; } = new double[4];

    /// <summary>Gets or sets the least ticks between idle animations, <c>nAnimationMinimumDelay</c> (set, never read).</summary>
    public int AnimationMinimumDelay { get; set; }

    /// <summary>Gets or sets the drawn ticks added to it, <c>nAnimationRandomDelay</c> (set, never read).</summary>
    public int AnimationRandomDelay { get; set; }

    /// <summary>Gets or sets the ticks before the next idle animation, <c>nAnimationDelay</c> (set, never read).</summary>
    public int AnimationDelay { get; set; }

    /// <summary>Gets the animations the script may draw and their weights, <c>atAnimationEntries</c>: at most five.</summary>
    public List<(int Animation, double Weight)> AnimationEntries { get; } = [];

    /// <summary>Gets or sets the evasive timer, <c>nEvasiveActionTimer</c>: 300 after being shot at.</summary>
    public int EvasiveActionTimer { get; set; }

    /// <summary>Gets or sets the ticks before it may say another line, <c>nSoundTimer</c>.</summary>
    public int SoundTimer { get; set; }

    /// <summary>Gets or sets the ticks it has gone without a target to update, <c>nTargetPosUpdateTimer</c>.</summary>
    public int TargetPosUpdateTimer { get; set; }

    /// <summary>Gets or sets the ticks it has been a member, <c>nMemberTimer</c>.</summary>
    public int MemberTimer { get; set; }

    /// <summary>Gets or sets the direction change timer, <c>nDirectionChangeTimer</c>.</summary>
    public int DirectionChangeTimer { get; set; }

    /// <summary>Gets or sets a value indicating whether it sees a target, <c>isDetection</c>: the script's <c>&lt;name&gt;.isDetection</c>.</summary>
    public bool IsDetection { get; set; }

    /// <summary>Gets or sets a value indicating whether it has dealt with its body's death, <c>isDead</c>.</summary>
    public bool IsDead { get; set; }

    /// <summary>Gets or sets a value indicating whether it heard the alarm this tick, <c>isAlarmed</c>.</summary>
    public bool IsAlarmed { get; set; }

    /// <inheritdoc/>
    public bool IsInvulnerable { get; set; }

    /// <inheritdoc/>
    public bool IsInstantDeathDisabled { get; set; }

    /// <inheritdoc/>
    public bool HasStarted { get; private set; }

    /// <summary>Gets or sets how far it hears, as a share, <c>vHearingRangeFactor</c>.</summary>
    public float HearingRangeFactor { get; set; } = 1f;

    /// <summary>Gets or sets how fast it may turn its aim up or down a tick, <c>vMaxAIMDelta</c>.</summary>
    public float MaxAimDelta { get; set; } = DefaultTurnLimit;

    /// <summary>Gets or sets how fast it may turn its body a tick, <c>vMaxRotationDelta</c>.</summary>
    public float MaxRotationDelta { get; set; } = DefaultTurnLimit;

    /// <summary>Loads the mind's script and readies its detection (<c>HumanAI_UpdateInternalDataHandler</c>).</summary>
    /// <remarks>
    /// The types are read from their names; the script is <c>MISSION:AI/&lt;level id&gt;.qsc</c>; the ranks the old script set are
    /// forgotten; detection checks the soldier's team.
    /// </remarks>
    public void UpdateInternalData()
    {
        World.Current = this;
        AiType = Task.AiType;
        AnimType = Task.AnimType;
        Script = null;
        if (Task.Id != Engine.Tasks.GameTask.NoId && World.Resources is { } resources)
        {
            Script = Igi2AiWorld.Load(resources, Igi2HumanAiTask.ScriptNameFor(Task.Id));
            Events.ClearPriorities();
        }

        InitDetection();
    }

    /// <summary>
    /// Readies the mind once the level is loaded (<c>HumanAI_UpdateInternalDataAfterLoadHandler</c>): its graph and the node
    /// nearest it, which is also where it returns to; the way it faces; its tuning; one draw of the run stream for its view sweep's
    /// first pause; and, if it is its squad's first soldier, the squad's timeouts.
    /// </summary>
    /// <param name="tweaks">The tuning its type and the difficulty give it, or <see langword="null"/>.</param>
    public void AfterLoad(Igi2AiTweaks? tweaks)
    {
        if (Task.GraphId != -1 && World.Graphs?.Graph(Task.GraphId) is { } graph)
        {
            Graph = graph;
            graph.ClosestNode(Soldier.Position, allowVehicleNodes: true, out Igi2AiGraphNode? node);
            if (node is not null)
            {
                SetCurrentNode(node.Id);
                OriginalNode = node.Id;
            }
        }

        Matrix3f o = Soldier.Orientation;
        OriginalViewDirection = new Vector3f(o.M12, o.M22, o.M32);
        if (tweaks is not null)
        {
            Igi2AiTweakIndex.Fill(Tweaks, tweaks);
        }

        ViewConeRotation.Timer = (int)(World.Random.NextRaw() % 150u);
        if (Squad is { } squad && squad.FirstMember == this)
        {
            squad.DangerTimeout = Tweaks[Igi2AiTweakIndex.DangerTimeout];
            squad.InquireTimeout = Tweaks[Igi2AiTweakIndex.InquireTimeout];
        }
    }

    /// <summary>Moves the mind's footing to a node, taking its weight off the old node and onto the new (<c>HumanAI_SetCurrentNode</c>).</summary>
    /// <param name="node">The new node.</param>
    public void SetCurrentNode(int node)
    {
        if (Graph is not { } graph || node == CurrentNode)
        {
            return;
        }

        graph.AddWeight(CurrentNode, -20480f);
        CurrentNode = node;
        graph.AddWeight(node, 20480f);
    }

    /// <inheritdoc/>
    /// <remarks>The mind runs as its body's child, inside the body's delayed pass.</remarks>
    public void Run(Igi2HumanSoldier soldier)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        UpdateIdleViewConeRotation();
        FindCurrentSectorFromNodes();
        soldier.Animation.MainTimeStep = Fpu.ToInt32((Fpu)Tweaks[Igi2AiTweakIndex.AnimSpeedFactor] * (double)Animation.Igi2AnimController.TicksPerFrame);
        if (EvasiveActionTimer != 0)
        {
            EvasiveActionTimer--;
        }

        if (RollDisallowedTimer != 0)
        {
            RollDisallowedTimer--;
        }

        if (SoundTimer != 0)
        {
            SoundTimer--;
        }

        if (DirectionChangeTimer != 0)
        {
            DirectionChangeTimer--;
        }

        IsDetection = GetTarget(visibleOnly: true) is not null;
        MemberTimer++;
        _channels[22] = 0f;
        MaintainSoundQueue();

        Flags &= ~4;
        if (Squad is { TenseTimer: > 0 })
        {
            Flags |= 4;
        }

        if ((Flags & 4) != 0 || (Detection.Enemies.Any(static e => e.IsIdentifying) && IsAnyIdentifyingBoneInView()))
        {
            UseCombatView();
            _channels[29] = 1f;
        }
        else
        {
            UseIdleView();
        }

        PlaceView();
        if (Script is null || Graph is null)
        {
            return;
        }

        if (HasStarted)
        {
            RunDeathChecks();
            if (Actions.IsEmpty)
            {
                Receive(new Igi2AiEvent(Squad?.State is > 0 ? 30 : 4, -1, 0, 0f, null, default, null));
            }
        }
        else
        {
            soldier.Update(_channels);
            FindWeapons();
            HasStarted = true;
            Igi2AiEventRecord create = new() { Event = 0 };
            Events.PushTransient(create);
            RunScriptEvent(0, -1);
            Events.PopTransient(create);
        }

        RunScript();
        RunActionStack();
        RunDetection();
        IsAlarmed = false;
        if (soldier.IsActive)
        {
            soldier.Update(_channels);
            Array.Clear(_channels);
        }
    }

    /// <summary>
    /// Weighs an event (<c>HumanAI_SendEventHandler</c>): a dead mind, an inactive body, a mind with no script or squad hears nothing;
    /// before its first tick only the creation event is heard, after it only the kinds its script ranked. The event's flags then ask
    /// for it to be within reach (quieter in noise), in view, from a human, alive or dead, of its side or the other's, or in sight.
    /// Heard, it is queued with its direction.
    /// </summary>
    /// <param name="aiEvent">The event.</param>
    public void Receive(in Igi2AiEvent aiEvent)
    {
        if (IsDead || !Soldier.IsActive || Script is null || Squad is null)
        {
            return;
        }

        int kind = aiEvent.Event;
        if (HasStarted ? !Events.IsPrioritized(kind) : kind != 0)
        {
            return;
        }

        if (aiEvent.Originator is Igi2HumanSoldier reporter && reporter.HasReportedTo(Squad))
        {
            return;
        }

        if (kind is 14 or 19)
        {
            EvasiveActionTimer = 300;
        }
        else if (kind is 24 or 34 && ReferenceEquals(aiEvent.Originator, Soldier))
        {
            return;
        }

        if (aiEvent.Flags != 0 && !PassesFlags(aiEvent))
        {
            return;
        }

        if (Events.Create() is not { } record)
        {
            return;
        }

        record.Event = kind;
        record.Team = aiEvent.Team;
        record.Position = aiEvent.Position ?? default;
        record.Originator = aiEvent.Originator;
        record.Reference = aiEvent.Reference;
        record.Specific = aiEvent.Specific;
        record.Direction = kind switch
        {
            14 or 23 or 24 or 31 or 34 or 42 => aiEvent.Direction,
            15 or 19 => new Vector3f((-(Fpu)aiEvent.Direction.X).F, (-(Fpu)aiEvent.Direction.Y).F, (-(Fpu)aiEvent.Direction.Z).F),
            _ when aiEvent.Position is { } at => new Vector3f(
                ((Fpu)at.X - Soldier.Position.X).F, ((Fpu)at.Y - Soldier.Position.Y).F, ((Fpu)at.Z - Soldier.Position.Z).F),
            _ => default,
        };

        if (aiEvent.Originator is Igi2HumanSoldier dying && kind is 22 or 34)
        {
            dying.ReportTo(Squad);
        }

        if (kind == 39)
        {
            OnVisualGunshot(aiEvent, record);
        }

        if (kind is 9 or 10 or 11 or 12 or 13 or 19 or 23 or 26 or 31 or 41 or 42 or 44)
        {
            Squad.IncreaseSuspicion(record.Position);
        }

        Events.Add(record);
    }

    private bool PassesFlags(in Igi2AiEvent aiEvent)
    {
        int flags = aiEvent.Flags;
        if ((flags & 0x100) != 0 && ReferenceEquals(aiEvent.Originator, Soldier))
        {
            return false;
        }

        Vector3d at = aiEvent.Position ?? default;
        if ((flags & 2) != 0)
        {
            double distance = Igi2AiMath.Distance(at, View.Position);
            if (aiEvent.Radius < distance)
            {
                return false;
            }

            if ((flags & 0x400) != 0)
            {
                float noise = Math.Max(World.NoiseAt(at), World.NoiseAt(Soldier.Position));
                noise = noise >= 1f ? 1f : noise;
                if (((((Fpu)1.0 - noise) * HearingRangeFactor) * aiEvent.Radius).Value < distance)
                {
                    return false;
                }
            }
        }

        if ((flags & 1) != 0 && !View.Contains(at, ignoreDistance: false))
        {
            return false;
        }

        if ((flags & 0x200) != 0 && !View.Contains(at, ignoreDistance: true))
        {
            return false;
        }

        Igi2Human? originator = aiEvent.Originator as Igi2Human;
        if ((flags & 4) != 0 && originator is null)
        {
            return false;
        }

        if ((flags & 8) != 0 && originator is { IsDead: true })
        {
            return false;
        }

        if ((flags & 0x10) != 0 && originator is { IsDead: false })
        {
            return false;
        }

        if ((flags & 0x20) != 0 && aiEvent.Team != Soldier.Team)
        {
            return false;
        }

        if ((flags & 0x40) != 0 && aiEvent.Team == Soldier.Team)
        {
            return false;
        }

        return (flags & 0x80) == 0 || World.Detection is not { } sight
            || sight.LineOfSight(Soldier.CheapHeadBonePosition(), at, OpenIGI.Game.Igi2.Detection.Igi2DetectionWorld.SightGroups, out _);
    }

    /// <summary>
    /// Runs the script for the most important kind waiting that outranks the top action, and lets every waiting event go
    /// (<c>HumanAI_RunScript</c>): at most one script runs a tick, and the kinds it did not run for are lost.
    /// </summary>
    public void RunScript()
    {
        if (Script is null)
        {
            return;
        }

        int topPriority = Actions.Top?.Priority ?? Igi2AiEventList.EventTypes;
        bool ran = false;
        for (int rank = 0; rank < Events.PriorityCount; rank++)
        {
            int kind = Events.KindAt(rank);
            if (rank < topPriority && Events.Of(kind).Count != 0 && !ran)
            {
                ran = RunScriptEvent(kind, rank);
            }

            Events.Flush(kind);
        }
    }

    /// <summary>
    /// Runs the script for a kind of event at a rank (<c>HumanAI_RunScriptEvent</c>): the actions it asks for are pushed, one by
    /// one when it is one, as a list when more.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="rank">The rank its actions take.</param>
    /// <returns><see langword="true"/> when it pushed something.</returns>
    public bool RunScriptEvent(int kind, int rank)
    {
        World.Enter(this, kind, rank);
        if (Script is { } script)
        {
            World.Execute(script);
        }

        List<Igi2AiActionItem> asked = [.. World.LocalActions];
        World.LocalActions.Clear();
        if (asked.Count == 0)
        {
            return false;
        }

        if (asked.Count == 1)
        {
            Push(asked[0]);
        }
        else
        {
            PushList(asked);
        }

        return true;
    }

    /// <summary>
    /// Pushes an action (<c>HumanAI_PushActionStackItem</c>): a pushable top with a push callback is told, otherwise the tops are
    /// popped to a pushable one; then it goes on top, the search and the way ahead are cleared, and its Init runs.
    /// </summary>
    /// <param name="item">The action.</param>
    public void Push(Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Actions.Top is { Flags: var flags, PushedFromTop: { } pushed } top && (flags & 1) != 0)
        {
            pushed(this, top);
        }
        else
        {
            Actions.PopUntilPushable(this);
        }

        Actions.AddTop(item);
        PathSearch.Clear();
        DestinationNode = CurrentNode;
        ClearMovementQueue();
        item.Init?.Invoke(this, item);
    }

    /// <summary>
    /// Pushes a script's several actions (<c>HumanAI_PushActionStackList</c>): the tops are popped to a pushable one, then the
    /// actions go on last first, so the first asked for runs first, each taking the last one's flags and running its Init.
    /// </summary>
    /// <param name="items">The actions, in the order asked for.</param>
    public void PushList(IReadOnlyList<Igi2AiActionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Actions.PopUntilPushable(this);
        int flags = items[^1].Flags;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            Igi2AiActionItem item = items[i];
            item.Flags = flags;
            Actions.AddTop(item);
            item.Init?.Invoke(this, item);
        }
    }

    /// <summary>
    /// Adds an action from a squad order or from within (<c>HumanAI_AddAction</c>): under a more important top it is put on top, its
    /// Init runs and, if it is still on top, it is popped at once; otherwise it is pushed.
    /// </summary>
    /// <param name="item">The action.</param>
    public void AddAction(Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Actions.Top is { } top && top.Priority < item.Priority)
        {
            Actions.AddTop(item);
            item.Init?.Invoke(this, item);
            if (ReferenceEquals(Actions.Top, item))
            {
                Actions.Pop(this, item);
            }
        }
        else
        {
            DestinationNode = CurrentNode;
            Push(item);
        }
    }

    /// <summary>
    /// Runs the top action (<c>HumanAI_RunActionStack</c>): from the top down, the first action still waiting counts its wait down
    /// (only one a tick), every waiting action is passed, and the first one not waiting with a run runs.
    /// </summary>
    public void RunActionStack()
    {
        bool counted = false;
        foreach (Igi2AiActionItem item in Actions.Items.ToArray())
        {
            if (item.Delay != 0)
            {
                if (!counted)
                {
                    item.Delay--;
                    counted = true;
                }
            }
            else if (item.Run is { } run)
            {
                run(this, item);
                return;
            }
        }
    }

    /// <summary>
    /// Deals with the body's death once (<c>HumanAI_RunDeathChecks</c>): every action popped with its Delete, the script run for
    /// <c>AIEVENT_DEAD</c> at its rank, detection cleared, its cover node freed and its events let go; and once the body is gone, its
    /// way, door and activations let go too.
    /// </summary>
    public void RunDeathChecks()
    {
        if (Soldier.IsDead && !IsDead)
        {
            Actions.Clear(this);
            Igi2AiEventRecord dead = new() { Event = 2 };
            Events.PushTransient(dead);
            RunScriptEvent(2, Events.RankOf(2));
            Events.PopTransient(dead);
            IsDead = true;
            Detection.Clear();
            Graph?.CoverOf(CurrentNode)?.Let(static item => item.Flags &= ~1);
            Events.Clear();
        }

        if (Soldier.IsDead && Soldier.IsDeathAnimationDone && Soldier.IsDeathPositionStabilized)
        {
            ClearMovementQueue();
            ReleaseDoor();
            ClearActivationEntries();
        }
    }

    /// <summary>Gets the tuning value at a retail index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The value.</returns>
    public float Tweak(int index) => Tweaks[index];
}

/// <summary>Small helpers on the AI's state.</summary>
internal static class Igi2AiExtensions
{
    /// <summary>Calls an action on a value.</summary>
    public static void Let<T>(this T value, Action<T> action) => action(value);
}

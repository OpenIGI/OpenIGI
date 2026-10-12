using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>The simple actions: waits, looks, moves, sounds and the dead.</content>
public static partial class Igi2AiContent
{
    private const float TurnTolerance = 0.0872664675116539f;
    private const float FineTolerance = 0.03490658476948738f;

    /// <summary>The look-around patterns, <c>HumanAIContent_atLookAroundPattern[6]</c> as <c>HumanAIContent_InitLookAroundPatterns</c> fills them: four turns each, the ticks before each and the turn speed.</summary>
    public static IReadOnlyList<(int Ticks, float Rotation)[]> LookAroundPatterns { get; } =
    [
        [(150, -1.5707963705062866f), (150, -1.5707963705062866f), (150, 1.5707963705062866f), (150, 1.5707963705062866f)],
        [(30, -0.7853981852531433f), (30, 0.7853981852531433f), (30, 0.7853981852531433f), (30, -0.7853981852531433f)],
        [(30, 0.7853981852531433f), (30, -0.7853981852531433f), (30, -0.7853981852531433f), (30, 0.7853981852531433f)],
        [(150, 1.5707963705062866f), (150, 1.5707963705062866f), (150, -1.5707963705062866f), (150, -1.5707963705062866f)],
        [(150, 1.5707963705062866f), (150, 1.5707963705062866f), (150, 1.5707963705062866f), (150, 1.5707963705062866f)],
        [(15, 1.5707963705062866f), (15, 1.5707963705062866f), (15, 1.5707963705062866f), (15, 1.5707963705062866f)],
    ];

    /// <summary>The turn speeds of the look-around patterns.</summary>
    public static IReadOnlyList<float> LookAroundSpeeds { get; } =
        [0.05235987901687622f, 0.10471975803375244f, 0.10471975803375244f, 0.05235987901687622f, 0.05235987901687622f, 0.15707963705062866f];

    // --- Delayed event pass-on (HumanAIContent_DelayedEventPassOn*) -----------------------------------------------------------

    /// <summary>Waits a tenth of the detection delay to the event (nine to 45 ticks), <c>…InitCB</c>.</summary>
    public static void DelayedEventPassOnInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        DelayedPassContext context = item.ContextAs<DelayedPassContext>();
        item.Run = DelayedEventPassOnRun;
        item.Delete = DelayedEventPassOnDelete;
        double ticks = 10 * ai.DelayFromDetectionTimeDistanceFactor(context.Position);
        context.Ticks = ticks >= 9.0 ? Fpu.ToInt32(ticks > 45.0 ? 45.0 : ticks) : 9;
    }

    /// <summary>Faces the event until the wait ends or a target shows, <c>…RunCB</c>.</summary>
    public static void DelayedEventPassOnRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        DelayedPassContext context = item.ContextAs<DelayedPassContext>();
        if (context.Ticks == 0 || ai.GetTarget(visibleOnly: true) is not null)
        {
            Done(ai, item);
            return;
        }

        ai.FacePositionSelectSpeed(context.Position, TurnTolerance, level: false);
        context.Ticks--;
    }

    /// <summary>Unless dead, tells the squad of the event at its rank, <c>…DeleteCB</c>.</summary>
    public static void DelayedEventPassOnDelete(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        DelayedPassContext context = item.ContextAs<DelayedPassContext>();
        if (ai.IsDead || ai.Squad is not { } squad)
        {
            return;
        }

        Igi2AiSquadEvent passed = new()
        {
            Kind = context.Event,
            Priority = ai.Events.RankOf(context.Event),
            Position = context.Position,
            Direction = context.Direction,
        };
        passed.Receivers.Add(ai);
        squad.ReceiveEvent(passed);
    }

    // --- Dead (HumanAIContent_Dead*) -------------------------------------------------------------------------------------------

    /// <summary>Levels the eyes from now on, <c>HumanAIContent_DeadInitCB</c>.</summary>
    public static void DeadInit(Igi2HumanAi ai, Igi2AiActionItem item) => item.Run = DeadRun;

    /// <summary>Levels the eyes, <c>HumanAIContent_DeadRunCB</c>.</summary>
    public static void DeadRun(Igi2HumanAi ai, Igi2AiActionItem item) => ai.LevelSight();

    // --- Stand action and sound (HumanAIContent_PlayAnimation*, …PlaySound*) -------------------------------------------------

    /// <summary>Plays the stand action, looping as asked, <c>HumanAIContent_PlayAnimationInitCB</c>.</summary>
    public static void PlayAnimationInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        AnimationContext context = item.ContextAs<AnimationContext>();
        item.Run = PlayAnimationRun;
        context.IsAnimationPlayed = false;
        ai.Soldier.IsLoopStandAnimation = context.IsLoop;
    }

    /// <summary>Starts the stand action once standing, and is done once it is over, <c>…RunCB</c>.</summary>
    public static void PlayAnimationRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        AnimationContext context = item.ContextAs<AnimationContext>();
        if (ai.IsState(4))
        {
            return;
        }

        if (context.IsAnimationPlayed)
        {
            Done(ai, item);
        }
        else if (ai.IsState(0))
        {
            ai.SetChannel(3, 1f);
            context.IsAnimationPlayed = true;
        }
    }

    /// <summary>Plays the sound from the next tick, <c>HumanAIContent_PlaySoundInitCB</c>.</summary>
    public static void PlaySoundInit(Igi2HumanAi ai, Igi2AiActionItem item) => item.Run = PlaySoundRun;

    /// <summary>
    /// Plays the sound, on the soldier when attached or where its eyes are, and is done, <c>…RunCB</c>.
    /// </summary>
    public static void PlaySoundRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        SoundContext context = item.ContextAs<SoundContext>();
        if (context.IsAttached)
        {
            ai.Soldier.Surroundings.StartSound(context.Sound);
        }
        else
        {
            ai.Soldier.Surroundings.PlaySoundAt(context.Sound, ai.View.Position);
        }

        Done(ai, item);
    }

    // --- Moving to an event (HumanAIContent_MoveToEvent*) -----------------------------------------------------------------------

    /// <summary>Takes the node nearest the event as the goal, <c>HumanAIContent_MoveToEventInitCB</c>.</summary>
    public static void MoveToEventInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        EventMoveContext context = item.ContextAs<EventMoveContext>();
        item.Run = MoveToEventRun;
        Igi2AiGraphNode? node = null;
        ai.Graph?.ClosestNode(context.Position, allowVehicleNodes: false, out node);
        context.Node = node?.Id ?? -1;
    }

    /// <summary>Walks or runs to it at the attack turns, done on arrival, <c>…RunCB</c>.</summary>
    public static void MoveToEventRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        EventMoveContext context = item.ContextAs<EventMoveContext>();
        if (context.IsRun)
        {
            ai.MakeRun();
        }
        else
        {
            ai.MakeWalk();
        }

        if (ai.AttackMoveToNode(context.Node))
        {
            Done(ai, item);
        }
    }

    // --- Looking at an event (HumanAIContent_LookAtEvent*) ---------------------------------------------------------------------

    /// <summary>Starts the look's clock unset, <c>HumanAIContent_LookAtEventInitCB</c>.</summary>
    public static void LookAtEventInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.Run = LookAtEventRun;
        item.ContextAs<LookContext>().StartTime = -1;
    }

    /// <summary>
    /// Aims at the event (at a target in sight, while it is being identified), turns to it, and once facing looks for the seconds
    /// asked, then is told <c>AIEVENT_LOOKAT_DONE</c> (40) unless a target is confirmed, <c>…RunCB</c>.
    /// </summary>
    public static void LookAtEventRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        LookContext context = item.ContextAs<LookContext>();
        IIgi2Detectable? confirmed = null;
        ai.SetChannel(5, 1f);
        if (ai.GetTarget(visibleOnly: true) is { } target)
        {
            context.Position = target.DetectablePosition;
            if (ai.Detection.Find(target) is not { IsIdentifying: true })
            {
                confirmed = target;
            }
        }
        else
        {
            ai.LastTargetPosition = context.Position;
            ai.TargetPosition = context.Position;
        }

        ai.SelectTurnSpeed();
        if (!ai.FacePosition(context.Position, FineTolerance, level: false) && confirmed is null)
        {
            return;
        }

        if (context.StartTime < 0)
        {
            context.StartTime = Fpu.ToInt32((Fpu)context.SecondsToLook * 30.0);
        }

        if (context.StartTime != 0)
        {
            if (confirmed is null)
            {
                context.StartTime--;
                return;
            }
        }
        else if (confirmed is null)
        {
            ai.Receive(new Igi2AiEvent(40, 0, 0, 0f, null, default, null));
        }

        Done(ai, item);
    }

    // --- Stunned and lying flat (HumanAIContent_Stunned*, …FallFlat*) ----------------------------------------------------------

    /// <summary>Starts the clock unset, <c>HumanAIContent_StunnedInitCB</c>.</summary>
    public static void StunnedInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.Run = StunnedRun;
        item.ContextAs<TimedContext>().EndTick = -1;
    }

    /// <summary>Plays the stand action until the seconds are up, <c>…RunCB</c>.</summary>
    public static void StunnedRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        TimedContext context = item.ContextAs<TimedContext>();
        int tick = ai.World.LevelTick();
        if (context.EndTick < 0)
        {
            context.EndTick = tick - Fpu.ToInt32((Fpu)context.Seconds * -30.0);
        }

        if (tick <= context.EndTick)
        {
            if (!ai.IsState(4))
            {
                ai.SetChannel(3, 1f);
            }
        }
        else
        {
            Done(ai, item);
        }
    }

    /// <summary>Starts the clock unset, <c>HumanAIContent_FallFlatInitCB</c>.</summary>
    public static void FallFlatInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.Run = FallFlatRun;
        item.ContextAs<TimedContext>().EndTick = -1;
    }

    /// <summary>Lies down until the seconds are up, <c>…RunCB</c>.</summary>
    public static void FallFlatRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        TimedContext context = item.ContextAs<TimedContext>();
        ai.SetChannel(2, 1f);
        int tick = ai.World.LevelTick();
        if (context.EndTick < 0)
        {
            context.EndTick = tick - Fpu.ToInt32((Fpu)context.Seconds * -30.0);
        }

        if (tick > context.EndTick)
        {
            Done(ai, item);
        }
    }

    // --- Kicking a grenade (HumanAIContent_KickGrenade*) -----------------------------------------------------------------------

    /// <summary>Takes the node nearest the grenade, <c>HumanAIContent_KickGrenadeInitCB</c>; the kick itself does nothing.</summary>
    public static void KickGrenadeInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        EventMoveContext context = item.ContextAs<EventMoveContext>();
        item.Run = KickGrenadeRun;
        item.Delete = KickGrenadeDelete;
        Igi2AiGraphNode? node = null;
        ai.Graph?.ClosestNode(context.Position, allowVehicleNodes: false, out node);
        context.Node = node?.Id ?? -1;
        context.IsRun = false;
    }

    /// <summary>Lets go of the grenade, <c>…DeleteCB</c>.</summary>
    public static void KickGrenadeDelete(Igi2HumanAi ai, Igi2AiActionItem item) => item.ContextAs<EventMoveContext>().Grenade = null;

    /// <summary>Nothing, <c>…RunCB</c>.</summary>
    public static void KickGrenadeRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
    }

    // --- Running in panic (HumanAIContent_RunPanicking*) ------------------------------------------------------------------------

    /// <summary>
    /// Flees thirty metres directly away from a danger within thirty metres, to the nearest node there; a vehicle's squad does not
    /// flee (<c>HumanAIContent_RunPanickingInitCB</c>).
    /// </summary>
    public static void RunPanickingInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        EventMoveContext context = item.ContextAs<EventMoveContext>();
        item.Run = RunPanickingRun;
        context.Timer = 0;
        context.Node = -1;
        Vector3d eye = ai.View.Position;
        double dx = context.Position.X - eye.X, dy = context.Position.Y - eye.Y, dz = context.Position.Z - eye.Z;
        if (Math.Sqrt((dz * dz) + (dy * dy) + (dx * dx)) > 122880.0)
        {
            Done(ai, item);
            return;
        }

        if (ai.Squad?.SquadType == 2)
        {
            Done(ai, item);
            return;
        }

        Vector3d at = ai.Soldier.Position;
        double ax = at.X - context.Position.X;
        float ay = (float)(at.Y - context.Position.Y), az = (float)(at.Z - context.Position.Z);
        double length = Math.Sqrt((ax * ax) + (ay * (double)ay) + (az * (double)az));
        double ox, oy;
        float oz;
        if (length <= 0.0)
        {
            oz = 0f;
            ox = 122880.0;
            oy = 0.0;
        }
        else
        {
            float scale = (float)(122880.0 / length);
            ox = ax * scale;
            oy = scale * ay;
            oz = scale * az;
        }

        Vector3d away = new(ox + at.X, oy + at.Y, oz + at.Z);
        if (ai.Graph is { } graph && graph.Nearest(away, false) is { } node)
        {
            context.Node = node.Id;
        }
        else
        {
            Done(ai, item);
        }
    }

    /// <summary>Runs to the node for at most five seconds, <c>…RunCB</c>.</summary>
    public static void RunPanickingRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        EventMoveContext context = item.ContextAs<EventMoveContext>();
        context.Timer++;
        if (context.Node == -1 || context.Timer > 150)
        {
            Done(ai, item);
            return;
        }

        ai.MakeRun();
        if (ai.AttackMoveToNode(context.Node))
        {
            Done(ai, item);
        }
    }

    // --- Walking and running to a node (HumanAIContent_WalkToNode*, …RunToNode*) ---------------------------------------------

    /// <summary>Walks to the node, or is done at once when the node is not on the graph, <c>HumanAIContent_WalkToNodeInitCB</c>.</summary>
    public static void WalkToNodeInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        if (ai.Graph?.NodeAt(item.ContextAs<NodeContext>().Node) is null)
        {
            Done(ai, item);
        }
        else
        {
            item.Run = WalkToNodeRun;
        }
    }

    /// <summary>Walks to the node at the attack turns, <c>…RunCB</c>.</summary>
    public static void WalkToNodeRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        int node = item.ContextAs<NodeContext>().Node;
        ai.MakeWalk();
        ai.DestinationNode = node;
        if (ai.AttackMoveToNode(node))
        {
            Done(ai, item);
        }
    }

    /// <summary>Runs to the node, or is done at once when the node is not on the graph, <c>HumanAIContent_RunToNodeInitCB</c>.</summary>
    public static void RunToNodeInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.Run = RunToNodeRun;
        if (ai.Graph?.NodeAt(item.ContextAs<NodeContext>().Node) is null)
        {
            Done(ai, item);
        }
    }

    /// <summary>Runs to the node at the attack turns, <c>…RunCB</c>.</summary>
    public static void RunToNodeRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        int node = item.ContextAs<NodeContext>().Node;
        ai.MakeRun();
        ai.DestinationNode = node;
        if (ai.AttackMoveToNode(node))
        {
            Done(ai, item);
        }
    }

    // --- Firing at a node or task (HumanAIContent_FireAtNode*, …FireAtTask*) -------------------------------------------------

    /// <summary>Not yet aiming, no pause, <c>HumanAIContent_FireAtNodeInitCB</c>.</summary>
    public static void FireAtNodeInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        FireAtContext context = item.ContextAs<FireAtContext>();
        item.Run = FireAtNodeRun;
        context.IsFireState = false;
        context.Delay = -1;
    }

    /// <summary>Fires at the node for the ticks asked, <c>…RunCB</c>.</summary>
    public static void FireAtNodeRun(Igi2HumanAi ai, Igi2AiActionItem item) =>
        FireAt(ai, item, context => ai.Graph?.NodeAt(context.Target)?.Position);

    /// <summary>Not yet aiming, no pause, <c>HumanAIContent_FireAtTaskInitCB</c>.</summary>
    public static void FireAtTaskInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        FireAtContext context = item.ContextAs<FireAtContext>();
        item.Run = FireAtTaskRun;
        context.IsFireState = false;
        context.Delay = -1;
    }

    /// <summary>Fires at the task for the ticks asked, done when it is no placed object, <c>…RunCB</c>.</summary>
    public static void FireAtTaskRun(Igi2HumanAi ai, Igi2AiActionItem item) =>
        FireAt(ai, item, context => ai.World.TaskPosition(context.Target));

    /// <summary>
    /// The shared fire loop: while ticks remain, an empty clip reloads (or shows the reload when asked); the target is aimed at;
    /// a burst is readied, then fired after the pause, the pause between bursts following.
    /// </summary>
    private static void FireAt(Igi2HumanAi ai, Igi2AiActionItem item, Func<FireAtContext, Vector3d?> aim)
    {
        FireAtContext context = item.ContextAs<FireAtContext>();
        int ticks = context.Ticks;
        context.Ticks = ticks - 1;
        if (ticks < 0 || ai.Soldier.Weapon.Firing is null)
        {
            Done(ai, item);
            return;
        }

        if (ai.ActiveWeaponType is null)
        {
            Done(ai, item);
            return;
        }

        if (ai.AmmoInWeapon() == 0)
        {
            if (context.ShowReload)
            {
                ai.Channels[8] = 1f;
                return;
            }

            ai.Soldier.Weapon.Firing?.Reload();
            context.IsFireState = false;
        }

        if (aim(context) is not { } target)
        {
            Done(ai, item);
            return;
        }

        ai.TargetPosition = target;
        if (!context.IsFireState)
        {
            InitFire(ai);
            context.IsFireState = true;
        }

        if (context.Delay >= 0)
        {
            context.Delay--;
        }
        else if (RunFire(ai))
        {
            context.IsFireState = false;
            context.Delay = context.BurstDelay;
        }
    }

    // --- Looking at a point (HumanAIContent_LookAtPosition*) -------------------------------------------------------------------

    /// <summary>Starts the look's count, <c>HumanAIContent_LookAtPositionInitCB</c>.</summary>
    public static void LookAtPositionInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.RunInit = LookAtPositionInit;
        item.Run = LookAtPositionRun;
        item.ContextAs<LookContext>().Timer = 0;
    }

    /// <summary>
    /// Turns to the point (aiming at it, unless ranked as a plain sighting), and once facing it watches up to five seconds or until a
    /// confirmed target shows (<c>…RunCB</c>).
    /// </summary>
    public static void LookAtPositionRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        LookContext context = item.ContextAs<LookContext>();
        if (item.Priority != ai.Events.RankOf(4))
        {
            ai.LastTargetPosition = context.Position;
            ai.TargetPosition = context.Position;
            ai.SetChannel(5, 1f);
        }

        ai.SelectTurnSpeed();
        if (!ai.FacePosition(context.Position, FineTolerance, level: true))
        {
            return;
        }

        IIgi2Detectable? confirmed = null;
        if (ai.GetTarget(visibleOnly: true) is { } target)
        {
            context.Position = target.DetectablePosition;
            confirmed = ai.Detection.Find(target) is { IsIdentifying: true } ? null : target;
        }

        if (context.Timer >= 150 || confirmed is not null)
        {
            Done(ai, item);
        }
        else
        {
            context.Timer++;
        }
    }

    // --- Looking around (HumanAIContent_LookAround*) ---------------------------------------------------------------------------

    /// <summary>
    /// Starts a sweep, the first turn to either side by turns across all soldiers, after half a second's drawn pause
    /// (<c>HumanAIContent_LookAroundInitCB</c>).
    /// </summary>
    public static void LookAroundInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        LookAroundContext context = item.ContextAs<LookAroundContext>();
        item.RunInit = LookAroundInit;
        item.Run = LookAroundRun;
        context.CurrentRotation = 0f;
        context.CurrentTarget = ai.World.LookAroundSide == 0 ? 0.7853981852531433f : -0.7853981852531433f;
        context.Stage = 0;
        ai.World.LookAroundSide = 1 - ai.World.LookAroundSide;
        context.Delay = Fpu.ToInt32(((double)(ushort)ai.World.Random.NextRaw() * IgiRandom.Scale) * 30.0 * 0.5);
    }

    /// <summary>
    /// Sweeps the eyes, aimed and crouched as asked, three degrees a tick to each side in turn, pausing a drawn moment at each end;
    /// done after six turns or a target (<c>…RunCB</c>).
    /// </summary>
    public static void LookAroundRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        LookAroundContext context = item.ContextAs<LookAroundContext>();
        ai.SetChannel(5, 1f);
        if (context.IsCrouch)
        {
            ai.SetChannel(1, 1f);
        }

        if (context.Delay != 0)
        {
            context.Delay--;
            return;
        }

        float s = (float)(Math.Sin(context.CurrentRotation) * 40960.0);
        float c = (float)(Math.Cos(context.CurrentRotation) * 40960.0);
        double step = context.CurrentTarget - (double)context.CurrentRotation;
        step = step >= -0.05235987901687622 ? (step > 0.05235987901687622 ? 0.05235987901687622 : step) : -0.05235987901687622;
        float[] o = ai.Soldier.OrientationRows;
        float lx = (float)((c * o[1]) + (s * o[0]) + (o[2] * 4096.0));
        float ly = (float)((c * o[4]) + (s * o[3]) + (o[5] * 4096.0));
        float lz = (float)((c * o[7]) + (s * o[6]) + (o[8] * 4096.0));
        if (Math.Abs(step) >= 0.05235987901687622)
        {
            context.CurrentRotation = (float)(step + context.CurrentRotation);
        }
        else
        {
            context.Stage++;
            context.CurrentTarget = -context.CurrentTarget;
            context.Delay = Fpu.ToInt32((double)ai.World.Random.NextSingle() * 30.0 * 0.3);
        }

        if (context.Stage > 6 || ai.GetTarget(visibleOnly: true) is not null)
        {
            Done(ai, item);
        }

        Vector3d at = ai.Soldier.Position;
        ai.LastTargetPosition = new Vector3d(lx + at.X, ly + at.Y, lz + at.Z);
    }

    // --- Enemy detection (pfActionInitCB__, HumanAIContent_EnemyDetectionRunCB) -----------------------------------------------

    /// <summary>Marks when a detection line last sounded, <c>pfActionInitCB__</c>.</summary>
    public static void EnemyDetectionInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.RunInit = EnemyDetectionInit;
        item.Run = EnemyDetectionRun;
        ai.World.LastDetectionSoundTick = ai.World.LevelTick();
    }

    /// <summary>
    /// Turns to the first enemy it has seen, a human's chest rather than his feet; done when none is listed
    /// (<c>HumanAIContent_EnemyDetectionRunCB</c>).
    /// </summary>
    public static void EnemyDetectionRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        WaitContext context = item.ContextAs<WaitContext>();
        Igi2DetectionItem? seen = ai.Detection.Enemies.FirstOrDefault(static e => e.HasBeenFullVisible);
        if (seen is null)
        {
            Done(ai, item);
            return;
        }

        if (context.Timer > 0)
        {
            context.Timer--;
        }

        Vector3d at = seen.Target.DetectablePosition;
        if (seen.Target is Igi2Human)
        {
            at = new Vector3d(at.X, at.Y, at.Z + 2048.0);
        }

        ai.FacePositionSelectSpeed(at, FineTolerance, level: true);
    }

    // --- Dead friend (HumanAIContent_DeadFriendlyDetection*) ------------------------------------------------------------------

    /// <summary>Notes it has seen a dead friend, <c>HumanAIContent_DeadFriendlyDetectionInitCB</c>.</summary>
    public static void DeadFriendlyDetectionInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.RunInit = DeadFriendlyDetectionInit;
        item.Run = DeadFriendlyDetectionRun;
        item.ContextAs<NodeContext>().Timer = 0;
        ai.Flags |= 2;
    }

    /// <summary>For three seconds, runs to cover if it has found some, <c>…RunCB</c>.</summary>
    public static void DeadFriendlyDetectionRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        NodeContext context = item.ContextAs<NodeContext>();
        context.Timer++;
        if (context.Timer > 90)
        {
            Done(ai, item);
            return;
        }

        if (ai.CoverSearch.State == Igi2AiCoverSearchState.NodeFound)
        {
            ai.DestinationNode = ai.CoverSearch.CoverNode;
            ai.MakeRun();
            if (ai.AttackMoveToNode(ai.DestinationNode))
            {
                ai.CoverSearch.State = Igi2AiCoverSearchState.Idle;
                Done(ai, item);
            }
        }
    }

    // --- Running to cover (HumanAIContent_RunToCover*) -------------------------------------------------------------------------

    /// <summary>Starts a fresh cover search, <c>HumanAIContent_RunToCoverInitCB</c>.</summary>
    public static void RunToCoverInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.ContextAs<NodeContext>().Timer = 0;
        item.RunInit = RunToCoverInit;
        item.Run = RunToCoverRun;
        ai.CoverSearch.State = Igi2AiCoverSearchState.Idle;
    }

    /// <summary>
    /// For three seconds searches cover from the event's position and runs to it, facing the event there; while none is found it
    /// lies or kneels as asked (<c>…RunCB</c>).
    /// </summary>
    public static void RunToCoverRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        NodeContext context = item.ContextAs<NodeContext>();
        ai.HandleCoverSearch(context.Position);
        context.Timer++;
        if (context.Timer > 90)
        {
            Done(ai, item);
            return;
        }

        if (ai.CoverSearch.State == Igi2AiCoverSearchState.NodeFound)
        {
            ai.DestinationNode = ai.CoverSearch.CoverNode;
            ai.MakeRun();
            if (ai.AttackMoveToNode(ai.DestinationNode))
            {
                ai.CoverSearch.State = Igi2AiCoverSearchState.Idle;
                if (ai.FacePositionSelectSpeed(context.Position, 0.1745329350233078f, level: true))
                {
                    Done(ai, item);
                }
            }
        }
        else if (context.Node is 0 or 1)
        {
            ai.SetChannel(context.Node == 0 ? 2 : 1, 1f);
        }
    }

    // --- Waiting (HumanAIContent_Wait*) ----------------------------------------------------------------------------------------

    /// <summary>Counts down from the next tick, <c>HumanAIContent_WaitInitCB</c>.</summary>
    public static void WaitInit(Igi2HumanAi ai, Igi2AiActionItem item) => item.Run = WaitRun;

    /// <summary>Done once the ticks are spent, <c>…RunCB</c>.</summary>
    public static void WaitRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        WaitContext context = item.ContextAs<WaitContext>();
        if (context.Timer != 0)
        {
            context.Timer--;
        }
        else
        {
            Done(ai, item);
        }
    }
}

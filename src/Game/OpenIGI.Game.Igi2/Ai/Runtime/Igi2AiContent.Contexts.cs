using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>
/// What each action keeps between ticks, the members of <c>HumanAIActionContext_t</c>'s union an action reads: each class holds one
/// action's fields, named for what they are rather than for the union member the C reaches them through.
/// </content>
public static partial class Igi2AiContent
{
    /// <summary>A wait, <c>tWaitContext</c>: its ticks.</summary>
    public sealed class WaitContext
    {
        /// <summary>Gets or sets the ticks left.</summary>
        public int Timer { get; set; }
    }

    /// <summary>A look at a point or an event.</summary>
    public sealed class LookContext
    {
        /// <summary>Gets or sets the point.</summary>
        public Vector3d Position { get; set; }

        /// <summary>Gets or sets the ticks spent looking at a point.</summary>
        public int Timer { get; set; }

        /// <summary>Gets or sets the seconds an event is looked at.</summary>
        public float SecondsToLook { get; set; }

        /// <summary>Gets or sets the ticks left looking at an event, -1 before the first.</summary>
        public int StartTime { get; set; } = -1;
    }

    /// <summary>A rough look the way an event came from.</summary>
    public sealed class ApproxContext
    {
        /// <summary>Gets or sets the point looked at.</summary>
        public Vector3d Position { get; set; }

        /// <summary>Gets or sets the ticks left.</summary>
        public int TimeLeft { get; set; }

        /// <summary>Gets or sets the ticks it began with.</summary>
        public int StartTime { get; set; }

        /// <summary>Gets or sets the sectors the circle is divided into.</summary>
        public int SectorResolution { get; set; }
    }

    /// <summary>A move to a node or a point.</summary>
    public sealed class NodeContext
    {
        /// <summary>Gets or sets the node.</summary>
        public int Node { get; set; }

        /// <summary>Gets or sets a value indicating whether to run.</summary>
        public bool IsRun { get; set; }

        /// <summary>Gets or sets the point.</summary>
        public Vector3d Position { get; set; }

        /// <summary>Gets or sets a timer the action keeps.</summary>
        public int Timer { get; set; }
    }

    /// <summary>A place in the squad's formation.</summary>
    public sealed class FormationContext
    {
        /// <summary>Gets or sets the place, relative to the leader.</summary>
        public Vector3f RelativePosition { get; set; }

        /// <summary>Gets or sets the facing, relative to the leader.</summary>
        public Vector3f RelativeDirection { get; set; }

        /// <summary>Gets or sets the leader.</summary>
        public Igi2HumanAi? Leader { get; set; }

        /// <summary>Gets or sets the node it makes for.</summary>
        public int Node { get; set; } = -1;

        /// <summary>Gets or sets the ticks before the node is chosen again.</summary>
        public int Countdown { get; set; }
    }

    /// <summary>A walk to something to use.</summary>
    public sealed class ActivateContext
    {
        /// <summary>Gets or sets the task to use.</summary>
        public int TaskId { get; set; }

        /// <summary>Gets or sets the node to walk to, or -1.</summary>
        public int Node { get; set; } = -1;

        /// <summary>Gets or sets the node after it, or -1.</summary>
        public int NextNode { get; set; } = -1;

        /// <summary>Gets or sets a value indicating whether the press has begun.</summary>
        public bool IsActivating { get; set; }

        /// <summary>Gets or sets a value indicating whether to run there.</summary>
        public bool IsWalkRun { get; set; }
    }

    /// <summary>A patrol.</summary>
    public sealed class PatrolContext
    {
        /// <summary>Gets or sets the patrol's task id.</summary>
        public int PatrolPathId { get; set; }

        /// <summary>Gets or sets the command it starts from.</summary>
        public int StartIndex { get; set; }

        /// <summary>Gets or sets the ticks spent facing a looked-at node.</summary>
        public int LookTicks { get; set; }
    }

    /// <summary>A cover taken from a danger.</summary>
    public sealed class CoverContext
    {
        /// <summary>Gets or sets where the danger is.</summary>
        public Vector3d Position { get; set; }

        /// <summary>Gets or sets the way it came.</summary>
        public Vector3f Direction { get; set; }

        /// <summary>Gets or sets the cover sector facing it.</summary>
        public int Sector { get; set; }

        /// <summary>Gets or sets the cover taken, or <see langword="null"/>.</summary>
        public Igi2AiCoverItem? Cover { get; set; }
    }

    /// <summary>A look around.</summary>
    public sealed class LookAroundContext
    {
        /// <summary>Gets or sets a value indicating whether to crouch while looking.</summary>
        public bool IsCrouch { get; set; }

        /// <summary>Gets or sets the ticks before the next turn.</summary>
        public int Delay { get; set; }

        /// <summary>Gets or sets the turns made.</summary>
        public int Stage { get; set; }

        /// <summary>Gets or sets the current turn.</summary>
        public float CurrentRotation { get; set; }

        /// <summary>Gets or sets the turn it heads for.</summary>
        public float CurrentTarget { get; set; }
    }

    /// <summary>An idle wait at the soldier's post.</summary>
    public sealed class IdleContext
    {
        /// <summary>Gets or sets the ticks the wait lasts.</summary>
        public int Timer { get; set; }

        /// <summary>Gets or sets the ticks spent facing the post's way.</summary>
        public int TicksUsed { get; set; }

        /// <summary>Gets or sets a value indicating whether the idle animation has played.</summary>
        public bool IsIdleAnimPlayed { get; set; }
    }

    /// <summary>A combat idle: the stance (nought lie, one kneel, two stand) and the line-of-fire check.</summary>
    public sealed class CombatIdleContext
    {
        /// <summary>Gets or sets the stance.</summary>
        public int Stance { get; set; } = 2;

        /// <summary>Gets or sets the stance before.</summary>
        public int PreviousStance { get; set; } = 2;

        /// <summary>Gets or sets the stance check's state: nought test lying, one test kneeling, two and three settled.</summary>
        public int Trace { get; set; }
    }

    /// <summary>An event handed on to the squad after a pause.</summary>
    public sealed class DelayedPassContext
    {
        /// <summary>Gets or sets the event.</summary>
        public int Event { get; set; }

        /// <summary>Gets or sets the ticks left.</summary>
        public int Ticks { get; set; }

        /// <summary>Gets or sets where it happened.</summary>
        public Vector3d Position { get; set; }

        /// <summary>Gets or sets the way it came.</summary>
        public Vector3f Direction { get; set; }
    }

    /// <summary>A timed pose: lying flat or stunned.</summary>
    public sealed class TimedContext
    {
        /// <summary>Gets or sets the seconds it lasts.</summary>
        public float Seconds { get; set; }

        /// <summary>Gets or sets the tick it ends on, -1 before it starts.</summary>
        public int EndTick { get; set; } = -1;
    }

    /// <summary>Fire at a node or a task.</summary>
    public sealed class FireAtContext
    {
        /// <summary>Gets or sets the node or task.</summary>
        public int Target { get; set; }

        /// <summary>Gets or sets the ticks left.</summary>
        public int Ticks { get; set; }

        /// <summary>Gets or sets the ticks between bursts.</summary>
        public int BurstDelay { get; set; }

        /// <summary>Gets or sets a value indicating whether an empty clip shows a reload.</summary>
        public bool ShowReload { get; set; }

        /// <summary>Gets or sets a value indicating whether a burst is readied.</summary>
        public bool IsFireState { get; set; }

        /// <summary>Gets or sets the ticks before the next burst, -1 for none.</summary>
        public int Delay { get; set; } = -1;
    }

    /// <summary>A stand action played.</summary>
    public sealed class AnimationContext
    {
        /// <summary>Gets or sets a value indicating whether it loops.</summary>
        public bool IsLoop { get; set; }

        /// <summary>Gets or sets a value indicating whether it has begun.</summary>
        public bool IsAnimationPlayed { get; set; }
    }

    /// <summary>A sound played.</summary>
    public sealed class SoundContext
    {
        /// <summary>Gets or sets the sound.</summary>
        public string Sound { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether it follows the soldier rather than staying where he is.</summary>
        public bool IsAttached { get; set; }
    }

    /// <summary>A move to where an event happened, or away from it.</summary>
    public sealed class EventMoveContext
    {
        /// <summary>Gets or sets where it happened.</summary>
        public Vector3d Position { get; set; }

        /// <summary>Gets or sets a value indicating whether to run.</summary>
        public bool IsRun { get; set; }

        /// <summary>Gets or sets the node to make for.</summary>
        public int Node { get; set; } = -1;

        /// <summary>Gets or sets the ticks the move has lasted.</summary>
        public int Timer { get; set; }

        /// <summary>Gets or sets the grenade kicked, or <see langword="null"/>.</summary>
        public object? Grenade { get; set; }
    }
}

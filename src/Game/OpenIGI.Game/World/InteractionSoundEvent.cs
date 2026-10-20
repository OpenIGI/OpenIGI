using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>What a level task does with a sound as it updates.</summary>
public enum InteractionSound
{
    /// <summary>A definition is played once where the task stands.</summary>
    Played,

    /// <summary>The task's running sound starts, to be held until it stops.</summary>
    LoopStarted,

    /// <summary>The task's running sound stops.</summary>
    LoopStopped,

    /// <summary>The task makes a sound area of its own (an <c>AmbientArea</c> child), which beds its sound in around a listener inside its box.</summary>
    AreaStarted,

    /// <summary>The task deletes its sound area.</summary>
    AreaStopped,
}

/// <summary>A sound a level task makes as it updates: a switch's click, a generator's hum, an electric fence's buzz.</summary>
/// <param name="Task">The task, which owns a held sound or area until it stops it.</param>
/// <param name="What">What happens to the sound.</param>
/// <param name="Sound">The sound definition, empty when stopping.</param>
/// <param name="Position">Where it plays, or the area's centre, in engine units.</param>
/// <param name="Orientation">An area's orientation; identity otherwise.</param>
/// <param name="HalfExtentsMetres">An area's half-size along each of its axes, in metres; nought otherwise.</param>
public readonly record struct InteractionSoundEvent(
    GameTask Task,
    InteractionSound What,
    string Sound,
    Vector3d Position,
    Matrix3f Orientation,
    Vector3d HalfExtentsMetres);

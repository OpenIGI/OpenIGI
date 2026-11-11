using System.Diagnostics.CodeAnalysis;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Level;

/// <summary>Takes a level's task as the class that runs it, when a registry hands the task its part of the tick.</summary>
public static class LevelTaskClass
{
    /// <summary>Gets a task as its class, for a run that is to carry out its handler.</summary>
    /// <typeparam name="T">The class.</typeparam>
    /// <param name="task">The task.</param>
    /// <param name="level">The run the task is being attached to.</param>
    /// <param name="typed">Receives the task as its class.</param>
    /// <returns>
    /// <see langword="true"/> when the task is of the class; <see langword="false"/> for a task of a level the run only
    /// passes over, which another title's classes make and run.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The level is the run's own and the task was made without its class: its loader did not register the level task types.
    /// </exception>
    public static bool TryAs<T>(GameTask task, LevelRun level, [NotNullWhen(true)] out T? typed)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(level);
        typed = task as T;
        if (typed is not null)
        {
            return true;
        }

        if (!level.OwnsLevel)
        {
            return false;
        }

        throw new InvalidOperationException(
            $"{task.Name} was made without its class ({typeof(T).Name}); a level's loader registers the level task types before it loads.");
    }
}

using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Level;

/// <summary>
/// Gives the tasks one game makes in a guest session of it, for another game's level, their ids: the id the host asks for
/// where the guest has not given it out, else the next spare id from the top.
/// </summary>
/// <remarks>A mind finds its script by its id, and a script sends a soldier on a path by the path's: one without an id is reached by none.</remarks>
/// <param name="tasks">The guest session's task tree.</param>
public sealed class GuestTaskIds(TaskTree tasks)
{
    /// <summary>The highest id handed out when the host asks for none, counting down.</summary>
    public const int TopId = 4095;

    private readonly TaskTree _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
    private int _spare = TopId;

    /// <summary>Binds a task its id.</summary>
    /// <param name="task">The task.</param>
    /// <param name="preferred">The id the host asks for, or a negative one for any.</param>
    public void Bind(GameTask task, int preferred)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (preferred >= 0 && _tasks.FromId(preferred) is null && _tasks.TryBindId(preferred, task))
        {
            return;
        }

        while (_tasks.FromId(_spare) is not null)
        {
            _spare--;
        }

        _tasks.TryBindId(_spare--, task);
    }
}

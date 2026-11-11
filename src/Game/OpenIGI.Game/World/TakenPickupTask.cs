using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>
/// How a pickup's <c>isPickedUp</c> is exported, and how a taken <c>GunPickup</c> or <c>AmmoPickup</c> leaves the level. The touch only raises <c>isPickedUp</c>. The task deletes
/// itself in its own update, where the level walk next reaches it.
/// </summary>
/// <remarks>
/// <para>All three pickup types register <c>%s.isPickedUp</c> from the level's activate handler (<c>0x46D170</c>, <c>0x46D8D0</c>,
/// <c>0x46C570</c>). <c>Level_NewTask</c> (<c>0x4F0780</c>) sends that event once, as the level script creates the task, and only
/// to a task with an id: one authored with <c>-1</c>, or with an id already taken, exports nothing. A conditional container
/// starting or stopping does not send it. It only puts its children into the world or takes them out
/// (<c>0x487A70</c>, <c>0x487B20</c>).</para>
/// <para>Both updates are the same check: <c>GunPickup_Run</c> (<c>0x46CEA0</c>, after its fall) and <c>AmmoPickup_Run</c>
/// (<c>0x46D740</c>) call <c>Task_Delete</c> once the flag is set. Until then, every task the walk runs in between reads
/// <c>isPickedUp</c> as 1. Mission 11's first objective depends on this (issue #44).</para>
/// <para>Their shared delete handler (<c>0x46D760</c>) runs the level's deactivate handler for a task with an id. That handler
/// (<c>0x46D1D0</c>, <c>0x46D930</c>) removes the <c>%s.isPickedUp</c> symbol. A <c>GenericPickup</c> is never deleted: its
/// update (<c>0x46C3C0</c>) only takes the item out of the world.</para>
/// </remarks>
internal static class TakenPickupTask
{
    /// <summary>The variable each pickup exports.</summary>
    internal const string PickedUpField = "isPickedUp";

    /// <summary>Exports a pickup's <c>isPickedUp</c>, if the level would register it: only a task with an id.</summary>
    /// <param name="variables">The level's script variables.</param>
    /// <param name="task">The pickup's task.</param>
    /// <param name="get">Reads the flag.</param>
    /// <param name="set">Writes the flag.</param>
    internal static void Export(ScriptVarRegistry variables, GameTask task, Func<bool> get, Action<bool> set)
    {
        if (task.Id >= 0)
        {
            variables.BindBool(task, PickedUpField, get, set);
        }
    }

    /// <summary>Gives a pickup task the update that deletes it once the pickup is taken.</summary>
    /// <param name="task">The pickup's task.</param>
    /// <param name="isTaken">Whether it has been taken.</param>
    /// <param name="variables">Where its <c>isPickedUp</c> is exported, or <see langword="null"/>.</param>
    internal static void BindUpdate(GameTask task, Func<bool> isTaken, ScriptVarRegistry? variables) =>
        BindUpdate(task, isTaken, static () => false, variables, null);

    /// <summary>Gives a pickup's task its update: once the pickup is taken the task goes, or is withdrawn until it is put back.</summary>
    /// <param name="task">The pickup's task.</param>
    /// <param name="isTaken">Whether the pickup has been taken.</param>
    /// <param name="withdraws">Whether a taken pickup's task is kept, its field withdrawn, rather than deleted.</param>
    /// <param name="variables">The level's variables, or <see langword="null"/>.</param>
    /// <param name="export">Exports the task's field again when a withdrawn pickup comes back.</param>
    /// <exception cref="InvalidOperationException">The task was made without its class.</exception>
    internal static void BindUpdate(GameTask task, Func<bool> isTaken, Func<bool> withdraws, ScriptVarRegistry? variables, Action? export)
    {
        if (task is not PickupTask pickup)
        {
            throw new InvalidOperationException(
                $"{task.Name} was made without its class; a level's loader registers the level task types before it loads.");
        }

        pickup.Taken = new TakenPickupUpdate(isTaken, withdraws, variables, export);
    }

    /// <summary>Deletes a taken pickup's task, as its delete handler does.</summary>
    /// <param name="task">The task.</param>
    /// <param name="variables">Where its <c>isPickedUp</c> is exported, or <see langword="null"/>.</param>
    internal static void Delete(GameTask task, ScriptVarRegistry? variables)
    {
        if (task.IsDeleted)
        {
            return;
        }

        if (task.Id >= 0)
        {
            variables?.Unbind(task, PickedUpField);
        }

        task.Tree.Delete(task);
    }
}

/// <summary>What a pickup's task does each tick: nothing until the pickup is taken, then it goes or is withdrawn.</summary>
/// <param name="isTaken">Whether the pickup has been taken.</param>
/// <param name="withdraws">Whether a taken pickup's task is kept, its field withdrawn, rather than deleted.</param>
/// <param name="variables">The level's variables, or <see langword="null"/>.</param>
/// <param name="export">Exports the task's field again when a withdrawn pickup comes back.</param>
internal sealed class TakenPickupUpdate(Func<bool> isTaken, Func<bool> withdraws, ScriptVarRegistry? variables, Action? export)
{
    private bool _withdrawn;

    /// <summary>Runs the update for one tick.</summary>
    /// <param name="owned">The pickup's task.</param>
    public void Run(GameTask owned)
    {
        if (!isTaken())
        {
            if (_withdrawn)
            {
                _withdrawn = false;
                export?.Invoke();
            }
        }
        else if (!withdraws())
        {
            TakenPickupTask.Delete(owned, variables);
        }
        else if (!_withdrawn)
        {
            _withdrawn = true;
            if (owned.Id >= 0)
            {
                variables?.Unbind(owned, TakenPickupTask.PickedUpField);
            }
        }
    }
}

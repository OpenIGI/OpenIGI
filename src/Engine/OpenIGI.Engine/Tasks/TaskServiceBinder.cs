using OpenIGI.Engine.Titles;

namespace OpenIGI.Engine.Tasks;

/// <summary>Hands every task of a level the services its <see cref="LevelServiceAttribute"/> properties ask for.</summary>
public static class TaskServiceBinder
{
    /// <summary>Binds a tree's tasks, the root first and then the rest in the tree's order.</summary>
    /// <param name="root">The tree.</param>
    /// <param name="services">The level's services.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void Bind(GameTask root, IServiceCatalog services)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(services);
        foreach (GameTask task in root.DescendantsAndSelf())
        {
            task.InvokeBindLevelServices(services);
        }
    }

    /// <summary>Binds one task, for a task made after its level was brought up.</summary>
    /// <param name="task">The task.</param>
    /// <param name="services">The level's services.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void BindOne(GameTask task, IServiceCatalog services)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(services);
        task.InvokeBindLevelServices(services);
    }
}

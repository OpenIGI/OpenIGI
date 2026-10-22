using OpenIGI.Engine.Tasks;

namespace OpenIGI.Modding;

/// <summary>The task types a mod adds to a game, and the game's own it makes of classes of the mod's.</summary>
/// <remarks>
/// A mod's task class reads the parameters the level writes for it as the game's own do: its properties marked
/// <see cref="ScriptParamAttribute"/>, in the order the level writes them.
/// </remarks>
public sealed class ModTasks
{
    private readonly Func<ModContentModule> _module;

    internal ModTasks(Func<ModContentModule> module) => _module = module;

    /// <summary>Adds a task type of the mod's own, which the level data and scripts spawn by its name.</summary>
    /// <typeparam name="TTask">The mod's class.</typeparam>
    /// <param name="typeName">The type's name.</param>
    /// <returns>This group.</returns>
    public ModTasks Add<TTask>(string typeName)
        where TTask : GameTask, new() => Add(typeName, static () => new TTask());

    /// <summary>Adds a task type of the mod's own, made as the mod says.</summary>
    /// <param name="typeName">The type's name.</param>
    /// <param name="factory">Makes an instance of the mod's class.</param>
    /// <returns>This group.</returns>
    public ModTasks Add(string typeName, Func<GameTask> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(factory);
        _module().Registrations.Add(context => context.Types.Register(typeName, factory));
        return this;
    }

    /// <summary>Makes the level's tasks of one of the game's types out of a class of the mod's, after the game registers its own.</summary>
    /// <typeparam name="TTask">The mod's class, which reads the same parameters the game's did.</typeparam>
    /// <param name="typeName">The task type's name, as the level's data spawns it.</param>
    /// <returns>This group.</returns>
    public ModTasks Replace<TTask>(string typeName)
        where TTask : GameTask, new() => Register(typeName, static () => new TTask(), null);

    /// <summary>Makes the level's tasks of one of the game's types out of a class of the mod's that reads other parameters.</summary>
    /// <typeparam name="TTask">The mod's class.</typeparam>
    /// <param name="typeName">The task type's name, as the level's data spawns it.</param>
    /// <param name="adapter">Sets the class from the parameters the level writes for the type.</param>
    /// <returns>This group.</returns>
    public ModTasks Replace<TTask>(string typeName, ITaskParameterAdapter adapter)
        where TTask : GameTask, new() => Register(typeName, static () => new TTask(), adapter ?? throw new ArgumentNullException(nameof(adapter)));

    /// <summary>Makes the level's tasks of one of the game's types as the mod says, after the game registers its own.</summary>
    /// <param name="typeName">The task type's name, as the level's data spawns it.</param>
    /// <param name="factory">Makes an instance of the mod's class, which reads the same parameters the game's did.</param>
    /// <returns>This group.</returns>
    public ModTasks Replace(string typeName, Func<GameTask> factory) => Register(typeName, factory, null);

    /// <summary>Makes the level's tasks of one of the game's types as the mod says, from the parameters the level writes.</summary>
    /// <param name="typeName">The task type's name, as the level's data spawns it.</param>
    /// <param name="factory">Makes an instance of the mod's class.</param>
    /// <param name="adapter">Sets the class from the parameters the level writes for the type.</param>
    /// <returns>This group.</returns>
    public ModTasks Replace(string typeName, Func<GameTask> factory, ITaskParameterAdapter adapter) =>
        Register(typeName, factory, adapter ?? throw new ArgumentNullException(nameof(adapter)));

    private ModTasks Register(string typeName, Func<GameTask> factory, ITaskParameterAdapter? adapter)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(factory);
        _module().Registrations.Add(context =>
        {
            if (adapter is null)
            {
                context.Types.Replace(typeName, factory);
            }
            else
            {
                context.Types.Replace(typeName, factory, adapter);
            }
        });
        return this;
    }
}

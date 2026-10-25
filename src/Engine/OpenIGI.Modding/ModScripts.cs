namespace OpenIGI.Modding;

/// <summary>What a mod gives the game's scripts.</summary>
public sealed class ModScripts
{
    private readonly Func<ModContentModule> _module;

    internal ModScripts(Func<ModContentModule> module) => _module = module;

    /// <summary>Adds an integer constant the game's scripts can read by name.</summary>
    /// <param name="name">The constant's name; prefixing it with the mod's keeps it apart from another mod's.</param>
    /// <param name="value">Its value.</param>
    /// <returns>This group.</returns>
    public ModScripts Constant(string name, int value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _module().Registrations.Add(context => context.Natives.RegisterConstant(name, value));
        return this;
    }
}

using System.Text;
using OpenIGI.Core.Configuration;

namespace OpenIGI.Modding;

/// <summary>A mod's settings: console variables of its own, which the settings screen lists on its Mods page.</summary>
/// <remarks>
/// <para>
/// Each is named <c>mod_&lt;id&gt;_&lt;name&gt;</c>, the mod's id with what is not a letter or a digit made an underscore, and
/// saved with the player's settings. A mod that is switched off keeps its values in the settings file until it is on again.
/// </para>
/// <para>
/// A mod's <see cref="IOpenIgiMod.Configure"/> runs for every game it applies to, and again for every session a test makes:
/// declaring a setting already declared under the same name and of the same kind gives back the one there is.
/// </para>
/// </remarks>
public sealed class ModSettings
{
    private readonly string _prefix;
    private readonly CVarRegistry _registry;

    internal ModSettings(string modId, CVarRegistry registry)
    {
        _prefix = PrefixFor(modId);
        _registry = registry;
    }

    /// <summary>Declares a switch.</summary>
    /// <param name="name">The setting's own name: lowercase letters, digits and underscores.</param>
    /// <param name="defaultValue">Its value until the player changes it.</param>
    /// <param name="description">The line the settings screen and the console show for it.</param>
    /// <returns>The setting.</returns>
    public BoolCVar Bool(string name, bool defaultValue, string description) =>
        Declare(name, full => CVar.Bool(full, defaultValue, CVarOptions.Archive, description, _registry));

    /// <summary>Declares a whole number between two bounds.</summary>
    /// <param name="name">The setting's own name: lowercase letters, digits and underscores.</param>
    /// <param name="defaultValue">Its value until the player changes it.</param>
    /// <param name="minimum">The least it may be.</param>
    /// <param name="maximum">The most it may be.</param>
    /// <param name="description">The line the settings screen and the console show for it.</param>
    /// <returns>The setting.</returns>
    public NumericCVar<int> Int(string name, int defaultValue, int minimum, int maximum, string description) =>
        Declare(name, full => CVar.Int(full, defaultValue, minimum, maximum, CVarOptions.Archive, description, _registry));

    /// <summary>Declares a number between two bounds.</summary>
    /// <param name="name">The setting's own name: lowercase letters, digits and underscores.</param>
    /// <param name="defaultValue">Its value until the player changes it.</param>
    /// <param name="minimum">The least it may be.</param>
    /// <param name="maximum">The most it may be.</param>
    /// <param name="description">The line the settings screen and the console show for it.</param>
    /// <returns>The setting.</returns>
    public NumericCVar<float> Float(string name, float defaultValue, float minimum, float maximum, string description) =>
        Declare(name, full => CVar.Float(full, defaultValue, minimum, maximum, CVarOptions.Archive, description, _registry));

    /// <summary>Gets the full name a setting of the mod's is saved and read under.</summary>
    /// <param name="name">The setting's own name.</param>
    /// <returns><c>mod_&lt;id&gt;_&lt;name&gt;</c>.</returns>
    public string NameOf(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            {
                throw new ArgumentException($"A setting's name is lowercase letters, digits and underscores; '{name}' is not.", nameof(name));
            }
        }

        return _prefix + name;
    }

    // mod_<id>_, the id's other characters made underscores.
    private static string PrefixFor(string modId)
    {
        StringBuilder prefix = new(SettingsFile.ModVariablePrefix);
        foreach (char c in modId)
        {
            prefix.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        }

        return prefix.Append('_').ToString();
    }

    private TVar Declare<TVar>(string name, Func<string, TVar> declare)
        where TVar : class, ICVar
    {
        string full = NameOf(name);
        if (_registry.TryGet(full, out ICVar? existing))
        {
            return existing as TVar
                ?? throw new InvalidOperationException($"The setting '{full}' is already declared as another kind of setting.");
        }

        return declare(full);
    }
}

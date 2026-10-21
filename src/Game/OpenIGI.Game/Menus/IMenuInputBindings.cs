using OpenIGI.Game.Config;

namespace OpenIGI.Game.Menus;

/// <summary>The player's key bindings, as a menu's controls list reads and edits them.</summary>
/// <remarks>
/// Project I.G.I.'s controls list reads the active profile's bindings straight out of it (<c>0x4061C0</c>) as a screen takes its
/// data in, and writes them back as the screen commits (<c>0x423EB0</c>). A configuration that keeps bindings offers them
/// through this.
/// </remarks>
public interface IMenuInputBindings
{
    /// <summary>Finds the binding of an action.</summary>
    /// <param name="action">The action's name.</param>
    /// <returns>The binding, or <see langword="null"/> when the action has none.</returns>
    InputBinding? Find(string action);

    /// <summary>Finds the binding an action is reset to.</summary>
    /// <param name="action">The action's name.</param>
    /// <returns>The binding, or <see langword="null"/> when the action has no default.</returns>
    InputBinding? DefaultOf(string action);

    /// <summary>Binds an action, replacing its binding.</summary>
    /// <param name="binding">The binding.</param>
    void Bind(InputBinding binding);
}

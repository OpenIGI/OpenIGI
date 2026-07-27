using System.Collections.ObjectModel;

namespace OpenIGI.Input;

/// <summary>
/// Maps named actions to keys, with more than one key allowed per action.
/// </summary>
/// <remarks>
/// The original binds actions by name from configuration, storing raw DirectInput scan codes — so a
/// binding read from a retail config resolves through this table unchanged. Action names are compared
/// ordinally and case-insensitively, matching the original's case-insensitive config parsing.
/// </remarks>
public sealed class InputBindings
{
    private readonly Dictionary<string, List<KeyCode>> _bindings =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the action names that have at least one key bound.</summary>
    public IReadOnlyCollection<string> Actions => _bindings.Keys;

    /// <summary>Binds a key to an action, in addition to any keys already bound.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="key">The key to bind.</param>
    /// <exception cref="ArgumentException"><paramref name="action"/> is empty or whitespace.</exception>
    public void Bind(string action, KeyCode key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        if (!_bindings.TryGetValue(action, out List<KeyCode>? keys))
        {
            keys = [];
            _bindings[action] = keys;
        }

        if (!keys.Contains(key))
        {
            keys.Add(key);
        }
    }

    /// <summary>Removes every key bound to an action.</summary>
    /// <param name="action">The action name.</param>
    /// <returns><see langword="true"/> when the action had bindings.</returns>
    public bool Clear(string action) => _bindings.Remove(action);

    /// <summary>Gets the keys bound to an action.</summary>
    /// <param name="action">The action name.</param>
    /// <returns>The bound keys, or an empty list when the action is unbound.</returns>
    public IReadOnlyList<KeyCode> GetKeys(string action) =>
        _bindings.TryGetValue(action, out List<KeyCode>? keys)
            ? new ReadOnlyCollection<KeyCode>(keys)
            : [];

    /// <summary>Determines whether any key bound to an action is down.</summary>
    /// <param name="keyboard">The keyboard to test.</param>
    /// <param name="action">The action name.</param>
    /// <returns><see langword="true"/> when at least one bound key is down.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyboard"/> is <see langword="null"/>.</exception>
    public bool IsDown(KeyboardState keyboard, string action)
    {
        ArgumentNullException.ThrowIfNull(keyboard);

        if (!_bindings.TryGetValue(action, out List<KeyCode>? keys))
        {
            return false;
        }

        foreach (KeyCode key in keys)
        {
            if (keyboard.IsDown(key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether any key bound to an action went down this frame.</summary>
    /// <param name="keyboard">The keyboard to test.</param>
    /// <param name="action">The action name.</param>
    /// <returns><see langword="true"/> when at least one bound key was pressed this frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyboard"/> is <see langword="null"/>.</exception>
    public bool WasPressed(KeyboardState keyboard, string action)
    {
        ArgumentNullException.ThrowIfNull(keyboard);

        if (!_bindings.TryGetValue(action, out List<KeyCode>? keys))
        {
            return false;
        }

        foreach (KeyCode key in keys)
        {
            if (keyboard.WasPressed(key))
            {
                return true;
            }
        }

        return false;
    }
}

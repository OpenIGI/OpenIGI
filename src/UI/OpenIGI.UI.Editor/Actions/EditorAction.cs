using System.Globalization;
using System.Text;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;

namespace OpenIGI.UI.Editor.Actions;

/// <summary>A key with its modifiers: one shortcut.</summary>
/// <param name="Key">The key.</param>
/// <param name="Modifiers">The modifiers that must be held, exactly.</param>
public readonly record struct KeyChord(KeyCode Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>Gets whether a key event is this chord.</summary>
    /// <param name="e">The event.</param>
    /// <returns><see langword="true"/> when the key and modifiers both match.</returns>
    public bool Matches(in KeyEvent e) => e.Key == Key && e.Modifiers == Modifiers;

    /// <summary>Parses a chord written the way menus show it: <c>Ctrl+Shift+S</c>, <c>F2</c>, <c>Delete</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The chord.</returns>
    /// <exception cref="FormatException">A part is not a modifier or key name.</exception>
    public static KeyChord Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        KeyModifiers modifiers = KeyModifiers.None;
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Control,
                "SHIFT" => KeyModifiers.Shift,
                "ALT" => KeyModifiers.Alt,
                _ => throw new FormatException($"'{parts[i]}' is not a modifier."),
            };
        }

        return new KeyChord(ParseKey(parts[^1]), modifiers);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder text = new();
        if ((Modifiers & KeyModifiers.Control) != 0)
        {
            text.Append("Ctrl+");
        }

        if ((Modifiers & KeyModifiers.Shift) != 0)
        {
            text.Append("Shift+");
        }

        if ((Modifiers & KeyModifiers.Alt) != 0)
        {
            text.Append("Alt+");
        }

        return text.Append(KeyName(Key)).ToString();
    }

    private static string KeyName(KeyCode key) => key switch
    {
        >= KeyCode.D1 and <= KeyCode.D9 => ((int)(key - KeyCode.D1) + 1).ToString(CultureInfo.InvariantCulture),
        KeyCode.D0 => "0",
        KeyCode.Delete => "Del",
        KeyCode.Escape => "Esc",
        KeyCode.Return => "Enter",
        _ => key.ToString(),
    };

    private static KeyCode ParseKey(string name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return name[0] == '0' ? KeyCode.D0 : (KeyCode)((int)KeyCode.D1 + (name[0] - '1'));
        }

        return name.ToUpperInvariant() switch
        {
            "DEL" => KeyCode.Delete,
            "ESC" => KeyCode.Escape,
            "ENTER" => KeyCode.Return,
            _ => Enum.TryParse(name, ignoreCase: true, out KeyCode key)
                ? key
                : throw new FormatException($"'{name}' is not a key."),
        };
    }
}

/// <summary>
/// Something the user can do, named once and reached from the menu bar, toolbars, context menus, the command
/// palette and its shortcut alike.
/// </summary>
/// <remarks>
/// One definition per action keeps every way of reaching it consistent: the same name, the same shortcut
/// shown, the same enabled state, the same tick.
/// </remarks>
public sealed class EditorAction
{
    /// <summary>Initializes a new instance of the <see cref="EditorAction"/> class.</summary>
    /// <param name="id">A stable id (<c>edit.undo</c>), for saved shortcut overrides and the palette.</param>
    /// <param name="title">The title menus show (<c>Undo</c>).</param>
    /// <param name="execute">What it does.</param>
    public EditorAction(string id, string title, Action execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(execute);
        Id = id;
        Title = title;
        Execute = execute;
    }

    /// <summary>Gets the stable id.</summary>
    public string Id { get; }

    /// <summary>Gets the title, or a function of the current state ("Undo Move Crate").</summary>
    public string Title { get; init; }

    /// <summary>Gets a function that names the action for the current state, overriding <see cref="Title"/>.</summary>
    public Func<string>? DynamicTitle { get; init; }

    /// <summary>Gets what the action does.</summary>
    public Action Execute { get; }

    /// <summary>Gets the shortcut, if any.</summary>
    public KeyChord? Shortcut { get; init; }

    /// <summary>Gets the icon's name in the icon set, if any.</summary>
    public string? Icon { get; init; }

    /// <summary>Gets a function that says whether the action can run now.</summary>
    public Func<bool>? CanExecute { get; init; }

    /// <summary>Gets a function that says whether the action shows a tick, for toggles.</summary>
    public Func<bool>? IsChecked { get; init; }

    /// <summary>Gets the category the palette groups it under (<c>Edit</c>, <c>View</c>).</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Gets the title to show now.</summary>
    public string CurrentTitle => DynamicTitle?.Invoke() ?? Title;

    /// <summary>Gets whether the action is enabled now.</summary>
    public bool IsEnabled => CanExecute?.Invoke() ?? true;

    /// <summary>Gets whether the action is ticked now.</summary>
    public bool Checked => IsChecked?.Invoke() ?? false;

    /// <summary>Runs the action if it is enabled.</summary>
    /// <returns>Whether it ran.</returns>
    public bool TryExecute()
    {
        if (!IsEnabled)
        {
            return false;
        }

        Execute();
        return true;
    }
}

/// <summary>Every action the editor offers, and the shortcut table built from them.</summary>
public sealed class ActionTable
{
    private readonly Dictionary<string, EditorAction> _byId = new(StringComparer.Ordinal);

    /// <summary>Gets every action, in registration order.</summary>
    public IReadOnlyCollection<EditorAction> All => _byId.Values;

    /// <summary>Adds an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The action, for chaining into a menu.</returns>
    /// <exception cref="ArgumentException">An action with the same id is already registered.</exception>
    public EditorAction Add(EditorAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_byId.TryAdd(action.Id, action))
        {
            throw new ArgumentException($"An action '{action.Id}' is already registered.", nameof(action));
        }

        return action;
    }

    /// <summary>Gets an action by id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The action.</returns>
    /// <exception cref="KeyNotFoundException">No such action.</exception>
    public EditorAction this[string id] => _byId[id];

    /// <summary>Runs the enabled action bound to a key, if any.</summary>
    /// <param name="e">The key event.</param>
    /// <returns>Whether an action ran.</returns>
    public bool TryShortcut(in KeyEvent e)
    {
        foreach (EditorAction action in _byId.Values)
        {
            if (action.Shortcut is { } chord && chord.Matches(e) && action.TryExecute())
            {
                return true;
            }
        }

        return false;
    }
}

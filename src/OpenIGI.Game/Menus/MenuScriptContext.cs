using OpenIGI.Core.Diagnostics;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Menus;

/// <summary>
/// The namespace a menu action expression is evaluated against: the manager's nine natives, plus
/// whatever else the host supplies.
/// </summary>
/// <remarks>
/// <para>
/// The nine names and their argument counts come from the manager's constructor (<c>0x417260</c>),
/// which registers them with <c>Script_RegisterNative</c>. Everything else a menu script calls belongs
/// to a subsystem that does not exist yet — <c>Config_*</c>, <c>LevelFlow_*</c>, <c>Game_*</c> — and is
/// handed to <see cref="Fallback"/> rather than guessed at.
/// </para>
/// </remarks>
public sealed class MenuScriptContext : IScriptExpressionContext
{
    private const string LogCategory = "MenuScript";

    private readonly MenuNavigator _stack;
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MenuScriptContext"/> class.</summary>
    /// <param name="stack">The screen stack the navigation natives drive.</param>
    /// <param name="logger">Where to report calls that reach <see cref="Fallback"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MenuScriptContext(MenuNavigator stack, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _stack = stack;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets or sets the handler for names this context does not implement.</summary>
    /// <remarks>Returns the value the call should evaluate to. The default logs and answers zero.</remarks>
    public Func<string, int[], int>? Fallback { get; set; }

    /// <summary>Gets the names this context implements itself.</summary>
    public static IReadOnlyList<string> NativeNames { get; } =
    [
        "MenuManager_RequestScreen",
        "MenuManager_LeaveMenus",
        "MenuManager_SetLanguage",
        "MenuManager_PushScreen",
        "MenuManager_PopScreen",
        "MenuManager_ForceUpdateWindow",
        "MenuManager_ActivatePopupScreen",
        "MenuManager_DeactivatePopuScreen",
        "MenuManager_SetEnabled",
    ];

    /// <summary>Gets or sets a value indicating whether the manager takes input.</summary>
    /// <remarks><c>MenuManager_SetEnabled</c> writes this.</remarks>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets the language the script last selected, or an empty string.</summary>
    public string Language { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool TryGetValue(string name, out int value)
    {
        _logger.Debug(LogCategory, $"read {name} (unimplemented, reads 0)");
        value = 0;
        return true;
    }

    /// <inheritdoc/>
    public bool TrySetValue(string name, int value)
    {
        _logger.Debug(LogCategory, $"write {name} = {value} (unimplemented)");
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// All three navigation natives return 1, so an expression such as
    /// <c>MenuManager_RequestScreen(904, FALSE) &amp;&amp; Game_UpdateVolume()</c> runs both sides.
    /// </remarks>
    public bool TryInvoke(string name, ReadOnlySpan<int> arguments, out int result)
    {
        result = 1;

        switch (name)
        {
            case "MenuManager_PushScreen" when arguments.Length >= 1:
                _stack.Push(arguments[0]);
                return true;

            case "MenuManager_PopScreen" when arguments.Length >= 1:
                _stack.Pop(arguments[0] != 0);
                return true;

            case "MenuManager_RequestScreen" when arguments.Length >= 2:
                _stack.Request(arguments[0], arguments[1] != 0);
                return true;

            case "MenuManager_ActivatePopupScreen" when arguments.Length >= 1:
                _stack.ActivatePopup(arguments[0]);
                return true;

            case "MenuManager_DeactivatePopuScreen":
                _stack.DeactivatePopup();
                return true;

            case "MenuManager_SetEnabled" when arguments.Length >= 1:
                IsEnabled = arguments[0] != 0;
                return true;

            case "MenuManager_LeaveMenus" when arguments.Length >= 1:
                _stack.Leave((MenuManagerState)arguments[0]);
                return true;

            case "MenuManager_SetLanguage" when arguments.Length >= 1:
                Language = arguments[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;

            case "MenuManager_ForceUpdateWindow":
                // Re-runs a screen's data expressions; harmless to treat as a no-op until the
                // On Change Data pass exists.
                return true;

            default:
                int[] copied = arguments.ToArray();
                if (Fallback is not null)
                {
                    result = Fallback(name, copied);
                    return true;
                }

                _logger.Information(
                    LogCategory,
                    $"{name}({string.Join(", ", copied)}) (unimplemented, returns 0)");
                result = 0;
                return true;
        }
    }
}

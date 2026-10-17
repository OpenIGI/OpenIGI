using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi1.Menus;

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

    /// <summary>The prefix an expression uses for the task it belongs to.</summary>
    /// <remarks>
    /// Retail menu scripts write <c>this.nUserData</c>, <c>this.isOn</c>, <c>this.vValue</c> and
    /// <c>this.nActiveID</c>. It resolves against whatever task the expression is running for.
    /// </remarks>
    public const string SelfPrefix = "this.";

    private readonly MenuNavigator _stack;
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MenuScriptContext"/> class.</summary>
    /// <param name="stack">The screen stack the navigation natives drive.</param>
    /// <param name="logger">Where to report calls that reach <see cref="Fallback"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MenuScriptContext(MenuNavigator stack, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _stack = stack;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets or sets the handler for names this context does not implement.</summary>
    /// <remarks>
    /// Returns the value the call should evaluate to; the default logs and answers zero. The arguments
    /// arrive unevaluated, so a handler pulls each one in the type it wants — which is what a native
    /// does in the original, and the only way <c>Config_SoundOptionsSetMusicVolume(this.vValue)</c>
    /// sees <c>0.75</c> rather than <c>0</c>.
    /// </remarks>
    public Func<string, ScriptArguments, QvmValue>? Fallback { get; set; }

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

    /// <summary>Gets or sets the registry a member path resolves through.</summary>
    /// <remarks>
    /// The named-variable export from <c>task-system.md</c> §8: a task binds <c>TypeName_id.field</c>
    /// as a live alias onto its own field, so <c>MenuScreen_905.nUserData</c> is a symbol lookup and
    /// nothing more. Menu expressions read <em>and write</em> these.
    /// </remarks>
    public ScriptVarRegistry? Variables { get; set; }

    /// <summary>Gets or sets the task <c>this.</c> refers to while an expression runs.</summary>
    public GameTask? Self { get; set; }

    /// <summary>Resolves a member path against the current task, expanding <c>this.</c>.</summary>
    /// <param name="name">The identifier.</param>
    /// <returns>The full symbol name, or <see langword="null"/> when the name is not a member path.</returns>
    public string? ResolveMemberPath(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        if (!name.StartsWith(SelfPrefix, StringComparison.Ordinal))
        {
            return name;
        }

        return Self is null ? null : $"{Self.Name}.{name[SelfPrefix.Length..]}";
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out QvmValue value)
    {
        if (Variables is not null
            && ResolveMemberPath(name) is { } symbol
            && Variables.TryRead(symbol, out value))
        {
            return true;
        }

        _logger.Debug(LogCategory, $"read {name} (unresolved, reads 0)");
        value = QvmValue.Zero;
        return true;
    }

    /// <inheritdoc/>
    public QvmValueKind GetValueKind(string name) =>
        Variables is not null && ResolveMemberPath(name) is { } symbol
            ? Variables.GetValueKind(symbol)
            : QvmValueKind.None;

    /// <inheritdoc/>
    public bool TrySetValue(string name, QvmValue value)
    {
        if (Variables is not null
            && ResolveMemberPath(name) is { } symbol
            && Variables.TryWrite(symbol, value))
        {
            return true;
        }

        _logger.Debug(LogCategory, $"write {name} = {value} (unresolved)");
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// All three navigation natives return 1, so an expression such as
    /// <c>MenuManager_RequestScreen(904, FALSE) &amp;&amp; Game_UpdateVolume()</c> runs both sides.
    /// </remarks>
    public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        result = QvmValue.FromInt32(1);

        switch (name)
        {
            case "MenuManager_PushScreen" when arguments.Count >= 1:
                _stack.Push(arguments.GetInt32(0));
                return true;

            case "MenuManager_PopScreen" when arguments.Count >= 1:
                _stack.Pop(arguments.GetBoolean(0));
                return true;

            case "MenuManager_RequestScreen" when arguments.Count >= 2:
                _stack.Request(arguments.GetInt32(0), arguments.GetBoolean(1));
                return true;

            case "MenuManager_ActivatePopupScreen" when arguments.Count >= 1:
                _stack.ActivatePopup(arguments.GetInt32(0));
                return true;

            case "MenuManager_DeactivatePopuScreen":
                _stack.DeactivatePopup();
                return true;

            case "MenuManager_SetEnabled" when arguments.Count >= 1:
                IsEnabled = arguments.GetBoolean(0);
                return true;

            // The state is the SECOND argument: the retail scripts read
            // MenuManager_LeaveMenus(-1, MENUMANAGER_STARTGAME) and MenuManager_LeaveMenus(0,
            // MENUMANAGER_QUIT). The original (0x417790) reads argument 0 into a field of its own and
            // argument 1 as the state; taking the first as the state made starting a mission throw.
            case "MenuManager_LeaveMenus" when arguments.Count >= 2:
                _stack.Leave((MenuManagerState)arguments.GetInt32(1), arguments.GetInt32(0));
                return true;

            case "MenuManager_SetLanguage" when arguments.Count >= 1:
                Language = MenuStringTable.LanguageNameOf(arguments.GetInt32(0));
                return true;

            case "MenuManager_ForceUpdateWindow":
                // Re-runs a screen's data expressions; harmless to treat as a no-op until the
                // On Change Data pass exists.
                return true;

            default:
                if (Fallback is not null)
                {
                    result = Fallback(name, arguments);
                    return true;
                }

                _logger.Information(LogCategory, $"{name}/{arguments.Count} (unimplemented, returns 0)");
                result = QvmValue.Zero;
                return true;
        }
    }
}

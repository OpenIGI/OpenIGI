using OpenIGI.Core.Diagnostics;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Desktop;

/// <summary>
/// The namespace a menu action expression is evaluated against in the shell.
/// </summary>
/// <remarks>
/// <para>
/// The natives a menu entry calls — <c>MenuManager_PushScreen</c>, <c>Config_*</c>,
/// <c>LevelFlow_*</c> — belong to subsystems that do not exist yet. Rather than stub them with
/// plausible return values, this context reports every call and every read to the log and answers
/// zero, so what the menu is asking for is visible and nothing is silently faked.
/// </para>
/// <para>
/// It is replaced, not extended, once the screen stack and the configuration natives land.
/// </para>
/// </remarks>
public sealed class DesktopMenuContext : IScriptExpressionContext
{
    private const string LogCategory = "MenuScript";

    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="DesktopMenuContext"/> class.</summary>
    /// <param name="logger">Where to report the calls the menu makes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public DesktopMenuContext(IIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out int value)
    {
        _logger.Debug(LogCategory, $"read {name} (not implemented, reads 0)");
        value = 0;
        return true;
    }

    /// <inheritdoc/>
    public bool TrySetValue(string name, int value)
    {
        _logger.Debug(LogCategory, $"write {name} = {value} (not implemented)");
        return true;
    }

    /// <inheritdoc/>
    public bool TryInvoke(string name, ReadOnlySpan<int> arguments, out int result)
    {
        _logger.Information(
            LogCategory,
            arguments.Length == 0
                ? $"{name}() (not implemented, returns 0)"
                : $"{name}({string.Join(", ", arguments.ToArray())}) (not implemented, returns 0)");

        result = 0;
        return true;
    }
}

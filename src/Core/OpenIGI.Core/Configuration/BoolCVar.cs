namespace OpenIGI.Core.Configuration;

/// <summary>
/// A console variable holding a switch.
/// </summary>
/// <remarks>
/// Stored as <c>0</c> or <c>1</c> so the settings file matches what the console prints, and accepted
/// as any of the spellings a player might type — <c>1</c>, <c>true</c>, <c>on</c>, <c>yes</c>, and
/// their negatives.
/// </remarks>
public sealed class BoolCVar : CVar<bool>
{
    private static readonly string[] TrueWords = ["1", "true", "on", "yes", "enabled"];
    private static readonly string[] FalseWords = ["0", "false", "off", "no", "disabled"];

    // The canonical pair only. Completion offers what the variable stores and prints, not every
    // spelling it will accept -- a menu of five synonyms for "on" is a worse menu, not a richer one.
    private static readonly string[] CanonicalChoices = ["0", "1"];

    /// <summary>Initialises a new instance of the <see cref="BoolCVar"/> class.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    public BoolCVar(string name, bool defaultValue, CVarOptions flags = CVarOptions.None, string help = "")
        : base(name, defaultValue, flags, help)
    {
    }

    /// <inheritdoc/>
    public override string RangeText => "0 | 1";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ValueChoices => CanonicalChoices;

    /// <inheritdoc/>
    protected override string FriendlyTypeName => "boolean";

    /// <inheritdoc/>
    protected override bool TryParse(string text, out bool value)
    {
        string trimmed = text.Trim();

        if (Array.Exists(TrueWords, word => word.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            value = true;
            return true;
        }

        value = false;
        return Array.Exists(FalseWords, word => word.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    protected override string Format(bool value) => value ? "1" : "0";
}

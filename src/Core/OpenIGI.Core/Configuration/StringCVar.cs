namespace OpenIGI.Core.Configuration;

/// <summary>
/// A console variable holding free text.
/// </summary>
/// <remarks>
/// Used for paths, profile names and preset names. Values are stored verbatim; the settings writer
/// quotes them when they contain white space so a round trip is lossless.
/// </remarks>
public sealed class StringCVar : CVar<string>
{
    /// <summary>Initialises a new instance of the <see cref="StringCVar"/> class.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    public StringCVar(string name, string defaultValue, CVarOptions flags = CVarOptions.None, string help = "")
        : base(name, defaultValue ?? string.Empty, flags, help)
    {
    }

    /// <inheritdoc/>
    protected override string FriendlyTypeName => "text";

    /// <inheritdoc/>
    protected override bool TryParse(string text, out string value)
    {
        value = text;
        return true;
    }

    /// <inheritdoc/>
    protected override string Format(string value) => value;
}

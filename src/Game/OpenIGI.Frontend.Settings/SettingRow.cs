namespace OpenIGI.Frontend.Settings;

/// <summary>One row of a settings page that is a single variable shown as itself.</summary>
/// <param name="Variable">The variable the row drives.</param>
/// <param name="Caption">The row's caption, as the player reads it.</param>
/// <param name="Help">What the row does, in a sentence written for a player.</param>
/// <param name="Choices">
/// What a choice variable's values are called on screen, keyed by the stored value, or null to show them
/// as stored.
/// </param>
/// <param name="Step">A numeric row's slider step, or <see langword="null"/> for a twentieth of the variable's range.</param>
/// <remarks>
/// The caption and the help are written here rather than taken from the variable, and that is the whole
/// point of the type. A variable's own <c>Help</c> is written for whoever is reading the code behind it —
/// it says things like "windowed inverse square", "L1-SH layout" and "solver substeps" — and it is the
/// right text for the console to print. It is the wrong text to put in front of somebody who wants the
/// game to look or play the way they like.
/// </remarks>
public sealed record SettingRow(
    string Variable,
    string Caption,
    string Help,
    IReadOnlyDictionary<string, string>? Choices = null,
    float? Step = null)
{
    /// <summary>Gets a value indicating whether the row waits behind its panel's Advanced expander.</summary>
    public bool Advanced { get; init; }

    /// <summary>
    /// Gets the stored values the row shows but never offers -- a profile's <c>custom</c>, which is what the settings read as
    /// once one is changed by hand, and which there is nothing to write for -- or <see langword="null"/> for none.
    /// </summary>
    public IReadOnlyList<string>? Readings { get; init; }

    /// <summary>Gets the setting the row is shown only while it is on, or <see langword="null"/> for a row always shown.</summary>
    public RowCondition? ShownWhen { get; init; }

    /// <summary>Gets what choosing one of the row's values also sets, or <see langword="null"/> for nothing.</summary>
    public RowEffect? OnChosen { get; init; }
}

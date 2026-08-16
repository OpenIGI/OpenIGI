namespace OpenIGI.Core.Configuration;

/// <summary>
/// One ranked completion candidate, carrying everything the console needs to describe it without
/// going back to the registries.
/// </summary>
/// <remarks>
/// <para>
/// The older completion path returned bare strings, which left the view knowing a name and nothing
/// else — so the reader had to complete a variable and press return just to find out what it currently
/// held or what it would accept. Carrying the value, the range and the help alongside the name turns
/// the dropdown into the answer rather than into a step towards it.
/// </para>
/// <para>
/// <see cref="Score"/> is exposed because ordering is the whole contract of a completion list, and a
/// test that cannot see the score can only assert the order, not the reason for it.
/// </para>
/// </remarks>
/// <param name="Text">The text that replaces the token being completed.</param>
/// <param name="Kind">What the candidate refers to.</param>
/// <param name="Detail">The current value for a variable, the expansion for an alias, the usage for a command.</param>
/// <param name="Range">What the variable accepts, or an empty string when it is unconstrained.</param>
/// <param name="Help">The one-line description, or an empty string when there is none.</param>
/// <param name="Score">How well the candidate matched; higher sorts first.</param>
/// <param name="IsBlocked">Whether the candidate is a cheat that the current session may not use.</param>
public readonly record struct ConsoleSuggestion(
    string Text,
    ConsoleSuggestionKind Kind,
    string Detail = "",
    string Range = "",
    string Help = "",
    int Score = 0,
    bool IsBlocked = false)
{
    /// <summary>Returns the candidate's text.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Text;
}

namespace OpenIGI.Core.Configuration;

/// <summary>
/// What a completion candidate refers to.
/// </summary>
/// <remarks>
/// The view colours a suggestion by this rather than by re-asking the registries what the text means,
/// which is both cheaper and the only way a value suggestion can be told apart from a variable that
/// happens to share its spelling.
/// </remarks>
public enum ConsoleSuggestionKind
{
    /// <summary>A registered console command.</summary>
    Command,

    /// <summary>A registered console variable.</summary>
    Variable,

    /// <summary>A user-defined alias.</summary>
    Alias,

    /// <summary>One of the values the variable being assigned accepts.</summary>
    Value,

    /// <summary>A line recalled from the submission history.</summary>
    History,
}

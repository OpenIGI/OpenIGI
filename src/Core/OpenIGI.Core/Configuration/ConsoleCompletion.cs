namespace OpenIGI.Core.Configuration;

/// <summary>
/// The result of asking the console to complete a partially typed line.
/// </summary>
/// <param name="Candidates">Every name matching what was typed, ordered by name.</param>
/// <param name="CommonPrefix">
/// The longest prefix every candidate shares. Replacing the typed text with this is what makes a
/// single Tab press useful even when the completion is ambiguous.
/// </param>
public readonly record struct ConsoleCompletion(IReadOnlyList<string> Candidates, string CommonPrefix)
{
    /// <summary>Gets a completion with no candidates.</summary>
    public static ConsoleCompletion None { get; } = new([], string.Empty);

    /// <summary>Gets a value indicating whether anything matched.</summary>
    public bool HasCandidates => Candidates is { Count: > 0 };

    /// <summary>Gets a value indicating whether exactly one name matched.</summary>
    public bool IsUnambiguous => Candidates is { Count: 1 };
}

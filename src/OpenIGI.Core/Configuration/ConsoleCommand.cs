namespace OpenIGI.Core.Configuration;

/// <summary>
/// One console command: a name, what it does, and how to explain it.
/// </summary>
/// <param name="Name">The name the command is invoked by. Case-insensitive.</param>
/// <param name="Help">The one-line description shown by <c>help</c> and by completion.</param>
/// <param name="Usage">The argument shape, for example <c>"&lt;key&gt; &lt;command&gt;"</c>. May be empty.</param>
/// <param name="Execute">What the command does.</param>
/// <param name="Flags">The command's behavioural attributes.</param>
/// <param name="CompleteArgument">
/// Returns the values an argument accepts, or <see langword="null"/> to let <see cref="Usage"/> answer.
/// </param>
public sealed record ConsoleCommand(
    string Name,
    string Help,
    string Usage,
    Action<ConsoleCommandContext> Execute,
    CommandOptions Flags = CommandOptions.None,
    Func<int, IReadOnlyList<string>>? CompleteArgument = null)
{
    /// <summary>Gets the command's name followed by its usage, as completion shows it.</summary>
    public string Signature => Usage.Length == 0 ? Name : $"{Name} {Usage}";

    /// <summary>Returns the usage token describing one argument, stripped of its brackets.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <returns>The token, or an empty string when the usage does not describe that argument.</returns>
    /// <remarks>
    /// <para>
    /// Split on whitespace <em>outside</em> brackets, because the shape most commands are declared
    /// with — <c>"[vanilla | enhanced | high | ultra]"</c> — is one argument containing spaces. Naive
    /// splitting reads it as four.
    /// </para>
    /// <para>
    /// A trailing token that ends in an ellipsis describes every remaining argument, so it keeps
    /// answering past its own position; that is what <c>"&lt;key&gt; &lt;command...&gt;"</c> means.
    /// </para>
    /// </remarks>
    public string ArgumentUsage(int index)
    {
        if (index < 0 || Usage.Length == 0)
        {
            return string.Empty;
        }

        int depth = 0;
        int start = 0;
        int found = 0;
        string last = string.Empty;

        for (int i = 0; i <= Usage.Length; i++)
        {
            char c = i < Usage.Length ? Usage[i] : ' ';

            if (c is '[' or '<')
            {
                depth++;
            }
            else if (c is ']' or '>')
            {
                depth = Math.Max(0, depth - 1);
            }

            if (!char.IsWhiteSpace(c) || depth != 0 || i == start)
            {
                continue;
            }

            last = Strip(Usage[start..i]);
            if (found == index)
            {
                return last;
            }

            found++;
            start = i + 1;
        }

        // Past the last described argument, a repeating token still applies.
        return last.EndsWith("...", StringComparison.Ordinal) ? last : string.Empty;
    }

    /// <summary>Returns the literal alternatives an argument accepts, if its usage lists any.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <returns>The alternatives, or an empty list when the usage does not enumerate them.</returns>
    /// <remarks>
    /// <c>|</c> separates alternatives, the same convention <see cref="ICVar.RangeText"/> uses for an
    /// enumeration — so a command declared the obvious way completes without being told to.
    /// </remarks>
    public IReadOnlyList<string> ArgumentChoices(int index)
    {
        if (CompleteArgument is { } explicitChoices)
        {
            return explicitChoices(index);
        }

        string token = ArgumentUsage(index);
        if (!token.Contains('|', StringComparison.Ordinal))
        {
            return [];
        }

        List<string> choices = [];
        foreach (string part in token.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            choices.Add(part);
        }

        return choices;
    }

    private static string Strip(string token)
    {
        string trimmed = token.Trim();

        while (trimmed.Length >= 2 &&
               ((trimmed[0] == '[' && trimmed[^1] == ']') || (trimmed[0] == '<' && trimmed[^1] == '>')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }
}

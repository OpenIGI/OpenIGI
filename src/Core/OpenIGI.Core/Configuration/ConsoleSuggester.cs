namespace OpenIGI.Core.Configuration;

/// <summary>
/// Ranks completion candidates for a partially typed console line.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="ConsoleSystem.Complete"/>, which stays exactly as it was. That method
/// answers a narrow question — "what common prefix can Tab safely insert?" — and its prefix-only,
/// first-token-only behaviour is the correct answer to it. This answers a different question: "what
/// might the reader mean, best first?" Ranking, fuzzy matching and value completion all belong to the
/// second question and would be wrong in the first.
/// </para>
/// <para>
/// <b>Scoring is deterministic and total</b> so that the order is a testable property rather than an
/// accident of enumeration. A candidate scores on how it matched, ties break on name, and nothing
/// depends on registration order.
/// </para>
/// </remarks>
public static class ConsoleSuggester
{
    /// <summary>An exact, case-insensitive match.</summary>
    private const int ExactScore = 1000;

    /// <summary>A prefix match, less the candidate's length so the shortest full match wins.</summary>
    private const int PrefixBase = 800;

    /// <summary>A match starting at an underscore-separated segment, so <c>fog</c> finds <c>r_fog_quality</c>.</summary>
    private const int SegmentScore = 600;

    /// <summary>A contiguous match anywhere in the name.</summary>
    private const int SubstringScore = 400;

    /// <summary>A subsequence match, less the gaps it had to jump.</summary>
    private const int SubsequenceBase = 200;

    /// <summary>Ranks the candidates for a line being typed.</summary>
    /// <param name="console">The console whose registries are searched.</param>
    /// <param name="input">The line as typed so far.</param>
    /// <param name="limit">The most candidates to return.</param>
    /// <returns>The candidates, best first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is not positive.</exception>
    public static IReadOnlyList<ConsoleSuggestion> Suggest(ConsoleSystem console, string? input, int limit = 32)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        string line = input ?? string.Empty;

        // Only the last statement on the line is being typed; anything before a ';' is already settled.
        int statementStart = LastStatementStart(line);
        string statement = line[statementStart..];

        string leading = statement.TrimStart();

        int firstSpace = IndexOfUnquotedSpace(leading);
        if (firstSpace < 0)
        {
            return RankNames(console, leading, limit);
        }

        // Past the first space the line names something; complete the value it is being given.
        string name = leading[..firstSpace];
        string rest = leading[(firstSpace + 1)..];

        int tokenStart = LastUnquotedSpace(rest) + 1;
        string valuePrefix = rest[tokenStart..];
        int argument = CountArguments(rest[..tokenStart]);

        return RankValues(console, name, valuePrefix, argument, limit);
    }

    /// <summary>Scores a candidate against what has been typed, or returns null when it does not match.</summary>
    /// <param name="candidate">The candidate name.</param>
    /// <param name="query">The text typed so far.</param>
    /// <returns>The score, or <see langword="null"/> when the candidate does not match at all.</returns>
    /// <remarks>
    /// An empty query matches everything at the same score, which is what makes an empty prompt list
    /// the whole registry in name order rather than in registration order.
    /// </remarks>
    public static int? Score(string candidate, string query)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Length == 0)
        {
            return 0;
        }

        if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return ExactScore;
        }

        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return PrefixBase - candidate.Length;
        }

        // A prefixed registry makes segment matching the one that feels like it reads minds: the
        // reader knows the variable is about fog but not whether it is r_fog_ or r_volumetric_fog_.
        int segment = candidate.IndexOf('_', StringComparison.Ordinal);
        while (segment >= 0 && segment + 1 < candidate.Length)
        {
            if (string.CompareOrdinal(candidate, segment + 1, query, 0, query.Length) == 0 ||
                candidate.AsSpan(segment + 1).StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return SegmentScore - candidate.Length;
            }

            segment = candidate.IndexOf('_', segment + 1);
        }

        if (candidate.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return SubstringScore - candidate.Length;
        }

        return SubsequenceScore(candidate, query);
    }

    /// <summary>Scores a scattered match, penalising how far apart the matched characters sat.</summary>
    /// <param name="candidate">The candidate name.</param>
    /// <param name="query">The text typed so far.</param>
    /// <returns>The score, or <see langword="null"/> when the query is not a subsequence at all.</returns>
    private static int? SubsequenceScore(string candidate, string query)
    {
        int at = 0;
        int gaps = 0;
        int previous = -1;

        foreach (char wanted in query)
        {
            int found = -1;
            for (int i = at; i < candidate.Length; i++)
            {
                if (char.ToLowerInvariant(candidate[i]) == char.ToLowerInvariant(wanted))
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            if (previous >= 0)
            {
                gaps += found - previous - 1;
            }

            previous = found;
            at = found + 1;
        }

        return SubsequenceBase - gaps;
    }

    private static List<ConsoleSuggestion> RankNames(ConsoleSystem console, string query, int limit)
    {
        List<ConsoleSuggestion> matches = [];

        foreach (ConsoleCommand command in console.Commands.All)
        {
            if ((command.Flags & CommandOptions.Hidden) != 0)
            {
                continue;
            }

            if (Score(command.Name, query) is { } score)
            {
                bool blocked = (command.Flags & CommandOptions.Cheat) != 0 && !console.CheatsEnabled;
                matches.Add(new ConsoleSuggestion(
                    command.Name,
                    ConsoleSuggestionKind.Command,
                    command.Usage,
                    string.Empty,
                    command.Help,
                    score,
                    blocked));
            }
        }

        foreach (ICVar variable in console.Variables.All)
        {
            if ((variable.Flags & CVarOptions.Hidden) != 0)
            {
                continue;
            }

            if (Score(variable.Name, query) is { } score)
            {
                bool blocked = (variable.Flags & CVarOptions.Cheat) != 0 && !console.CheatsEnabled;
                matches.Add(new ConsoleSuggestion(
                    variable.Name,
                    ConsoleSuggestionKind.Variable,
                    variable.ValueText,
                    variable.RangeText,
                    variable.Help,
                    score,
                    blocked));
            }
        }

        foreach ((string alias, string expansion) in console.Aliases)
        {
            if (Score(alias, query) is { } score)
            {
                matches.Add(new ConsoleSuggestion(alias, ConsoleSuggestionKind.Alias, expansion, string.Empty, string.Empty, score));
            }
        }

        return Order(matches, limit);
    }

    private static List<ConsoleSuggestion> RankValues(
        ConsoleSystem console,
        string name,
        string query,
        int argument,
        int limit)
    {
        if (!console.Variables.TryGet(name, out ICVar? variable))
        {
            return console.Commands.TryGet(name, out ConsoleCommand? command)
                ? RankArguments(console, command, query, argument, limit)
                : [];
        }

        List<ConsoleSuggestion> matches = [];
        foreach (string choice in variable.ValueChoices)
        {
            if (Score(choice, query) is { } score)
            {
                // The value in hand is worth pointing at, because "which one is it on now" is the
                // question a reader has before "which ones are there".
                bool current = choice.Equals(variable.ValueText, StringComparison.OrdinalIgnoreCase);
                bool @default = choice.Equals(variable.DefaultText, StringComparison.OrdinalIgnoreCase);
                string detail = (current, @default) switch
                {
                    (true, true) => "current, default",
                    (true, false) => "current",
                    (false, true) => "default",
                    _ => string.Empty,
                };

                matches.Add(new ConsoleSuggestion(choice, ConsoleSuggestionKind.Value, detail, string.Empty, string.Empty, score));
            }
        }

        return Order(matches, limit);
    }

    /// <summary>
    /// Ranks what a command's argument accepts.
    /// </summary>
    /// <param name="console">The console whose registries name-taking arguments draw from.</param>
    /// <param name="command">The command being given an argument.</param>
    /// <param name="query">What has been typed of the argument so far.</param>
    /// <param name="argument">Which argument it is, counting from zero.</param>
    /// <param name="limit">The most candidates to return.</param>
    /// <returns>The candidates, best first.</returns>
    /// <remarks>
    /// Most commands need nothing beyond the usage string they were already declared with —
    /// <c>gfx_preset [vanilla | enhanced | high | ultra]</c> completes because the alternatives are
    /// written right there. The two placeholders worth understanding are <c>&lt;variable&gt;</c> and
    /// <c>&lt;command&gt;</c>, which stand for the registries rather than for a fixed list, and which
    /// are what make <c>help</c>, <c>reset</c>, <c>toggle</c> and <c>watch</c> complete usefully.
    /// </remarks>
    private static List<ConsoleSuggestion> RankArguments(
        ConsoleSystem console,
        ConsoleCommand command,
        string query,
        int argument,
        int limit)
    {
        List<ConsoleSuggestion> matches = [];

        foreach (string choice in command.ArgumentChoices(argument))
        {
            if (Score(choice, query) is { } score)
            {
                matches.Add(new ConsoleSuggestion(choice, ConsoleSuggestionKind.Value, string.Empty, string.Empty, string.Empty, score));
            }
        }

        if (matches.Count > 0)
        {
            return Order(matches, limit);
        }

        string placeholder = command.ArgumentUsage(argument).TrimEnd('.');

        if (placeholder.Equals("variable", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Equals("cvar", StringComparison.OrdinalIgnoreCase))
        {
            foreach (ICVar variable in console.Variables.All)
            {
                if ((variable.Flags & CVarOptions.Hidden) == 0 && Score(variable.Name, query) is { } score)
                {
                    matches.Add(new ConsoleSuggestion(
                        variable.Name,
                        ConsoleSuggestionKind.Variable,
                        variable.ValueText,
                        variable.RangeText,
                        variable.Help,
                        score));
                }
            }
        }
        else if (placeholder.Equals("command", StringComparison.OrdinalIgnoreCase))
        {
            foreach (ConsoleCommand other in console.Commands.All)
            {
                if ((other.Flags & CommandOptions.Hidden) == 0 && Score(other.Name, query) is { } score)
                {
                    matches.Add(new ConsoleSuggestion(
                        other.Name,
                        ConsoleSuggestionKind.Command,
                        other.Usage,
                        string.Empty,
                        other.Help,
                        score));
                }
            }
        }

        return Order(matches, limit);
    }

    /// <summary>Counts how many complete arguments precede a point in a line.</summary>
    /// <param name="text">The text before the argument being typed.</param>
    /// <returns>The zero-based index of the argument now being typed.</returns>
    private static int CountArguments(string text)
    {
        int count = 0;
        bool quoted = false;
        bool inToken = false;

        foreach (char c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                inToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (inToken)
                {
                    count++;
                    inToken = false;
                }

                continue;
            }

            inToken = true;
        }

        return count + (inToken ? 1 : 0);
    }

    /// <summary>Finds the last space that is not inside quotes, or -1.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The index, or -1 when there is none.</returns>
    private static int LastUnquotedSpace(string text)
    {
        bool quoted = false;
        int last = -1;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                last = i;
            }
        }

        return last;
    }

    private static List<ConsoleSuggestion> Order(List<ConsoleSuggestion> matches, int limit)
    {
        matches.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
        });

        if (matches.Count > limit)
        {
            matches.RemoveRange(limit, matches.Count - limit);
        }

        return matches;
    }

    /// <summary>Finds where the last statement on the line begins, respecting quotes.</summary>
    /// <param name="line">The line being typed.</param>
    /// <returns>The index just past the last unquoted separator, or zero.</returns>
    private static int LastStatementStart(string line)
    {
        bool quoted = false;
        int start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ';' && !quoted)
            {
                start = i + 1;
            }
        }

        return start;
    }

    /// <summary>Finds the first space that is not inside quotes.</summary>
    /// <param name="text">The statement being typed.</param>
    /// <returns>The index, or -1 when the statement is still one token.</returns>
    private static int IndexOfUnquotedSpace(string text)
    {
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                return i;
            }
        }

        return -1;
    }
}

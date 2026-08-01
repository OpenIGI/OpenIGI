namespace OpenIGI.Core.Configuration;

/// <summary>
/// Splits console input into statements and statements into tokens.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is the one every game console has used since Quake, because it is the one players and
/// config files already know: statements separated by <c>;</c>, tokens separated by white space,
/// double quotes to keep white space inside a token, and <c>//</c> starting a comment that runs to the
/// end of the line. Quoting suspends all three separators, so
/// <c>bind f5 "exec quicksave.cfg; echo saved"</c> binds one two-statement script.
/// </para>
/// <para>
/// A closing quote is optional at end of input — an unterminated string yields the rest of the line
/// rather than an error, since refusing to run a half-typed line helps nobody.
/// </para>
/// </remarks>
public static class CommandTokenizer
{
    /// <summary>Splits a line into the statements it contains.</summary>
    /// <param name="line">The input to split. May be <see langword="null"/>.</param>
    /// <returns>The statements, trimmed, with empty ones and comments removed.</returns>
    public static IReadOnlyList<string> SplitStatements(string? line)
    {
        List<string> statements = [];
        if (string.IsNullOrEmpty(line))
        {
            return statements;
        }

        int start = 0;
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (quoted)
            {
                continue;
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                Emit(statements, line, start, i);
                return statements;
            }

            if (c == ';')
            {
                Emit(statements, line, start, i);
                start = i + 1;
            }
        }

        Emit(statements, line, start, line.Length);
        return statements;
    }

    /// <summary>Splits one statement into its tokens.</summary>
    /// <param name="statement">The statement to split. May be <see langword="null"/>.</param>
    /// <returns>The tokens, with quotes removed.</returns>
    /// <remarks>
    /// An empty quoted token (<c>""</c>) is preserved, which is how a text variable is cleared:
    /// <c>ui_profile ""</c>.
    /// </remarks>
    public static IReadOnlyList<string> Tokenize(string? statement)
    {
        List<string> tokens = [];
        if (string.IsNullOrEmpty(statement))
        {
            return tokens;
        }

        int i = 0;
        while (i < statement.Length)
        {
            while (i < statement.Length && char.IsWhiteSpace(statement[i]))
            {
                i++;
            }

            if (i >= statement.Length)
            {
                break;
            }

            if (statement[i] == '"')
            {
                int close = statement.IndexOf('"', i + 1);
                if (close < 0)
                {
                    tokens.Add(statement[(i + 1)..]);
                    break;
                }

                tokens.Add(statement[(i + 1)..close]);
                i = close + 1;
                continue;
            }

            int end = i;
            while (end < statement.Length && !char.IsWhiteSpace(statement[end]) && statement[end] != '"')
            {
                end++;
            }

            tokens.Add(statement[i..end]);
            i = end;
        }

        return tokens;
    }

    /// <summary>Renders a token so that <see cref="Tokenize"/> reads it back unchanged.</summary>
    /// <param name="token">The token to render. May be <see langword="null"/>.</param>
    /// <returns>The token, quoted when it is empty or contains white space, a quote, a semicolon or a slash.</returns>
    /// <remarks>Used by the settings writer so that paths and profile names survive a round trip.</remarks>
    public static string Quote(string? token)
    {
        string text = token ?? string.Empty;
        bool needsQuotes = text.Length == 0 || text.AsSpan().ContainsAny(" \t\";/");
        return needsQuotes ? $"\"{text.Replace("\"", string.Empty, StringComparison.Ordinal)}\"" : text;
    }

    private static void Emit(List<string> statements, string line, int start, int end)
    {
        string statement = line[start..end].Trim();
        if (statement.Length > 0)
        {
            statements.Add(statement);
        }
    }
}

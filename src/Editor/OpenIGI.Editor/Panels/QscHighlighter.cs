using OpenIGI.Core.Graphics;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor.Panels;

/// <summary>Colours QSC one line at a time: keywords, calls, constants, strings, numbers and comments.</summary>
/// <remarks>
/// Uses the parser's own identifier rules (<see cref="QscLexer.IsIdentifierStart"/>), so what is coloured as a name is
/// what the compiler reads as one. A block comment is coloured to the end of its line; the scripts the decompiler
/// prints have none, and retail sources use line comments.
/// </remarks>
internal static class QscHighlighter
{
    public static IReadOnlyList<CodeSpan> Line(EditorTheme theme, string line)
    {
        List<CodeSpan> spans = [];
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            int start = i;
            if (c == '/' && i + 1 < line.Length && line[i + 1] is '/' or '*')
            {
                spans.Add(new CodeSpan(i, line.Length - i, theme.SyntaxComment));
                break;
            }

            if (c == '"')
            {
                i++;
                while (i < line.Length && line[i] != '"')
                {
                    i += line[i] == '\\' ? 2 : 1;
                }

                i = Math.Min(line.Length, i + 1);
                spans.Add(new CodeSpan(start, i - start, theme.SyntaxString));
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < line.Length && char.IsAsciiDigit(line[i + 1])))
            {
                while (i < line.Length && (char.IsAsciiLetterOrDigit(line[i]) || line[i] is '.' or '+' or '-') && !(line[i] is '+' or '-' && line[i - 1] is not ('e' or 'E')))
                {
                    i++;
                }

                spans.Add(new CodeSpan(start, i - start, theme.SyntaxNumber));
                continue;
            }

            if (QscLexer.IsIdentifierStart(c))
            {
                while (i < line.Length && QscLexer.IsIdentifierPart(line[i]))
                {
                    i++;
                }

                string word = line[start..i];
                int next = i;
                while (next < line.Length && line[next] == ' ')
                {
                    next++;
                }

                Bgra32? color = word switch
                {
                    "if" or "else" or "while" => theme.SyntaxKeyword,
                    _ when next < line.Length && line[next] == '(' => theme.SyntaxFunction,
                    _ when IsConstantName(word) => theme.SyntaxConstant,
                    _ => null,
                };

                if (color is { } ink)
                {
                    spans.Add(new CodeSpan(start, i - start, ink));
                }

                continue;
            }

            i++;
        }

        return spans;
    }

    // TRUE, FALSE, AIEVENT_IDLE: the script namespace's constants are written in capitals.
    private static bool IsConstantName(string word) =>
        word.Length > 1 && word.All(static c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
}

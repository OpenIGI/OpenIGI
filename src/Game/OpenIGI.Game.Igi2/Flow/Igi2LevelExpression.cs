using System.Text;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// Script text a level task carries in a <c>VarString</c> parameter, compiled as <c>Level_CompileLevelExpression</c>
/// (<c>igi2 0x545440</c>) compiles it and run as <c>Level_RunLevelExpression</c> (<c>0x545790</c>) runs it.
/// </summary>
/// <remarks>
/// <para>
/// The compile is a rewrite of the text, a word at a time (<c>String_GetWordEx</c>, <c>0x57CB40</c>), before it is parsed:
/// whitespace is dropped, a word beginning <c>this.</c> is given the owner's name in its place, a word naming
/// <c>.isExists</c> anywhere becomes <c>1</c>, and <c>//</c> between words cuts the line. The rest is copied as it stands,
/// a character at a time. The result is parsed as one expression (<c>Script_ParseLine</c>, <c>0x40D850</c>) and evaluated in
/// the float walk (<c>Expression_EvalFloat</c>, <c>0x557D00</c>), whose callers truncate it where they want a flag.
/// </para>
/// <para>
/// A task tests its expression before running it — text, compiled text and parsed tree all present — so an empty one, or
/// one that does not parse, is never run (<see cref="IsEmpty"/>).
/// </para>
/// </remarks>
public sealed class Igi2LevelExpression
{
    private readonly ScriptExpression? _tree;

    /// <summary>Initializes a new instance of the <see cref="Igi2LevelExpression"/> class.</summary>
    /// <param name="source">The parameter's text, or <see langword="null"/> for none.</param>
    /// <param name="owner">The owning task's unique name, which <c>this.</c> stands for (<c>Level_GetUniqueQTaskNameFromTask</c>).</param>
    /// <param name="natives">The namespace, whose integer constants the parse folds.</param>
    public Igi2LevelExpression(string? source, string owner, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(natives);
        Source = source ?? string.Empty;
        Compiled = Compile(Source, owner);
        if (Compiled.Length == 0)
        {
            return;
        }

        try
        {
            _tree = ScriptExpressionParser.Parse(Compiled, name => Igi2LineScript.Constant(natives, name));
        }
        catch (ScriptExpressionException)
        {
            _tree = null;
        }
    }

    /// <summary>Gets the parameter's text.</summary>
    public string Source { get; }

    /// <summary>Gets the text as the compile rewrote it.</summary>
    public string Compiled { get; }

    /// <summary>Gets a value indicating whether there is nothing to run: no text, nothing left of it, or no expression parsed.</summary>
    public bool IsEmpty => _tree is null;

    /// <summary>Rewrites script text as <c>Level_CompileLevelExpression</c> does.</summary>
    /// <param name="source">The text.</param>
    /// <param name="owner">The owning task's unique name.</param>
    /// <returns>The rewritten text.</returns>
    public static string Compile(string source, string owner)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(owner);

        StringBuilder target = new(source.Length);
        int at = 0;
        while (at < source.Length)
        {
            SkipWhitespace(source, ref at);
            if (at >= source.Length)
            {
                break;
            }

            if (TryWord(source, ref at, out string word))
            {
                word = LevelExpressionNames.ResolveThis(word, owner);
                target.Append(LevelExpressionNames.IsExistsProbe(word) ? "1" : word);
                continue;
            }

            if (source[at] == '/' && at + 1 < source.Length && source[at + 1] == '/')
            {
                while (at < source.Length && source[at] != '\n')
                {
                    at++;
                }

                continue;
            }

            target.Append(source[at++]);
        }

        return target.ToString();
    }

    /// <summary>Evaluates the expression in the float walk.</summary>
    /// <param name="natives">The namespace its names resolve in.</param>
    /// <param name="functions">The natives it may call.</param>
    /// <returns>Its value, or nought when it is empty.</returns>
    public double Run(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        return _tree is null ? 0.0 : new ScriptExpressionEvaluator(new Igi2ScriptContext(natives, functions)).EvaluateReal(_tree);
    }

    /// <summary>The value as a byte flag, the way its callers store it: chopped to a 64-bit integer (<c>fistp qword</c>), its low eight bits.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Whether the low byte of its truncation is set.</returns>
    public static bool Flag(double value) => (byte)Truncate(value) != 0;

    /// <summary>The value chopped to a 64-bit integer, as <c>(__int64)</c> under the game's chopping control word.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Its truncation.</returns>
    public static long Truncate(double value) => (long)Math.Truncate(value);

    private static void SkipWhitespace(string text, ref int at)
    {
        while (at < text.Length && text[at] is ' ' or '\t' or '\n' or '\r')
        {
            at++;
        }
    }

    // String_GetWordEx with its quote flag clear: a quoted string kept with its quotes and escapes, a number (hexadecimal
    // after 0x, else digits with points and one exponent), or a run of letters, digits, '_' and '.', after an optional '$'.
    private static bool TryWord(string text, ref int at, out string word)
    {
        int start = at;
        char first = text[at];
        if (first == '"')
        {
            int end = at + 1;
            while (end < text.Length && text[end] != '"')
            {
                end += text[end] == '\\' && end + 1 < text.Length ? 2 : 1;
            }

            if (end >= text.Length)
            {
                word = string.Empty;
                return false;
            }

            at = end + 1;
            word = text[start..at];
            return true;
        }

        int i = at;
        if (char.IsAsciiDigit(first))
        {
            if (first == '0' && i + 1 < text.Length && text[i + 1] is 'x' or 'X')
            {
                i += 2;
                while (i < text.Length && char.IsAsciiHexDigit(text[i]))
                {
                    i++;
                }
            }
            else
            {
                bool exponent = false;
                while (i < text.Length)
                {
                    if (text[i] == '.')
                    {
                        i++;
                    }

                    if (i < text.Length && text[i] is 'e' or 'E')
                    {
                        if (exponent)
                        {
                            break;
                        }

                        exponent = true;
                        i++;
                        if (i < text.Length && text[i] is '+' or '-')
                        {
                            i++;
                        }
                    }

                    if (i >= text.Length || !char.IsAsciiDigit(text[i]))
                    {
                        break;
                    }

                    i++;
                }
            }
        }
        else
        {
            if (first == '$')
            {
                i++;
            }

            while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '.'))
            {
                i++;
            }
        }

        if (i == start)
        {
            word = string.Empty;
            return false;
        }

        at = i;
        word = text[start..i];
        return true;
    }
}

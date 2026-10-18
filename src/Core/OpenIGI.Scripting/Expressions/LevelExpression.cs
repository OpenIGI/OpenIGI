using System.Text;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Scripting.Expressions;

/// <summary>How a title's retail engine turns a level task's script text into the expression it parses.</summary>
public enum LevelExpressionDialect
{
    /// <summary>
    /// Parsed as written; <c>this.</c> and <c>.isExists</c> are answered as each name is looked up. How the port has
    /// always read Project I.G.I.'s task slots.
    /// </summary>
    /// <remarks>
    /// Project I.G.I. has a text preprocessor too (<c>0x4F1400</c> writes the owner's name over <c>this</c>, and
    /// <c>0x4F1574</c> puts <c>1</c> in place of any token holding <c>.isExists</c>), so for the shipped levels the two
    /// readings agree. Whether its preprocessor is I.G.I. 2's word for word -- whitespace, comments and all -- has not
    /// been read, and until it has this dialect keeps the port's behaviour.
    /// </remarks>
    AsWritten,

    /// <summary>
    /// Rewritten a word at a time first, as I.G.I. 2's <c>Level_CompileLevelExpression</c> (<c>igi2 0x545440</c>) does:
    /// whitespace dropped, <c>this.</c> given the owner's name, a word naming <c>.isExists</c> made <c>1</c>, and
    /// <c>//</c> between words cutting the line. See <see cref="LevelExpression.Compile"/>.
    /// </summary>
    Rewritten,
}

/// <summary>Script text a level task carries in one of its slots, compiled once and evaluated as often as the task asks.</summary>
/// <remarks>
/// <para>
/// Both games evaluate a slot in the float walk (Project I.G.I.'s <c>sub_4F16E0</c> into <c>Expr_EvalFloat</c>; I.G.I. 2's
/// <c>Level_RunLevelExpression</c>, <c>0x545790</c>, into <c>Expression_EvalFloat</c>), and callers truncate the result where
/// they want a flag. What differs between them is the compile, which is the <see cref="LevelExpressionDialect"/>.
/// </para>
/// <para>
/// An expression that is empty, or that does not parse, is never run (<see cref="IsEmpty"/>).
/// </para>
/// </remarks>
public sealed class LevelExpression
{
    private readonly ScriptExpression? _tree;

    /// <summary>Initializes a new instance of the <see cref="LevelExpression"/> class.</summary>
    /// <param name="source">The slot's text, or <see langword="null"/> for none.</param>
    /// <param name="owner">The owning task's unique name, which <c>this.</c> stands for; <see langword="null"/> for none.</param>
    /// <param name="constants">The integer constants the parse folds.</param>
    /// <param name="dialect">How the title's engine compiles the text.</param>
    public LevelExpression(string? source, string? owner, Func<string, int?> constants, LevelExpressionDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(constants);
        Source = source ?? string.Empty;
        Owner = owner;
        Dialect = dialect;
        Compiled = dialect == LevelExpressionDialect.Rewritten ? Compile(Source, owner) : Source;
        if (Compiled.Length == 0)
        {
            return;
        }

        try
        {
            _tree = ScriptExpressionParser.Parse(Compiled, name => constants(name));
        }
        catch (ScriptExpressionException)
        {
            _tree = null;
        }
    }

    /// <summary>Gets the slot's text.</summary>
    public string Source { get; }

    /// <summary>Gets the owning task's name, which <c>this.</c> stands for.</summary>
    public string? Owner { get; }

    /// <summary>Gets how the text was compiled.</summary>
    public LevelExpressionDialect Dialect { get; }

    /// <summary>Gets the text that was parsed.</summary>
    public string Compiled { get; }

    /// <summary>Gets a value indicating whether there is nothing to run: no text, nothing left of it, or no expression parsed.</summary>
    public bool IsEmpty => _tree is null;

    /// <summary>Evaluates the expression in the float walk.</summary>
    /// <param name="context">What its names resolve to.</param>
    /// <returns>Its value, or nought when it is empty.</returns>
    public double Evaluate(IScriptExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _tree is null ? 0.0 : new ScriptExpressionEvaluator(Scope(context)).EvaluateReal(_tree);
    }

    /// <summary>Evaluates the expression in the integer walk.</summary>
    /// <param name="context">What its names resolve to.</param>
    /// <returns>Its value, or nought when it is empty.</returns>
    public int EvaluateInt32(IScriptExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _tree is null ? 0 : new ScriptExpressionEvaluator(Scope(context)).Evaluate(_tree);
    }

    /// <summary>Rewrites script text as <c>Level_CompileLevelExpression</c> does.</summary>
    /// <param name="source">The text.</param>
    /// <param name="owner">The owning task's unique name; <see langword="null"/> leaves <c>this.</c> as written.</param>
    /// <returns>The rewritten text.</returns>
    public static string Compile(string source, string? owner)
    {
        ArgumentNullException.ThrowIfNull(source);

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
                word = owner is null ? word : LevelExpressionNames.ResolveThis(word, owner);
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

    // A text parsed as written still names this. and .isExists, and they are answered here, at each lookup. A rewritten
    // one has neither left in it.
    private IScriptExpressionContext Scope(IScriptExpressionContext context) =>
        Dialect == LevelExpressionDialect.AsWritten ? new AsWrittenScope(context, Owner) : context;


    private sealed class AsWrittenScope(IScriptExpressionContext inner, string? owner) : IScriptExpressionContext
    {
        public bool TryGetValue(string name, out QvmValue value)
        {
            string resolved = Resolve(name);
            if (LevelExpressionNames.IsExistsProbe(resolved))
            {
                value = QvmValue.FromInt32(1);
                return true;
            }

            return inner.TryGetValue(resolved, out value);
        }

        public QvmValueKind GetValueKind(string name)
        {
            string resolved = Resolve(name);
            return LevelExpressionNames.IsExistsProbe(resolved) ? QvmValueKind.Integer : inner.GetValueKind(resolved);
        }

        public bool TrySetValue(string name, QvmValue value) => inner.TrySetValue(Resolve(name), value);

        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result) =>
            inner.TryInvoke(name, arguments, out result);

        private string Resolve(string name) => owner is null ? name : LevelExpressionNames.ResolveThis(name, owner);
    }
}

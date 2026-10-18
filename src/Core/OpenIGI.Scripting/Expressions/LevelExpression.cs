using System.Text;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Scripting.Expressions;

/// <summary>Whose compile a level expression goes through: the retail engine's, a word at a time, before the parse.</summary>
/// <remarks>
/// Both engines drop whitespace between words, give <c>this.</c> the owner's name and make any word holding
/// <c>.isExists</c> a <c>1</c>. They part on three things, listed on each value.
/// </remarks>
public enum LevelExpressionDialect
{
    /// <summary>
    /// Project I.G.I.'s compile (<c>0x4F1400</c>). A word whose trailing id names no task in the
    /// <see cref="ILevelTaskTable"/> becomes <c>0</c> (so <c>.isExists</c> asks a real question); a quoted string loses
    /// its quotes and has its escapes read (<c>sub_517260</c>); <c>//</c> means nothing.
    /// </summary>
    Igi1,

    /// <summary>
    /// I.G.I. 2's <c>Level_CompileLevelExpression</c> (<c>igi2 0x545440</c>). A word naming a missing task is kept as
    /// written; a quoted string keeps its quotes and escapes (<c>String_GetWordEx</c> with its quote flag clear); <c>//</c>
    /// between words cuts the line.
    /// </summary>
    Igi2,
}

/// <summary>Script text a level task carries in one of its slots, compiled once and evaluated as often as the task asks.</summary>
/// <remarks>
/// <para>
/// Both games evaluate a slot in the float walk (Project I.G.I.'s <c>sub_4F16E0</c> into <c>Expr_EvalFloat</c>; I.G.I. 2's
/// <c>Level_RunLevelExpression</c>, <c>0x545790</c>, into <c>Expression_EvalFloat</c>), and callers truncate the result where
/// they want a flag. What differs between them is the compile, which is the <see cref="LevelExpressionDialect"/>.
/// </para>
/// <para>
/// Given a task table, the compile keeps a reference to each task its words name (up to 16, the slots the retail structure
/// holds), and before each evaluation checks them: once any has died the text is compiled again (<c>sub_4F16E0</c> and
/// <c>sub_4F1740</c>; I.G.I. 2's <c>Level_RunLevelExpression</c> and <c>Level_ValidateLevelExpression</c>). In Project
/// I.G.I. that turns the dead task's words into <c>0</c>.
/// </para>
/// <para>
/// An expression that is empty, or that does not parse, is never run (<see cref="IsEmpty"/>).
/// </para>
/// </remarks>
public sealed class LevelExpression
{
    private const int MaxReferences = 16;

    private readonly Func<string, int?> _constants;
    private readonly ILevelTaskTable? _tasks;
    private readonly List<object> _references = [];
    private ScriptExpression? _tree;

    /// <summary>Initializes a new instance of the <see cref="LevelExpression"/> class.</summary>
    /// <param name="source">The slot's text, or <see langword="null"/> for none.</param>
    /// <param name="owner">The owning task's unique name, which <c>this.</c> stands for; <see langword="null"/> for none.</param>
    /// <param name="constants">The integer constants the parse folds.</param>
    /// <param name="dialect">Whose compile the text goes through.</param>
    /// <param name="tasks">The level's tasks; <see langword="null"/> takes every task a word names as present.</param>
    public LevelExpression(
        string? source,
        string? owner,
        Func<string, int?> constants,
        LevelExpressionDialect dialect,
        ILevelTaskTable? tasks = null)
    {
        ArgumentNullException.ThrowIfNull(constants);
        Source = source ?? string.Empty;
        Owner = owner;
        Dialect = dialect;
        _constants = constants;
        _tasks = tasks;
        Compiled = string.Empty;
        Build();
    }

    /// <summary>Gets the slot's text.</summary>
    public string Source { get; }

    /// <summary>Gets the owning task's name, which <c>this.</c> stands for.</summary>
    public string? Owner { get; }

    /// <summary>Gets whose compile the text went through.</summary>
    public LevelExpressionDialect Dialect { get; }

    /// <summary>Gets the text that was parsed, as of the last compile.</summary>
    public string Compiled { get; private set; }

    /// <summary>Gets a value indicating whether there is nothing to run: no text, nothing left of it, or no expression parsed.</summary>
    public bool IsEmpty
    {
        get
        {
            Validate();
            return _tree is null;
        }
    }

    /// <summary>Evaluates the expression in the float walk.</summary>
    /// <param name="context">What its names resolve to.</param>
    /// <returns>Its value, or nought when it is empty.</returns>
    public double Evaluate(IScriptExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Validate();
        return _tree is null ? 0.0 : new ScriptExpressionEvaluator(context).EvaluateReal(_tree);
    }

    /// <summary>Evaluates the expression in the integer walk.</summary>
    /// <param name="context">What its names resolve to.</param>
    /// <returns>Its value, or nought when it is empty.</returns>
    public int EvaluateInt32(IScriptExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Validate();
        return _tree is null ? 0 : new ScriptExpressionEvaluator(context).Evaluate(_tree);
    }

    /// <summary>Rewrites script text as I.G.I. 2's <c>Level_CompileLevelExpression</c> does.</summary>
    /// <param name="source">The text.</param>
    /// <param name="owner">The owning task's unique name; <see langword="null"/> leaves <c>this.</c> as written.</param>
    /// <returns>The rewritten text.</returns>
    public static string Compile(string source, string? owner) => Compile(source, owner, LevelExpressionDialect.Igi2);

    /// <summary>Rewrites script text as the dialect's engine does.</summary>
    /// <param name="source">The text.</param>
    /// <param name="owner">The owning task's unique name; <see langword="null"/> leaves <c>this.</c> as written.</param>
    /// <param name="dialect">Whose compile.</param>
    /// <param name="tasks">The level's tasks; <see langword="null"/> takes every task a word names as present.</param>
    /// <returns>The rewritten text.</returns>
    public static string Compile(string source, string? owner, LevelExpressionDialect dialect, ILevelTaskTable? tasks = null) =>
        Compile(source, owner, dialect, tasks, references: null);

    private static string Compile(
        string source,
        string? owner,
        LevelExpressionDialect dialect,
        ILevelTaskTable? tasks,
        List<object>? references)
    {
        ArgumentNullException.ThrowIfNull(source);

        bool igi1 = dialect == LevelExpressionDialect.Igi1;
        StringBuilder target = new(source.Length);
        int at = 0;
        while (at < source.Length)
        {
            SkipWhitespace(source, ref at);
            if (at >= source.Length)
            {
                break;
            }

            if (TryWord(source, ref at, unquote: igi1, out string word))
            {
                // The task a word names, by the id at its end; a word starting with a digit is a number.
                int id = TargetId(word);
                if (tasks is not null && id >= 0 && !char.IsAsciiDigit(word[0]))
                {
                    object? task = tasks.FindTask(id);
                    if (task is null)
                    {
                        if (igi1)
                        {
                            word = "0";
                        }
                    }
                    else if (references is not null && references.Count < MaxReferences && !references.Contains(task))
                    {
                        references.Add(task);
                    }
                }

                word = owner is null ? word : LevelExpressionNames.ResolveThis(word, owner);
                target.Append(LevelExpressionNames.IsExistsProbe(word) ? "1" : word);
                continue;
            }

            if (!igi1 && source[at] == '/' && at + 1 < source.Length && source[at + 1] == '/')
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

    // The word reader: a quoted string, a number (hexadecimal after 0x, else digits with points and one exponent), or a run
    // of letters, digits, '_' and '.', after an optional '$'. I.G.I. 2's String_GetWordEx with its quote flag clear keeps a
    // string's quotes and escapes; Project I.G.I.'s sub_517260 has no such flag and always unwraps it.
    private static bool TryWord(string text, ref int at, bool unquote, out string word)
    {
        int start = at;
        char first = text[at];
        if (first == '"' && unquote)
        {
            return TryUnquote(text, ref at, out word);
        }

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

    // sub_517260's string: the text between the quotes, with \b \n \r \t and \xHH read and any other escaped character
    // taken as itself. One with no closing quote is no word.
    private static bool TryUnquote(string text, ref int at, out string word)
    {
        StringBuilder value = new();
        int i = at + 1;
        while (i < text.Length && text[i] != '"')
        {
            char c = text[i++];
            if (c == '\\' && i < text.Length)
            {
                c = text[i++];
                switch (c)
                {
                    case 'b':
                        c = '\b';
                        break;
                    case 'n':
                        c = '\n';
                        break;
                    case 'r':
                        c = '\r';
                        break;
                    case 't':
                        c = '\t';
                        break;
                    case 'x' or 'X' when i + 1 < text.Length:
                        c = (char)(byte)((HexNibble(text[i]) << 4) | HexNibble(text[i + 1]));
                        i += 2;
                        break;
                }
            }

            value.Append(c);
        }

        if (i >= text.Length)
        {
            word = string.Empty;
            return false;
        }

        at = i + 1;
        word = value.ToString();
        return true;
    }

    // sub_517260's hexadecimal digit: up to '9' its low bits, above '9' its low bits plus nine.
    private static int HexNibble(char c) => c > '9' ? (c & 0xF) + 9 : c & 0xF;

    // sub_4F17D0 (I.G.I. 2's Level_GetTargetQTaskIDFromSymbol): the digits just before a word's first '.', or before its
    // end when it has none, read as an id; -1 when there are none. The sum wraps as the original's does.
    private static int TargetId(string word)
    {
        int end = word.IndexOf('.');
        if (end == 0)
        {
            return -1;
        }

        if (end < 0)
        {
            end = word.Length;
        }

        int value = 0;
        int scale = 1;
        bool any = false;
        for (int i = end - 1; i >= 0 && char.IsAsciiDigit(word[i]); i--)
        {
            any = true;
            value = unchecked(value + (scale * (word[i] - '0')));
            scale = unchecked(scale * 10);
        }

        return any ? value : -1;
    }

    // A reference whose task has died since the compile sends the text through it again.
    private void Validate()
    {
        foreach (object task in _references)
        {
            if (!_tasks!.IsLive(task))
            {
                Build();
                return;
            }
        }
    }

    private void Build()
    {
        _references.Clear();
        Compiled = Compile(Source, Owner, Dialect, _tasks, _references);
        _tree = null;
        if (Compiled.Length == 0)
        {
            return;
        }

        try
        {
            _tree = ScriptExpressionParser.Parse(Compiled, name => _constants(name));
        }
        catch (ScriptExpressionException)
        {
            _tree = null;
        }
    }
}

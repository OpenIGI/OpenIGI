using System.Text;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Scripting;

/// <summary>Script text a task carries in a <c>VarString</c> parameter, evaluated as the original does: one expression.</summary>
/// <remarks>
/// <para>
/// A soldier's or the player's <c>Weapon</c> parameter and the player tuning's two <c>Define ... script</c> parameters are
/// source text, not compiled code. <c>Script_ParseLineInt32</c> (<c>igi2 0x40D950</c>) reads the text through
/// <c>SingleLine_GetInput</c> (<c>0x55E830</c>), parses <b>one</b> expression from it (<c>Expression_Parse</c>,
/// <c>0x558690</c>) and evaluates that as an integer. The parser stops at the first <c>;</c>, so whatever follows it never
/// runs: <c>Human_AddWeapon("A"), Human_AddWeapon("B");</c> gives both weapons, while
/// <c>Human_AddWeapon("A"); Human_AddWeapon("B");</c> gives only the first, and several of mission 1's soldiers are
/// written the second way.
/// </para>
/// <para>
/// <c>SingleLine_GetInput</c> hands the parser a line at a time with everything from <c>//</c> to the line's end cut off,
/// unless the <c>//</c> is inside a quoted string; there is no block comment. <see cref="StripComments"/> is that pass, and
/// the expression language is then the shared one (<see cref="ScriptExpressionParser"/>), the same family as this
/// game's.
/// </para>
/// </remarks>
public static class Igi2LineScript
{
    /// <summary>Cuts each line of script text at its first <c>//</c> outside a quoted string.</summary>
    /// <param name="source">The text.</param>
    /// <returns>The text without comments, one line per line.</returns>
    /// <remarks><c>SingleLine_GetInput</c> (<c>igi2 0x55E830</c>): inside quotes a backslash skips the character after it.</remarks>
    public static string StripComments(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        StringBuilder text = new(source.Length);
        foreach (string line in source.Split('\n'))
        {
            string bare = line.TrimEnd('\r');
            text.Append(bare, 0, CommentStart(bare)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Evaluates script text as one integer expression, as <c>Script_ParseLineInt32</c> (<c>igi2 0x40D950</c>) does.</summary>
    /// <param name="source">The text.</param>
    /// <param name="natives">The script namespace: its constants and its symbols' contexts.</param>
    /// <param name="functions">The natives the text may call.</param>
    /// <returns>The expression's value, or zero when the text holds no expression or does not parse.</returns>
    public static int Evaluate(string? source, NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);

        if (string.IsNullOrEmpty(source))
        {
            return 0;
        }

        string text = StripComments(source);
        if (ScriptExpressionParser.IsBlank(text))
        {
            return 0;
        }

        ScriptExpression expression;
        try
        {
            expression = ScriptExpressionParser.Parse(text, name => Constant(natives, name));
        }
        catch (ScriptExpressionException)
        {
            return 0;
        }

        return new ScriptExpressionEvaluator(new Igi2ScriptContext(natives, functions)).EvaluateInt32(expression);
    }

    /// <summary>Evaluates script text as one string expression, as <c>Script_ParseLine</c> and <c>Expression_EvalString</c> do.</summary>
    /// <param name="source">The text.</param>
    /// <param name="natives">The script namespace.</param>
    /// <param name="functions">The natives the text may call.</param>
    /// <returns>The expression's text, or empty when the text holds no expression or does not parse.</returns>
    public static string EvaluateText(string? source, NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        string text = source is null ? string.Empty : StripComments(source);
        if (ScriptExpressionParser.IsBlank(text))
        {
            return string.Empty;
        }

        try
        {
            ScriptExpression expression = ScriptExpressionParser.Parse(text, name => Constant(natives, name));
            return new ScriptExpressionEvaluator(new Igi2ScriptContext(natives, functions)).EvaluateText(expression);
        }
        catch (ScriptExpressionException)
        {
            return string.Empty;
        }
    }

    private static int CommentStart(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                for (i++; i < line.Length && line[i] != '"'; i++)
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        i++;
                    }
                }

                continue;
            }

            if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                return i;
            }
        }

        return line.Length;
    }

    /// <summary>Resolves a name to an integer constant of the namespace, as the parser folds them.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="name">The name.</param>
    /// <returns>Its value, or <see langword="null"/> when it is no integer constant.</returns>
    internal static int? Constant(NativeRegistry natives, string name) =>
        natives.TryResolve(name, out QvmSymbol? symbol) && symbol.Kind == QvmSymbolKind.Constant && symbol.ValueType == QvmValueKind.Integer
            ? symbol.Read().Integer
            : null;
}

/// <summary>What script text's names resolve to: the namespace's readable symbols and the registered natives.</summary>
/// <param name="natives">The namespace.</param>
/// <param name="functions">The natives the text may call.</param>
internal sealed class Igi2ScriptContext(NativeRegistry natives, Igi2ScriptFunctions functions) : IScriptExpressionContext
{
    /// <inheritdoc/>
    public bool TryGetValue(string name, out QvmValue value)
    {
        if (natives.TryResolve(name, out QvmSymbol? symbol) && symbol.IsReadable)
        {
            value = symbol.Read();
            return true;
        }

        value = QvmValue.Zero;
        return false;
    }

    /// <inheritdoc/>
    public QvmValueKind GetValueKind(string name) =>
        natives.TryResolve(name, out QvmSymbol? symbol) && symbol.IsReadable ? symbol.ValueType : QvmValueKind.None;

    /// <inheritdoc/>
    public bool TrySetValue(string name, QvmValue value) => false;

    /// <inheritdoc/>
    public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
    {
        if (!functions.TryGet(name, out Igi2ScriptFunction? function))
        {
            result = QvmValue.Zero;
            return false;
        }

        object? context = natives.TryResolve(name, out QvmSymbol? symbol) ? symbol.Context : null;
        result = function(new Igi2ScriptCall(arguments, context));
        return true;
    }
}

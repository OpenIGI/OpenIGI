using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>I.G.I. 2's level expressions: compiled by <c>Level_CompileLevelExpression</c>'s rewrite, folded against the namespace's constants.</summary>
internal static class Igi2LevelExpressions
{
    /// <summary>Compiles a task's script text the way I.G.I. 2 compiles it.</summary>
    /// <param name="source">The parameter's text, or <see langword="null"/> for none.</param>
    /// <param name="owner">The owning task's unique name, which <c>this.</c> stands for (<c>Level_GetUniqueQTaskNameFromTask</c>).</param>
    /// <param name="natives">The namespace, whose integer constants the parse folds.</param>
    /// <returns>The expression.</returns>
    public static LevelExpression Create(string? source, string owner, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(natives);
        return new LevelExpression(source, owner, name => Igi2LineScript.Constant(natives, name), LevelExpressionDialect.Rewritten);
    }

    /// <summary>Evaluates the expression in the float walk (<c>Level_RunLevelExpression</c>, <c>0x545790</c>).</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="natives">The namespace its names resolve in.</param>
    /// <param name="functions">The natives it may call.</param>
    /// <returns>Its value, or nought when it is empty.</returns>
    public static double Run(this LevelExpression expression, NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        return expression.Evaluate(new Igi2ScriptContext(natives, functions));
    }
}

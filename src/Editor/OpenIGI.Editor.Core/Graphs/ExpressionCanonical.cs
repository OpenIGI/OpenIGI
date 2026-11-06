using System.Globalization;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>Whether two level expressions mean the same to the game: the one comparison the graph's writes are held to.</summary>
/// <remarks>
/// Both texts go through the game's own preparation (<see cref="LevelExpression.Compile(string, string?, LevelExpressionDialect, ILevelTaskTable?)"/>)
/// and parser, and the trees are compared with brackets dropped, numbers by value, and each field read by the task id and
/// field it names: the game resolves <c>Door_12.isOpen</c> by the <c>12</c> alone. Text the game cannot parse is only the
/// same as itself.
/// </remarks>
public static class ExpressionCanonical
{
    /// <summary>The canonical form of an expression's text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="owner">The task it belongs to, as <c>Type_Id</c>, for <c>this.</c>.</param>
    /// <param name="dialect">Which game's rules apply.</param>
    /// <returns>Its canonical form, empty for an empty expression, or <see langword="null"/> when the game cannot parse it.</returns>
    public static string? Of(string text, string owner, LevelExpressionDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(text);
        string compiled = LevelExpression.Compile(text, owner, dialect);
        if (ScriptExpressionParser.IsBlank(compiled))
        {
            return string.Empty;
        }

        try
        {
            return Of(ScriptExpressionParser.Parse(compiled));
        }
        catch (ScriptExpressionException)
        {
            return null;
        }
    }

    /// <summary>Whether two texts mean the same to the game.</summary>
    /// <param name="a">One text.</param>
    /// <param name="b">The other.</param>
    /// <param name="owner">The task they belong to, as <c>Type_Id</c>.</param>
    /// <param name="dialect">Which game's rules apply.</param>
    /// <returns><see langword="true"/> when the game reads them the same.</returns>
    public static bool Same(string a, string b, string owner, LevelExpressionDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        string? left = Of(a, owner, dialect);
        return left is not null && left == Of(b, owner, dialect);
    }

    /// <summary>The canonical form of a parsed expression.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>Its shape.</returns>
    public static string Of(ScriptExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression.Kind switch
        {
            ScriptExpressionKind.Group when expression.Left is { } inner => Of(inner),
            ScriptExpressionKind.Number => expression.Number.ToString("R", CultureInfo.InvariantCulture),
            ScriptExpressionKind.String => "s:" + expression.Text,
            ScriptExpressionKind.Identifier when expression.IsCall =>
                $"{expression.Text}({string.Join(',', expression.Arguments.Select(Of))})",
            ScriptExpressionKind.Identifier when ExpressionGraphReader.TryParseFieldRead(expression.Text, out int id, out string field) =>
                string.Create(CultureInfo.InvariantCulture, $"#{id}.{field}"),
            ScriptExpressionKind.Operator when ScriptOperatorInfo.IsUnary(expression.Operator) =>
                $"({ScriptOperatorInfo.GetSymbol(expression.Operator)} {Of(expression.Right!)})",
            ScriptExpressionKind.Operator =>
                $"({ScriptOperatorInfo.GetSymbol(expression.Operator)} {Of(expression.Left!)} {Of(expression.Right!)})",
            ScriptExpressionKind.Conditional => $"(? {Of(expression.Left!)} {Of(expression.Right!)} {Of(expression.Extra!)})",
            _ => expression.ToString(),
        };
    }
}

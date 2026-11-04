using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Scripting.Vocabulary;

/// <summary>The same checks over a level expression: a condition or run-at-start string as the game's own parser reads it.</summary>
public static partial class ScriptCallChecker
{
    /// <summary>Checks every call a level expression makes.</summary>
    /// <param name="expression">The expression, as <see cref="ScriptExpressionParser"/> parses it.</param>
    /// <param name="vocabulary">The vocabulary its natives come from.</param>
    /// <returns>The problems, in source order.</returns>
    public static IReadOnlyList<ScriptCallProblem> Check(ScriptExpression expression, ScriptVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(vocabulary);
        List<ScriptCallProblem> problems = [];
        Visit(expression, vocabulary, problems);
        return problems;
    }

    /// <summary>The type a level expression has, as far as the vocabulary can tell.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="vocabulary">The vocabulary.</param>
    /// <returns>Its type, or <see cref="ScriptType.Any"/> when it cannot be told.</returns>
    public static ScriptType TypeOf(ScriptExpression expression, ScriptVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(vocabulary);
        return expression switch
        {
            { Kind: ScriptExpressionKind.String } => ScriptType.Text,
            { Kind: ScriptExpressionKind.Number } => ScriptType.Real,
            { Kind: ScriptExpressionKind.Constant } => ScriptType.Int,
            { Kind: ScriptExpressionKind.Group, Left: { } inner } => TypeOf(inner, vocabulary),
            { Kind: ScriptExpressionKind.Operator or ScriptExpressionKind.Conditional } => ScriptType.Real,
            { IsCall: true } when vocabulary.TryGetNative(expression.Text, out NativeSignature? native) => native.Returns ?? ScriptType.Any,
            { Kind: ScriptExpressionKind.Identifier } when vocabulary.TryGetConstant(expression.Text, out string? family, out _) =>
                ScriptType.EnumOf(family),
            _ => ScriptType.Any,
        };
    }

    private static void Visit(ScriptExpression expression, ScriptVocabulary vocabulary, List<ScriptCallProblem> problems)
    {
        foreach (ScriptExpression child in new[] { expression.Left, expression.Right, expression.Extra }.OfType<ScriptExpression>())
        {
            Visit(child, vocabulary, problems);
        }

        foreach (ScriptExpression argument in expression.Arguments)
        {
            Visit(argument, vocabulary, problems);
        }

        if (expression.IsCall && vocabulary.TryGetNative(expression.Text, out NativeSignature? native))
        {
            CheckArguments(native, [.. expression.Arguments.Select(a => TypeOf(a, vocabulary))], problems);
        }
    }
}

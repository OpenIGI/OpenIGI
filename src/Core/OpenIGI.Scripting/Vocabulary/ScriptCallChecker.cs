using System.Globalization;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Scripting.Vocabulary;

/// <summary>A call that does not fit its native's signature.</summary>
/// <param name="Native">The native called.</param>
/// <param name="Argument">The argument at fault, or -1 when the count is wrong.</param>
/// <param name="Message">What is wrong, in a sentence.</param>
public sealed record ScriptCallProblem(string Native, int Argument, string Message);

/// <summary>Checks every call a script makes against a vocabulary: argument counts, and values the game would misread.</summary>
/// <remarks>
/// A hard problem is one the game would act on wrongly: too many or too few arguments, text where a number is read or a
/// number where text is read, or a constant of one family where another is wanted. A bare number where a constant family is
/// wanted is legal (retail writes <c>AIAction_Idle(1)</c>) and is not a problem. Natives the vocabulary does not have are
/// skipped; finding those is a separate check.
/// </remarks>
public static partial class ScriptCallChecker
{
    /// <summary>Checks a whole script.</summary>
    /// <param name="script">The script.</param>
    /// <param name="vocabulary">The vocabulary its natives come from.</param>
    /// <returns>The problems, in source order.</returns>
    public static IReadOnlyList<ScriptCallProblem> Check(QscScript script, ScriptVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(vocabulary);
        List<ScriptCallProblem> problems = [];
        foreach (QscStatement statement in script.Statements)
        {
            Visit(statement, vocabulary, problems);
        }

        return problems;
    }

    /// <summary>Checks one expression and everything inside it.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="vocabulary">The vocabulary its natives come from.</param>
    /// <returns>The problems, in source order.</returns>
    public static IReadOnlyList<ScriptCallProblem> Check(QscExpression expression, ScriptVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(vocabulary);
        List<ScriptCallProblem> problems = [];
        Visit(expression, vocabulary, problems);
        return problems;
    }

    /// <summary>The type an expression has, as far as the vocabulary can tell.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="vocabulary">The vocabulary.</param>
    /// <returns>Its type, or <see cref="ScriptType.Any"/> when it cannot be told.</returns>
    public static ScriptType TypeOf(QscExpression expression, ScriptVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(vocabulary);
        return expression switch
        {
            QscStringLiteral => ScriptType.Text,
            QscIntLiteral => ScriptType.Int,
            QscRealLiteral => ScriptType.Real,
            QscUnary { Operator: QscUnaryOperator.Minus or QscUnaryOperator.Plus } sign => TypeOf(sign.Operand, vocabulary),
            QscUnary or QscBinary => ScriptType.Int,
            QscSymbol symbol when vocabulary.TryGetConstant(symbol.Name, out string? family, out _) => ScriptType.EnumOf(family),
            QscCall call when vocabulary.TryGetNative(call.Name, out NativeSignature? native) => native.Returns ?? ScriptType.Any,
            _ => ScriptType.Any,
        };
    }

    private static void Visit(QscStatement statement, ScriptVocabulary vocabulary, List<ScriptCallProblem> problems)
    {
        switch (statement)
        {
            case QscExpressionStatement expression:
                Visit(expression.Expression, vocabulary, problems);
                break;

            case QscIf branch:
                Visit(branch.Condition, vocabulary, problems);
                VisitAll(branch.Then, vocabulary, problems);
                VisitAll(branch.Else ?? [], vocabulary, problems);
                break;

            case QscWhile loop:
                Visit(loop.Condition, vocabulary, problems);
                VisitAll(loop.Body, vocabulary, problems);
                break;
        }
    }

    private static void VisitAll(IEnumerable<QscStatement> statements, ScriptVocabulary vocabulary, List<ScriptCallProblem> problems)
    {
        foreach (QscStatement statement in statements)
        {
            Visit(statement, vocabulary, problems);
        }
    }

    private static void Visit(QscExpression expression, ScriptVocabulary vocabulary, List<ScriptCallProblem> problems)
    {
        switch (expression)
        {
            case QscCall call:
                foreach (QscExpression argument in call.Arguments)
                {
                    Visit(argument, vocabulary, problems);
                }

                if (vocabulary.TryGetNative(call.Name, out NativeSignature? native))
                {
                    CheckCall(call, native, vocabulary, problems);
                }

                break;

            case QscUnary unary:
                Visit(unary.Operand, vocabulary, problems);
                break;

            case QscBinary binary:
                Visit(binary.Left, vocabulary, problems);
                Visit(binary.Right, vocabulary, problems);
                break;

            case QscAssignment assignment:
                Visit(assignment.Target, vocabulary, problems);
                Visit(assignment.Value, vocabulary, problems);
                break;
        }
    }

    private static void CheckCall(QscCall call, NativeSignature native, ScriptVocabulary vocabulary, List<ScriptCallProblem> problems) =>
        CheckArguments(native, [.. call.Arguments.Select(a => TypeOf(a, vocabulary))], problems);

    // The one rule both trees share: the count fits, and each argument's type fits its parameter.
    private static void CheckArguments(NativeSignature native, IReadOnlyList<ScriptType> arguments, List<ScriptCallProblem> problems)
    {
        if (!native.Accepts(arguments.Count))
        {
            problems.Add(new(native.Name, -1, string.Create(
                CultureInfo.InvariantCulture,
                $"{native.Name} takes {Expected(native)} argument(s), not {arguments.Count}.")));
            return;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            NativeParameter parameter = native.Parameters[Math.Min(i, native.Arity - 1)];
            ScriptType given = arguments[i];
            if (!Fits(given, parameter.Type))
            {
                problems.Add(new(native.Name, i, string.Create(
                    CultureInfo.InvariantCulture,
                    $"{native.Name} argument {i + 1} ('{parameter.Name}') wants {parameter.Type}, given {given}.")));
            }
        }
    }

    // "2", "1 to 3" or "2 or more", as the signature allows.
    private static string Expected(NativeSignature native) =>
        native.IsVariadic
            ? string.Create(CultureInfo.InvariantCulture, $"{native.MinimumArity} or more")
            : native.MinimumArity == native.Arity
                ? native.Arity.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{native.MinimumArity} to {native.Arity}");

    // A bare number is a legal member of any family; a constant of the wrong family, or text against number, is not.
    private static bool Fits(ScriptType given, ScriptType wanted) =>
        (given.Kind is ScriptTypeKind.Int or ScriptTypeKind.Real && wanted.IsNumber) || given.ConnectsTo(wanted);
}

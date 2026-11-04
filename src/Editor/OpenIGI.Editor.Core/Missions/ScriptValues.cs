using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>
/// Reads constant values out of script expressions and builds expressions the way the original compiler
/// would have written them.
/// </summary>
/// <remarks>
/// Level data writes numbers as literals, a negative one as a unary minus applied to a positive literal,
/// and a whole-number value in a real parameter sometimes as an integer (<c>0</c> rather than <c>0.0</c>).
/// Reading folds all of that to a number; writing produces the retail shape, so an edited file still looks
/// like one the original tools saved.
/// </remarks>
public static class ScriptValues
{
    /// <summary>Evaluates a constant numeric expression.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="false"/> when the expression is not a numeric constant.</returns>
    public static bool TryGetNumber(QscExpression? expression, out double value)
    {
        switch (expression)
        {
            case QscIntLiteral literal:
                value = literal.Value;
                return true;
            case QscRealLiteral literal:
                value = literal.Value;
                return true;
            case QscUnary { Operator: QscUnaryOperator.Minus } unary when TryGetNumber(unary.Operand, out double negated):
                value = -negated;
                return true;
            case QscUnary { Operator: QscUnaryOperator.Plus } unary:
                return TryGetNumber(unary.Operand, out value);
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>Evaluates a constant integer expression.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="value">Receives the value, truncated toward zero when the constant is a real.</param>
    /// <returns><see langword="false"/> when the expression is not a numeric constant.</returns>
    public static bool TryGetInteger(QscExpression? expression, out int value)
    {
        bool ok = TryGetNumber(expression, out double number);
        value = ok ? (int)number : 0;
        return ok;
    }

    /// <summary>Reads a string literal.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="value">Receives the text.</param>
    /// <returns><see langword="false"/> when the expression is not a string literal.</returns>
    public static bool TryGetString(QscExpression? expression, out string value)
    {
        value = (expression as QscStringLiteral)?.Value ?? string.Empty;
        return expression is QscStringLiteral;
    }

    /// <summary>Builds an integer constant in the retail shape.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A literal, or a unary minus over one for a negative value.</returns>
    public static QscExpression Integer(int value) => value < 0 && value != int.MinValue
        ? new QscUnary(QscUnaryOperator.Minus, new QscIntLiteral(-value))
        : new QscIntLiteral(value);

    /// <summary>Builds a real constant in the retail shape.</summary>
    /// <param name="value">The value, narrowed to 32 bits as the VM stores it.</param>
    /// <returns>A literal, or a unary minus over one for a negative value.</returns>
    public static QscExpression Real(double value)
    {
        float narrowed = (float)value;
        return narrowed < 0
            ? new QscUnary(QscUnaryOperator.Minus, new QscRealLiteral(-narrowed))
            : new QscRealLiteral(narrowed);
    }

    /// <summary>The symbols level data writes for a true and a false <c>bool8</c>.</summary>
    public const string TrueSymbol = "TRUE";

    /// <summary>The symbol for false.</summary>
    public const string FalseSymbol = "FALSE";

    /// <summary>Evaluates a constant truth value: <c>TRUE</c>, <c>FALSE</c>, or a number (non-zero is true).</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="false"/> when the expression is not a truth constant.</returns>
    public static bool TryGetBool(QscExpression? expression, out bool value)
    {
        switch (expression)
        {
            case QscSymbol { Name: TrueSymbol }:
                value = true;
                return true;
            case QscSymbol { Name: FalseSymbol }:
                value = false;
                return true;
            default:
                bool ok = TryGetNumber(expression, out double number);
                value = ok && number != 0.0;
                return ok;
        }
    }

    /// <summary>Builds a truth constant the way level data writes one.</summary>
    /// <param name="value">The value.</param>
    /// <returns><c>TRUE</c> or <c>FALSE</c>.</returns>
    public static QscExpression Bool(bool value) => new QscSymbol(value ? TrueSymbol : FalseSymbol);

    /// <summary>Replaces an integer constant only if its value changed.</summary>
    /// <param name="current">The expression now in place.</param>
    /// <param name="value">The value wanted, rounded to the nearest integer.</param>
    /// <returns><paramref name="current"/> when it already evaluates to the value, else a new integer constant.</returns>
    public static QscExpression KeepOrInteger(QscExpression current, double value)
    {
        int rounded = (int)Math.Round(value);
        return TryGetNumber(current, out double existing) && existing == rounded ? current : Integer(rounded);
    }

    /// <summary>
    /// Replaces a numeric constant only if its value changed, so an untouched field keeps its exact spelling
    /// (an integer <c>0</c> stays an integer).
    /// </summary>
    /// <param name="current">The expression now in place.</param>
    /// <param name="value">The value wanted.</param>
    /// <returns><paramref name="current"/> when it already evaluates to the value, else a new real constant.</returns>
    public static QscExpression KeepOrReal(QscExpression current, double value) =>
        TryGetNumber(current, out double existing) && (float)existing == (float)value ? current : Real(value);
}

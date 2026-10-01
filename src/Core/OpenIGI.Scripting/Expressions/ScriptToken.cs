namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// One token from the expression tokenizer.
/// </summary>
/// <param name="Kind">What class of token this is.</param>
/// <param name="Operator">The operator, when <see cref="Kind"/> is an operator, ternary or terminator class.</param>
/// <param name="Text">The identifier name or string-literal contents.</param>
/// <param name="Number">The value of a numeric literal.</param>
/// <param name="Constant">The value of an identifier that resolved to a registered constant.</param>
/// <param name="Line">The one-based source line the token started on.</param>
public readonly record struct ScriptToken(
    ScriptTokenKind Kind,
    ScriptOperator Operator,
    string Text,
    double Number,
    int Constant,
    int Line)
{
    /// <summary>Gets the end-of-input token.</summary>
    public static ScriptToken End => new(ScriptTokenKind.End, ScriptOperator.None, string.Empty, 0.0, 0, 0);

    /// <summary>Gets a value indicating whether this token can begin an operand.</summary>
    public bool IsOperand =>
        Kind is ScriptTokenKind.String or ScriptTokenKind.Identifier
            or ScriptTokenKind.Number or ScriptTokenKind.Constant;

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        ScriptTokenKind.Number => Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ScriptTokenKind.String => $"\"{Text}\"",
        ScriptTokenKind.Identifier => Text,
        ScriptTokenKind.Constant => $"{Text}={Constant}",
        ScriptTokenKind.End => "<end>",
        _ => Operator.ToString(),
    };
}

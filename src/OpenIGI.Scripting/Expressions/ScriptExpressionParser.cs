namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// Parses the runtime expression language the original compiles menu actions and conditions from.
/// </summary>
/// <remarks>
/// <para>
/// Behaviourally equivalent to <c>Expr_Parse</c> (<c>0x4B9CD0</c>) and its argument-list helper
/// <c>Expr_ParseArguments</c> (<c>0x4BA4C0</c>). The original climbs precedence by re-parenting nodes
/// through a parent chain as it goes; this parses by recursion instead. That is a deliberate
/// divergence in <em>method</em>, not in result: with the same precedence and associativity rules —
/// <see cref="ScriptOperatorInfo"/>, transcribed verbatim — both produce the same tree, and the round
/// trip is asserted in the tests.
/// </para>
/// <para>
/// What is <em>not</em> a divergence, and what the transcription had to get right:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A <c>+</c> or <c>-</c> in prefix position is rewritten to <see cref="ScriptOperator.UnaryPlus"/> or
/// <see cref="ScriptOperator.UnaryMinus"/>, which bind far tighter than their binary forms.
/// </description></item>
/// <item><description>
/// Adjacent string literals concatenate, as they do in C. The original does it in the node
/// constructor by replacing the previous node in the tree.
/// </description></item>
/// <item><description>
/// Inside an argument list, <c>,</c> stops the expression instead of acting as the sequencing
/// operator it is everywhere else. The original passes a flag that reclassifies the token.
/// </description></item>
/// <item><description>
/// <c>)</c>, <c>;</c>, <c>{</c> and <c>}</c> end an expression rather than being consumed by it.
/// </description></item>
/// </list>
/// </remarks>
public sealed class ScriptExpressionParser
{
    /// <summary>The precedence rank of the loosest-binding operator, used as the starting limit.</summary>
    private const int LoosestPrecedence = 16;

    private readonly ScriptLexer _lexer;

    /// <summary>Initializes a new instance of the <see cref="ScriptExpressionParser"/> class.</summary>
    /// <param name="lexer">The token source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lexer"/> is <see langword="null"/>.</exception>
    public ScriptExpressionParser(ScriptLexer lexer)
    {
        ArgumentNullException.ThrowIfNull(lexer);
        _lexer = lexer;
    }

    /// <summary>Parses a complete expression from source.</summary>
    /// <param name="source">The expression source.</param>
    /// <param name="resolveConstant">Resolves an identifier to a registered constant, or <see langword="null"/> when it is not one.</param>
    /// <returns>The parsed tree.</returns>
    /// <exception cref="ScriptExpressionException">The source is not a well-formed expression.</exception>
    public static ScriptExpression Parse(string source, Func<string, int?>? resolveConstant = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        ScriptExpressionParser parser = new(new ScriptLexer(source, resolveConstant));
        ScriptExpression expression = parser.ParseExpression(LoosestPrecedence, stopAtComma: false);

        // A terminator after a complete expression ends it and is not an error. This is not
        // permissiveness for its own sake: retail mainmenu.qvm ships
        // "(Config_GetNumberOfPlayerProfiles() > 1) ? MenuManager_ActivatePopupScreen(930) : TRUE)"
        // with one ')' too many, and the original's parser simply stops there — a stray terminator
        // sets its "expression finished" flag rather than raising a diagnostic. Rejecting it would
        // make a shipped menu entry unusable.
        ScriptToken trailing = parser._lexer.Next();
        if (trailing.Kind is not (ScriptTokenKind.End or ScriptTokenKind.Terminator))
        {
            throw new ScriptExpressionException("Syntax error", trailing.Line, trailing.Text);
        }

        return expression;
    }

    /// <summary>Parses one expression, binding operators no looser than <paramref name="limit"/>.</summary>
    /// <param name="limit">The loosest precedence rank this call may absorb.</param>
    /// <param name="stopAtComma">Whether a comma ends the expression instead of sequencing it.</param>
    /// <returns>The parsed tree.</returns>
    private ScriptExpression ParseExpression(int limit, bool stopAtComma)
    {
        ScriptExpression left = ParseOperand();

        while (true)
        {
            ScriptToken token = _lexer.Next();

            if (token.Kind is ScriptTokenKind.End or ScriptTokenKind.Terminator)
            {
                _lexer.PushBack(token);
                return left;
            }

            if (token.Kind == ScriptTokenKind.Ternary)
            {
                if (token.Operator == ScriptOperator.Colon)
                {
                    // The ternary's own parse consumes its colon; anything else meeting one is done.
                    _lexer.PushBack(token);
                    return left;
                }

                int ternaryRank = ScriptOperatorInfo.GetPrecedence(ScriptOperator.Question);
                if (ternaryRank > limit)
                {
                    _lexer.PushBack(token);
                    return left;
                }

                left = ParseConditional(left, token.Line);
                continue;
            }

            if (token.Kind is not (ScriptTokenKind.BinaryOperator or ScriptTokenKind.UnaryOperator))
            {
                throw new ScriptExpressionException("Syntax error", token.Line, token.Text);
            }

            if (stopAtComma && token.Operator == ScriptOperator.Comma)
            {
                _lexer.PushBack(token);
                return left;
            }

            int rank = ScriptOperatorInfo.GetPrecedence(token.Operator);
            if (rank > limit)
            {
                _lexer.PushBack(token);
                return left;
            }

            if (token.Operator == ScriptOperator.Assign && !left.IsAssignable)
            {
                throw new ScriptExpressionException(
                    "Syntax error, binary operator requires lvalue",
                    token.Line,
                    token.Text);
            }

            // A left-associative operator may not absorb another of equal rank; a right-associative
            // one may. The original spells this as a 0/1 added before a <= comparison.
            int rightLimit = ScriptOperatorInfo.IsLeftAssociative(token.Operator) ? rank - 1 : rank;
            ScriptExpression right = ParseExpression(rightLimit, stopAtComma);
            left = ScriptExpression.FromBinary(token.Operator, left, right, token.Line);
        }
    }

    private ScriptExpression ParseConditional(ScriptExpression condition, int line)
    {
        int rank = ScriptOperatorInfo.GetPrecedence(ScriptOperator.Question);

        // Both branches are parsed at the ternary's own rank, which is what makes `a ? b : c ? d : e`
        // group to the right.
        ScriptExpression whenTrue = ParseExpression(rank, stopAtComma: true);

        ScriptToken colon = _lexer.Next();
        if (colon.Kind != ScriptTokenKind.Ternary || colon.Operator != ScriptOperator.Colon)
        {
            throw new ScriptExpressionException("Expected ':'", colon.Line, colon.Text);
        }

        ScriptExpression whenFalse = ParseExpression(rank, stopAtComma: true);
        return ScriptExpression.FromConditional(condition, whenTrue, whenFalse, line);
    }

    private ScriptExpression ParseOperand()
    {
        ScriptToken token = _lexer.Next();

        switch (token.Kind)
        {
            case ScriptTokenKind.Number:
                return ScriptExpression.FromNumber(token.Number, token.Line);

            case ScriptTokenKind.Constant:
                return ScriptExpression.FromConstant(token.Constant, token.Text, token.Line);

            case ScriptTokenKind.String:
                return ParseStringRun(token);

            case ScriptTokenKind.Identifier:
                return ParseIdentifier(token);

            case ScriptTokenKind.UnaryOperator:
                return ScriptExpression.FromUnary(
                    token.Operator,
                    ParseExpression(ScriptOperatorInfo.GetPrecedence(token.Operator), stopAtComma: true),
                    token.Line);

            case ScriptTokenKind.BinaryOperator when token.Operator == ScriptOperator.OpenParen:
                {
                    ScriptExpression inner = ParseExpression(LoosestPrecedence, stopAtComma: false);
                    Expect(ScriptOperator.CloseParen, "Expected ')'");
                    return ScriptExpression.FromGroup(inner, token.Line);
                }

            case ScriptTokenKind.BinaryOperator
                when token.Operator is ScriptOperator.Add or ScriptOperator.Subtract:
                {
                    // Prefix position: the parser rewrites the binary form to its unary counterpart,
                    // which binds at rank 2 instead of 4.
                    ScriptOperator unary = token.Operator == ScriptOperator.Add
                        ? ScriptOperator.UnaryPlus
                        : ScriptOperator.UnaryMinus;

                    return ScriptExpression.FromUnary(
                        unary,
                        ParseExpression(ScriptOperatorInfo.GetPrecedence(unary), stopAtComma: true),
                        token.Line);
                }

            case ScriptTokenKind.End:
                throw new ScriptExpressionException("Unexpected end of file", token.Line, token.Text);

            default:
                throw new ScriptExpressionException("Syntax error, expected operand", token.Line, token.Text);
        }
    }

    /// <summary>Consumes a run of adjacent string literals, which concatenate.</summary>
    private ScriptExpression ParseStringRun(ScriptToken first)
    {
        string text = first.Text;

        while (true)
        {
            ScriptToken next = _lexer.Next();
            if (next.Kind != ScriptTokenKind.String)
            {
                _lexer.PushBack(next);
                return ScriptExpression.FromString(text, first.Line);
            }

            text += next.Text;
        }
    }

    private ScriptExpression ParseIdentifier(ScriptToken token)
    {
        ScriptToken next = _lexer.Next();
        if (next.Kind != ScriptTokenKind.BinaryOperator || next.Operator != ScriptOperator.OpenParen)
        {
            _lexer.PushBack(next);
            return ScriptExpression.FromIdentifier(token.Text, token.Line);
        }

        return ScriptExpression.FromCall(token.Text, ParseArguments(), token.Line);
    }

    /// <summary>Parses a call's argument list, the opening parenthesis already consumed.</summary>
    /// <remarks>
    /// Mirrors <c>Expr_ParseArguments</c> (<c>0x4BA4C0</c>): each argument is a full expression parsed
    /// with the comma suppressed, arguments are separated by commas, and the list ends at <c>)</c>.
    /// The original also checks the count against the callee's declared arity and reports "Too few" or
    /// "Too many"; that check needs the native's descriptor and so belongs to binding, not parsing.
    /// </remarks>
    private ScriptExpression[] ParseArguments()
    {
        ScriptToken first = _lexer.Next();
        if (first.Kind == ScriptTokenKind.Terminator && first.Operator == ScriptOperator.CloseParen)
        {
            return [];
        }

        _lexer.PushBack(first);
        List<ScriptExpression> arguments = [];

        while (true)
        {
            arguments.Add(ParseExpression(LoosestPrecedence, stopAtComma: true));

            ScriptToken separator = _lexer.Next();
            if (separator.Kind == ScriptTokenKind.Terminator && separator.Operator == ScriptOperator.CloseParen)
            {
                return [.. arguments];
            }

            if (separator.Kind != ScriptTokenKind.BinaryOperator || separator.Operator != ScriptOperator.Comma)
            {
                throw new ScriptExpressionException("Expected ')'", separator.Line, separator.Text);
            }
        }
    }

    private void Expect(ScriptOperator op, string message)
    {
        ScriptToken token = _lexer.Next();
        if (token.Operator != op)
        {
            throw new ScriptExpressionException(message, token.Line, token.Text);
        }
    }
}

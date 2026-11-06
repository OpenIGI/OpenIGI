namespace OpenIGI.Scripting.Qsc;

/// <summary>
/// Parses QSC source into a syntax tree.
/// </summary>
/// <remarks>
/// <para>Grammar, with C's precedence and left associativity for every binary operator:</para>
/// <code>
/// script     := statement*
/// statement  := 'if' '(' expr ')' body ('else' body)?
///             | 'while' '(' expr ')' body
///             | expr ';'
/// body       := '{' statement* '}' | statement
/// expr       := or ('=' expr)?
/// or         := and ('||' and)*          ... down through | ^ &amp; == != &lt; &lt;= &gt; &gt;= &lt;&lt; &gt;&gt; + - * /
/// unary      := ('-' | '+' | '!' | '~') unary | primary
/// primary    := INTEGER | REAL | STRING | NAME | NAME '(' (expr (',' expr)*)? ')' | '(' expr ')'
/// </code>
/// <para>
/// A body written without braces is a block of one statement, so <c>else if</c> needs no rule of its own.
/// </para>
/// </remarks>
public sealed class QscParser
{
    private static readonly QscBinaryOperator[][] Levels =
    [
        [QscBinaryOperator.LogicalOr],
        [QscBinaryOperator.LogicalAnd],
        [QscBinaryOperator.BitwiseOr],
        [QscBinaryOperator.BitwiseXor],
        [QscBinaryOperator.BitwiseAnd],
        [QscBinaryOperator.Equal, QscBinaryOperator.NotEqual],
        [QscBinaryOperator.Less, QscBinaryOperator.LessOrEqual, QscBinaryOperator.Greater, QscBinaryOperator.GreaterOrEqual],
        [QscBinaryOperator.ShiftLeft, QscBinaryOperator.ShiftRight],
        [QscBinaryOperator.Add, QscBinaryOperator.Subtract],
        [QscBinaryOperator.Multiply, QscBinaryOperator.Divide],
    ];

    private readonly List<QscToken> _tokens;
    private int _index;

    private QscParser(string source) => _tokens = new QscLexer(source).ReadAll();

    /// <summary>Parses a whole script.</summary>
    /// <param name="source">The source text.</param>
    /// <param name="version">The bytecode version the script will compile to: 5 for I.G.I., 7 for I.G.I. 2.</param>
    /// <returns>The script.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="QscParseException">The source is not valid QSC.</exception>
    public static QscScript ParseScript(string source, int version)
    {
        QscParser parser = new(source);
        List<QscStatement> statements = [];
        while (parser.Current.Kind != QscTokenKind.End)
        {
            statements.Add(parser.Statement());
        }

        return new QscScript(statements, version);
    }

    /// <summary>Parses a single expression, such as a property value typed into an editor field.</summary>
    /// <param name="source">The source text.</param>
    /// <returns>The expression.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="QscParseException">The source is not exactly one valid expression.</exception>
    public static QscExpression ParseExpression(string source)
    {
        QscParser parser = new(source);
        QscExpression expression = parser.Expression();
        if (parser.Current.Kind != QscTokenKind.End)
        {
            throw new QscParseException(parser.Current.Position, $"unexpected '{parser.Current.Text}' after the expression");
        }

        return expression;
    }

    private QscToken Current => _tokens[_index];

    private QscStatement Statement()
    {
        if (IsKeyword("if"))
        {
            _index++;
            QscExpression condition = Condition();
            List<QscStatement> then = Body();
            List<QscStatement>? @else = null;
            if (IsKeyword("else"))
            {
                _index++;
                @else = Body();
            }

            return new QscIf(condition, then, @else);
        }

        if (IsKeyword("while"))
        {
            _index++;
            QscExpression condition = Condition();
            return new QscWhile(condition, Body());
        }

        QscExpression expression = Expression();
        Expect(";");
        return new QscExpressionStatement(expression);
    }

    private QscExpression Condition()
    {
        Expect("(");
        QscExpression condition = Expression();
        Expect(")");
        return condition;
    }

    private List<QscStatement> Body()
    {
        if (!IsPunctuation("{"))
        {
            return [Statement()];
        }

        _index++;
        List<QscStatement> statements = [];
        while (!IsPunctuation("}"))
        {
            if (Current.Kind == QscTokenKind.End)
            {
                throw new QscParseException(Current.Position, "a block is never closed with '}'");
            }

            statements.Add(Statement());
        }

        _index++;
        return statements;
    }

    private QscExpression Expression()
    {
        QscExpression left = Binary(0);
        if (!IsPunctuation("="))
        {
            return left;
        }

        _index++;
        return new QscAssignment(left, Expression());
    }

    private QscExpression Binary(int level)
    {
        if (level == Levels.Length)
        {
            return Unary();
        }

        QscExpression left = Binary(level + 1);
        while (MatchOperator(Levels[level]) is { } op)
        {
            left = new QscBinary(op, left, Binary(level + 1));
        }

        return left;
    }

    private QscExpression Unary()
    {
        if (Current.Kind == QscTokenKind.Punctuation)
        {
            QscUnaryOperator? op = Current.Text switch
            {
                "-" => QscUnaryOperator.Minus,
                "+" => QscUnaryOperator.Plus,
                "!" => QscUnaryOperator.LogicalNot,
                "~" => QscUnaryOperator.BitwiseNot,
                _ => null,
            };

            if (op is { } unary)
            {
                _index++;
                return new QscUnary(unary, Unary());
            }
        }

        return Primary();
    }

    private QscExpression Primary()
    {
        QscToken token = Current;
        switch (token.Kind)
        {
            case QscTokenKind.Integer:
                _index++;
                return new QscIntLiteral(token.Integer);
            case QscTokenKind.Real:
                _index++;
                return new QscRealLiteral(token.Real);
            case QscTokenKind.String:
                _index++;
                return new QscStringLiteral(token.Text);
            case QscTokenKind.Identifier when token.Text is "if" or "else" or "while":
                throw new QscParseException(token.Position, $"'{token.Text}' cannot start an expression");
            case QscTokenKind.Identifier:
                _index++;
                return IsPunctuation("(") ? CallArguments(token.Text) : new QscSymbol(token.Text);
            case QscTokenKind.Punctuation when token.Text == "(":
            {
                _index++;
                QscExpression inner = Expression();
                Expect(")");
                return inner;
            }

            case QscTokenKind.End:
                throw new QscParseException(token.Position, "the source ends where an expression was expected");
            default:
                throw new QscParseException(token.Position, $"'{token.Text}' cannot start an expression");
        }
    }

    private QscCall CallArguments(string name)
    {
        Expect("(");
        List<QscExpression> arguments = [];
        if (!IsPunctuation(")"))
        {
            arguments.Add(Expression());
            while (IsPunctuation(","))
            {
                _index++;
                arguments.Add(Expression());
            }
        }

        Expect(")");
        return new QscCall(name, arguments);
    }

    private QscBinaryOperator? MatchOperator(QscBinaryOperator[] candidates)
    {
        if (Current.Kind != QscTokenKind.Punctuation)
        {
            return null;
        }

        foreach (QscBinaryOperator op in candidates)
        {
            if (Current.Text == QscPrinter.Symbol(op))
            {
                _index++;
                return op;
            }
        }

        return null;
    }

    private void Expect(string punctuation)
    {
        if (!IsPunctuation(punctuation))
        {
            string found = Current.Kind == QscTokenKind.End ? "the end of the source" : $"'{Current.Text}'";
            throw new QscParseException(Current.Position, $"expected '{punctuation}' but found {found}");
        }

        _index++;
    }

    private bool IsPunctuation(string text) => Current.Kind == QscTokenKind.Punctuation && Current.Text == text;

    private bool IsKeyword(string text) => Current.Kind == QscTokenKind.Identifier && Current.Text == text;
}

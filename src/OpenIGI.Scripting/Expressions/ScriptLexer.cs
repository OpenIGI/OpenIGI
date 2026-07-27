using System.Globalization;
using System.Text;

namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// The expression tokenizer, with the one-token pushback the parser relies on.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>Expr_NextToken</c> (<c>0x4BFC70</c>). See
/// the original for the token classes and operator ids.
/// </para>
/// <para>
/// Two behaviours are worth naming because they are easy to get wrong:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The two-character operators are matched by peeking exactly one character ahead, and only for the
/// pairs the original checks. <c>&amp;</c> looks for <c>&amp;&amp;</c>, <c>|</c> for <c>||</c>,
/// <c>!</c> for <c>!=</c>, <c>&lt;</c> for <c>&lt;&lt;</c> and <c>&lt;=</c>, <c>&gt;</c> for
/// <c>&gt;&gt;</c> and <c>&gt;=</c>, <c>=</c> for <c>==</c>. There is no <c>+=</c> family.
/// </description></item>
/// <item><description>
/// An identifier is reclassified from <see cref="ScriptTokenKind.Identifier"/> to
/// <see cref="ScriptTokenKind.Constant"/> the moment it resolves against the constant table, so the
/// parser never sees a constant as a name.
/// </description></item>
/// </list>
/// </remarks>
public sealed class ScriptLexer
{
    private readonly string _source;
    private readonly Func<string, int?>? _resolveConstant;
    private ScriptToken? _pushedBack;
    private int _position;
    private int _line = 1;

    /// <summary>Initializes a new instance of the <see cref="ScriptLexer"/> class.</summary>
    /// <param name="source">The expression source.</param>
    /// <param name="resolveConstant">
    /// Resolves an identifier to a registered constant, or returns <see langword="null"/> when it is
    /// not one. Pass <see langword="null"/> to treat every identifier as a name.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public ScriptLexer(string source, Func<string, int?>? resolveConstant = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _resolveConstant = resolveConstant;
    }

    /// <summary>Gets the one-based line the cursor is on.</summary>
    public int Line => _line;

    /// <summary>Reads the next token.</summary>
    /// <returns>The token, or <see cref="ScriptToken.End"/> at end of input.</returns>
    /// <exception cref="ScriptExpressionException">A string literal is unterminated.</exception>
    public ScriptToken Next()
    {
        if (_pushedBack is { } pushed)
        {
            _pushedBack = null;
            return pushed;
        }

        SkipWhitespace();

        if (_position >= _source.Length)
        {
            return ScriptToken.End;
        }

        int line = _line;
        char c = _source[_position];

        switch (c)
        {
            case '!':
                return Peek(1) == '=' ? Punct(ScriptOperator.NotEqual, 2, line) : Punct(ScriptOperator.Not, 1, line);
            case '&':
                return Peek(1) == '&' ? Punct(ScriptOperator.LogicalAnd, 2, line) : Punct(ScriptOperator.BitwiseAnd, 1, line);
            case '|':
                return Peek(1) == '|' ? Punct(ScriptOperator.LogicalOr, 2, line) : Punct(ScriptOperator.BitwiseOr, 1, line);
            case '<':
                return Peek(1) switch
                {
                    '<' => Punct(ScriptOperator.ShiftLeft, 2, line),
                    '=' => Punct(ScriptOperator.LessOrEqual, 2, line),
                    _ => Punct(ScriptOperator.Less, 1, line),
                };
            case '>':
                return Peek(1) switch
                {
                    '>' => Punct(ScriptOperator.ShiftRight, 2, line),
                    '=' => Punct(ScriptOperator.GreaterOrEqual, 2, line),
                    _ => Punct(ScriptOperator.Greater, 1, line),
                };
            case '=':
                return Peek(1) == '=' ? Punct(ScriptOperator.Equal, 2, line) : Punct(ScriptOperator.Assign, 1, line);
            case '+':
                return Punct(ScriptOperator.Add, 1, line);
            case '-':
                return Punct(ScriptOperator.Subtract, 1, line);
            case '*':
                return Punct(ScriptOperator.Multiply, 1, line);
            case '/':
                return Punct(ScriptOperator.Divide, 1, line);
            case '^':
                return Punct(ScriptOperator.BitwiseXor, 1, line);
            case '~':
                return Punct(ScriptOperator.BitwiseNot, 1, line);
            case '(':
                return Punct(ScriptOperator.OpenParen, 1, line);
            case ')':
                return Punct(ScriptOperator.CloseParen, 1, line);
            case ';':
                return Punct(ScriptOperator.Semicolon, 1, line);
            case ',':
                return Punct(ScriptOperator.Comma, 1, line);
            case '?':
                return Punct(ScriptOperator.Question, 1, line);
            case ':':
                return Punct(ScriptOperator.Colon, 1, line);
            case '{':
                return Punct(ScriptOperator.OpenBrace, 1, line);
            case '}':
                return Punct(ScriptOperator.CloseBrace, 1, line);
            case '"':
                return ReadString(line);
            default:
                return ReadWord(line);
        }
    }

    /// <summary>Pushes a token back so the next <see cref="Next"/> returns it again.</summary>
    /// <param name="token">The token to push back.</param>
    /// <exception cref="InvalidOperationException">A token is already pushed back.</exception>
    /// <remarks>The original keeps exactly one slot for this, and so does the parser; a second push is a bug, not a deeper buffer.</remarks>
    public void PushBack(in ScriptToken token)
    {
        if (_pushedBack is not null)
        {
            throw new InvalidOperationException("Only one token can be pushed back at a time.");
        }

        _pushedBack = token;
    }

    /// <summary>Reads every remaining token, for tests and tooling.</summary>
    /// <returns>The tokens, without the terminating end token.</returns>
    public IReadOnlyList<ScriptToken> ReadAll()
    {
        List<ScriptToken> tokens = [];
        while (true)
        {
            ScriptToken token = Next();
            if (token.Kind == ScriptTokenKind.End)
            {
                return tokens;
            }

            tokens.Add(token);
        }
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '.';

    private char Peek(int offset) =>
        _position + offset < _source.Length ? _source[_position + offset] : '\0';

    private void SkipWhitespace()
    {
        while (_position < _source.Length)
        {
            char c = _source[_position];
            if (c is ' ' or '\t' or '\r')
            {
                _position++;
            }
            else if (c == '\n')
            {
                _position++;
                _line++;
            }
            else
            {
                return;
            }
        }
    }

    private ScriptToken Punct(ScriptOperator op, int length, int line)
    {
        string text = _source.Substring(_position, Math.Min(length, _source.Length - _position));
        _position += length;

        ScriptTokenKind kind = op switch
        {
            ScriptOperator.Not or ScriptOperator.BitwiseNot => ScriptTokenKind.UnaryOperator,
            ScriptOperator.Question or ScriptOperator.Colon => ScriptTokenKind.Ternary,
            ScriptOperator.CloseParen or ScriptOperator.Semicolon
                or ScriptOperator.OpenBrace or ScriptOperator.CloseBrace => ScriptTokenKind.Terminator,
            _ => ScriptTokenKind.BinaryOperator,
        };

        return new ScriptToken(kind, op, text, 0.0, 0, line);
    }

    private ScriptToken ReadString(int line)
    {
        _position++; // opening quote
        StringBuilder text = new();

        while (true)
        {
            if (_position >= _source.Length)
            {
                throw new ScriptExpressionException("Parse error (missing \" ?)", line, text.ToString());
            }

            char c = _source[_position++];
            if (c == '"')
            {
                break;
            }

            if (c == '\n')
            {
                _line++;
            }

            text.Append(c);
        }

        return new ScriptToken(ScriptTokenKind.String, ScriptOperator.None, text.ToString(), 0.0, 0, line);
    }

    private ScriptToken ReadWord(int line)
    {
        int start = _position;

        if (char.IsDigit(_source[_position]) || (_source[_position] == '.' && char.IsDigit(Peek(1))))
        {
            return ReadNumber(line);
        }

        if (!IsWordStart(_source[_position]))
        {
            // Not an operator, not a number, not a name. The original scans it as a word anyway and
            // the parser rejects it; consuming one character keeps that behaviour without looping.
            _position++;
            return new ScriptToken(
                ScriptTokenKind.Identifier,
                ScriptOperator.None,
                _source.Substring(start, 1),
                0.0,
                0,
                line);
        }

        while (_position < _source.Length && IsWordPart(_source[_position]))
        {
            _position++;
        }

        string name = _source[start.._position];

        // An identifier that names a constant is reclassified here, so the parser never sees it.
        if (_resolveConstant?.Invoke(name) is { } value)
        {
            return new ScriptToken(ScriptTokenKind.Constant, ScriptOperator.None, name, value, value, line);
        }

        return new ScriptToken(ScriptTokenKind.Identifier, ScriptOperator.None, name, 0.0, 0, line);
    }

    private ScriptToken ReadNumber(int line)
    {
        int start = _position;

        if (_source[_position] == '0' && (Peek(1) is 'x' or 'X'))
        {
            _position += 2;
            int digits = _position;
            while (_position < _source.Length && Uri.IsHexDigit(_source[_position]))
            {
                _position++;
            }

            string hex = _source[digits.._position];
            long parsed = hex.Length == 0 ? 0 : long.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new ScriptToken(ScriptTokenKind.Number, ScriptOperator.None, _source[start.._position], parsed, 0, line);
        }

        while (_position < _source.Length && (char.IsDigit(_source[_position]) || _source[_position] == '.'))
        {
            _position++;
        }

        // An exponent, so that a float written the C way still reads as one number.
        if (_position < _source.Length && (_source[_position] is 'e' or 'E'))
        {
            int save = _position;
            _position++;
            if (_position < _source.Length && (_source[_position] is '+' or '-'))
            {
                _position++;
            }

            if (_position < _source.Length && char.IsDigit(_source[_position]))
            {
                while (_position < _source.Length && char.IsDigit(_source[_position]))
                {
                    _position++;
                }
            }
            else
            {
                _position = save;
            }
        }

        string literal = _source[start.._position];
        double number = double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0.0;
        return new ScriptToken(ScriptTokenKind.Number, ScriptOperator.None, literal, number, 0, line);
    }
}

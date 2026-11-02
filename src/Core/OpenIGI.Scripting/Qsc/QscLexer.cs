using System.Globalization;
using System.Text;

namespace OpenIGI.Scripting.Qsc;

/// <summary>The kind of a QSC token.</summary>
public enum QscTokenKind
{
    /// <summary>The end of the source.</summary>
    End,

    /// <summary>A name: a function, symbol or keyword.</summary>
    Identifier,

    /// <summary>An integer literal.</summary>
    Integer,

    /// <summary>A real literal: one with a decimal point or an exponent.</summary>
    Real,

    /// <summary>A string literal.</summary>
    String,

    /// <summary>An operator or punctuation mark.</summary>
    Punctuation,
}

/// <summary>One QSC token, with where it starts for diagnostics.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Text">The identifier or punctuation text, or a string literal's decoded value.</param>
/// <param name="Integer">An integer literal's value.</param>
/// <param name="Real">A real literal's value.</param>
/// <param name="Position">Where the token starts.</param>
public readonly record struct QscToken(QscTokenKind Kind, string Text, int Integer, float Real, QscSourcePosition Position);

/// <summary>A position in QSC source, one-based.</summary>
/// <param name="Line">The line.</param>
/// <param name="Column">The column.</param>
/// <param name="Offset">The zero-based character offset.</param>
public readonly record struct QscSourcePosition(int Line, int Column, int Offset)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Line}:{Column}";
}

/// <summary>Splits QSC source into tokens.</summary>
/// <remarks>
/// <para>
/// QSC is C-shaped: <c>//</c> and <c>/* */</c> comments, double-quoted strings with C escapes, and the
/// C operator set without compound assignment. Identifiers may contain dots, because a field reference
/// such as <c>HumanSoldier_404.isDead</c> is one symbol in the script namespace, not a member access.
/// </para>
/// <para>
/// A number is an integer unless it has a decimal point or an exponent. A leading minus is never part of
/// a literal: <c>-1</c> is the unary operator applied to <c>1</c>, which is how the original compiles it.
/// </para>
/// </remarks>
public sealed class QscLexer
{
    private static readonly string[] Operators =
    [
        "&&", "||", "==", "!=", "<=", ">=", "<<", ">>",
        "+", "-", "*", "/", "&", "|", "^", "!", "~", "<", ">", "=", "(", ")", "{", "}", ",", ";",
    ];

    private readonly string _source;
    private int _offset;
    private int _line = 1;
    private int _lineStart;

    /// <summary>Initializes a new instance of the <see cref="QscLexer"/> class.</summary>
    /// <param name="source">The source text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public QscLexer(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>Gets whether a character may appear in an identifier after its first character.</summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for letters, digits, <c>_</c> and <c>.</c>.</returns>
    public static bool IsIdentifierPart(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.';

    /// <summary>Gets whether a character may start an identifier.</summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for letters and <c>_</c>.</returns>
    public static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c == '_';

    /// <summary>Reads every token, ending with one <see cref="QscTokenKind.End"/>.</summary>
    /// <returns>The tokens.</returns>
    /// <exception cref="QscParseException">The source holds a character or literal that is not QSC.</exception>
    public List<QscToken> ReadAll()
    {
        List<QscToken> tokens = [];
        QscToken token;
        do
        {
            token = Next();
            tokens.Add(token);
        }
        while (token.Kind != QscTokenKind.End);

        return tokens;
    }

    private QscToken Next()
    {
        SkipTrivia();
        QscSourcePosition at = Here;
        if (_offset >= _source.Length)
        {
            return new QscToken(QscTokenKind.End, string.Empty, 0, 0f, at);
        }

        char c = _source[_offset];
        if (IsIdentifierStart(c))
        {
            int start = _offset;
            while (_offset < _source.Length && IsIdentifierPart(_source[_offset]))
            {
                _offset++;
            }

            return new QscToken(QscTokenKind.Identifier, _source[start.._offset], 0, 0f, at);
        }

        if (char.IsAsciiDigit(c) || (c == '.' && _offset + 1 < _source.Length && char.IsAsciiDigit(_source[_offset + 1])))
        {
            return ReadNumber(at);
        }

        if (c == '"')
        {
            return ReadString(at);
        }

        foreach (string op in Operators)
        {
            if (string.CompareOrdinal(_source, _offset, op, 0, op.Length) == 0)
            {
                _offset += op.Length;
                return new QscToken(QscTokenKind.Punctuation, op, 0, 0f, at);
            }
        }

        throw new QscParseException(at, $"unexpected character '{c}'");
    }

    private QscToken ReadNumber(QscSourcePosition at)
    {
        int start = _offset;
        bool real = false;
        if (_source[_offset] == '0' && _offset + 1 < _source.Length && _source[_offset + 1] is 'x' or 'X')
        {
            _offset += 2;
            while (_offset < _source.Length && char.IsAsciiHexDigit(_source[_offset]))
            {
                _offset++;
            }

            string hex = _source[(start + 2).._offset];
            return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint bits)
                ? new QscToken(QscTokenKind.Integer, _source[start.._offset], unchecked((int)bits), 0f, at)
                : throw new QscParseException(at, $"'{_source[start.._offset]}' is not a 32-bit hexadecimal number");
        }

        while (_offset < _source.Length && char.IsAsciiDigit(_source[_offset]))
        {
            _offset++;
        }

        if (_offset < _source.Length && _source[_offset] == '.')
        {
            real = true;
            _offset++;
            while (_offset < _source.Length && char.IsAsciiDigit(_source[_offset]))
            {
                _offset++;
            }
        }

        if (_offset < _source.Length && _source[_offset] is 'e' or 'E')
        {
            int mark = _offset++;
            if (_offset < _source.Length && _source[_offset] is '+' or '-')
            {
                _offset++;
            }

            if (_offset < _source.Length && char.IsAsciiDigit(_source[_offset]))
            {
                real = true;
                while (_offset < _source.Length && char.IsAsciiDigit(_source[_offset]))
                {
                    _offset++;
                }
            }
            else
            {
                _offset = mark;
            }
        }

        string text = _source[start.._offset];
        if (real)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
                ? new QscToken(QscTokenKind.Real, text, 0, value, at)
                : throw new QscParseException(at, $"'{text}' is not a 32-bit real");
        }

        // An integer as wide as 2147483648 is allowed so that "-2147483648" can be written; it wraps to int.MinValue,
        // and negating that gives it back.
        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out uint integer) && integer <= 2147483648u
            ? new QscToken(QscTokenKind.Integer, text, unchecked((int)integer), 0f, at)
            : throw new QscParseException(at, $"'{text}' does not fit a 32-bit integer");
    }

    private QscToken ReadString(QscSourcePosition at)
    {
        StringBuilder value = new();
        _offset++;
        while (true)
        {
            if (_offset >= _source.Length || _source[_offset] == '\n')
            {
                throw new QscParseException(at, "a string literal is not closed on its line");
            }

            char c = _source[_offset++];
            if (c == '"')
            {
                return new QscToken(QscTokenKind.String, value.ToString(), 0, 0f, at);
            }

            if (c != '\\')
            {
                value.Append(c);
                continue;
            }

            if (_offset >= _source.Length)
            {
                throw new QscParseException(at, "a string literal ends in the middle of an escape");
            }

            char escape = _source[_offset++];
            switch (escape)
            {
                case 'n':
                    value.Append('\n');
                    break;
                case 'r':
                    value.Append('\r');
                    break;
                case 't':
                    value.Append('\t');
                    break;
                case 'x' when _offset + 2 <= _source.Length
                    && int.TryParse(_source.AsSpan(_offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code):
                    value.Append((char)code);
                    _offset += 2;
                    break;
                case '"' or '\\':
                    value.Append(escape);
                    break;
                default:
                    throw new QscParseException(at, $"unknown escape '\\{escape}' in a string literal");
            }
        }
    }

    private void SkipTrivia()
    {
        while (_offset < _source.Length)
        {
            char c = _source[_offset];
            if (c == '\n')
            {
                _offset++;
                _line++;
                _lineStart = _offset;
            }
            else if (char.IsWhiteSpace(c))
            {
                _offset++;
            }
            else if (c == '/' && Peek(1) == '/')
            {
                while (_offset < _source.Length && _source[_offset] != '\n')
                {
                    _offset++;
                }
            }
            else if (c == '/' && Peek(1) == '*')
            {
                QscSourcePosition at = Here;
                int close = _source.IndexOf("*/", _offset + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    throw new QscParseException(at, "a block comment is never closed");
                }

                for (int i = _offset; i < close; i++)
                {
                    if (_source[i] == '\n')
                    {
                        _line++;
                        _lineStart = i + 1;
                    }
                }

                _offset = close + 2;
            }
            else
            {
                return;
            }
        }
    }

    private char Peek(int ahead) => _offset + ahead < _source.Length ? _source[_offset + ahead] : '\0';

    private QscSourcePosition Here => new(_line, _offset - _lineStart + 1, _offset);
}

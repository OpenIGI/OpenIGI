using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// Decodes a QVM code section into instructions and renders them as a listing.
/// </summary>
/// <remarks>
/// <para>
/// Decoding is a linear sweep. That works because the compiler never interleaves data with code: an
/// argument block is ordinary code that the emitter jumps over with a <see cref="QvmOpcode.Bra"/>,
/// so every byte of the section belongs to some instruction.
/// </para>
/// <para>
/// An unknown opcode byte decodes to a one-byte instruction that renders as <c>ILLEGAL</c>, the same
/// answer the original's mnemonic lookup gives. Executing one faults; disassembling one does not.
/// </para>
/// </remarks>
public static class QvmDisassembler
{
    /// <summary>The mnemonic the original reports for an opcode outside the table.</summary>
    public const string IllegalMnemonic = "ILLEGAL";

    private const int CommentColumn = 38;

    /// <summary>Decodes every instruction of a program's code section, in address order.</summary>
    /// <param name="program">The program to decode.</param>
    /// <returns>The instructions, from offset zero to the end of the section.</returns>
    /// <exception cref="QvmFormatException">An instruction runs past the end of the code section.</exception>
    public static IEnumerable<QvmInstruction> Decode(QvmProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return Decode(program.Code, QvmOpcodeEncoding.ForVersion(program.Version));
    }

    /// <summary>Decodes every instruction of a code section, in address order.</summary>
    /// <param name="code">The code section.</param>
    /// <returns>The instructions, from offset zero to the end of the section.</returns>
    /// <exception cref="QvmFormatException">An instruction runs past the end of the code section.</exception>
    public static IEnumerable<QvmInstruction> Decode(ReadOnlyMemory<byte> code) =>
        Decode(code, QvmOpcodeEncoding.Version5);

    /// <summary>Decodes every instruction of a code section written in a given encoding.</summary>
    /// <param name="code">The code section.</param>
    /// <param name="encoding">How this bytecode version numbers the opcodes.</param>
    /// <returns>The instructions, from offset zero to the end of the section.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is <see langword="null"/>.</exception>
    /// <exception cref="QvmFormatException">An instruction runs past the end of the code section.</exception>
    public static IEnumerable<QvmInstruction> Decode(ReadOnlyMemory<byte> code, QvmOpcodeEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        List<QvmInstruction> instructions = [];
        int offset = 0;

        while (offset < code.Length)
        {
            QvmInstruction instruction = DecodeAt(code.Span, offset, encoding);
            instructions.Add(instruction);
            offset += instruction.Length;
        }

        return instructions;
    }

    /// <summary>Decodes the single instruction at an offset.</summary>
    /// <param name="code">The code section.</param>
    /// <param name="offset">The byte offset of the instruction within <paramref name="code"/>.</param>
    /// <returns>The decoded instruction.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is outside <paramref name="code"/>.</exception>
    /// <exception cref="QvmFormatException">The instruction runs past the end of the code section.</exception>
    public static QvmInstruction DecodeAt(ReadOnlySpan<byte> code, int offset) =>
        DecodeAt(code, offset, QvmOpcodeEncoding.Version5);

    /// <summary>Decodes the single instruction at an offset, in a given encoding.</summary>
    /// <param name="code">The code section.</param>
    /// <param name="offset">The byte offset of the instruction within <paramref name="code"/>.</param>
    /// <param name="encoding">How this bytecode version numbers the opcodes.</param>
    /// <returns>The decoded instruction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is outside <paramref name="code"/>.</exception>
    /// <exception cref="QvmFormatException">The instruction runs past the end of the code section.</exception>
    public static QvmInstruction DecodeAt(ReadOnlySpan<byte> code, int offset, QvmOpcodeEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, code.Length);

        if (!encoding.TryDecode(code[offset], out QvmOpcode opcode))
        {
            // A byte this version does not define. Reported as a one-byte unknown rather than thrown,
            // the way an undefined opcode has always been, so a caller sweeping a section sees where
            // the trouble starts instead of only that there was some.
            return new QvmInstruction(offset, (QvmOpcode)code[offset], 1);
        }

        if (!QvmOpcodeInfo.TryGet(opcode, out QvmOpcodeInfo info))
        {
            return new QvmInstruction(offset, opcode, 1);
        }

        ReadOnlySpan<byte> operand = code[(offset + 1)..];

        switch (info.OperandKind)
        {
            case QvmOperandKind.None:
                return new QvmInstruction(offset, opcode, 1);

            case QvmOperandKind.SByte:
                Require(code, offset, 2, opcode);
                return new QvmInstruction(offset, opcode, 2, (sbyte)operand[0]);

            case QvmOperandKind.Index8:
                Require(code, offset, 2, opcode);
                return new QvmInstruction(offset, opcode, 2, operand[0]);

            case QvmOperandKind.Int16:
                Require(code, offset, 3, opcode);
                return new QvmInstruction(offset, opcode, 3, BinaryPrimitives.ReadInt16LittleEndian(operand));

            case QvmOperandKind.Index16:
                Require(code, offset, 3, opcode);
                return new QvmInstruction(offset, opcode, 3, BinaryPrimitives.ReadUInt16LittleEndian(operand));

            case QvmOperandKind.Single:
                Require(code, offset, 5, opcode);
                return new QvmInstruction(offset, opcode, 5, single: BinaryPrimitives.ReadSingleLittleEndian(operand));

            case QvmOperandKind.InlineString:
                return DecodeInlineString(code, offset, opcode);

            case QvmOperandKind.CallArguments:
                return DecodeCall(code, offset, opcode);

            default:
                Require(code, offset, 5, opcode);
                return new QvmInstruction(offset, opcode, 5, BinaryPrimitives.ReadInt32LittleEndian(operand));
        }
    }

    /// <summary>Renders a program as a listing, one line per instruction.</summary>
    /// <param name="program">The program to disassemble.</param>
    /// <returns>The listing lines, in address order.</returns>
    /// <exception cref="QvmFormatException">An instruction runs past the end of the code section.</exception>
    public static IEnumerable<string> Disassemble(QvmProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return [.. Decode(program).Select(instruction => Format(instruction, program))];
    }

    /// <summary>Renders one instruction without resolving its table indices.</summary>
    /// <param name="instruction">The instruction to render.</param>
    /// <returns>The listing line.</returns>
    public static string Format(QvmInstruction instruction) => Format(instruction, null);

    /// <summary>Renders one instruction, resolving table indices against a program.</summary>
    /// <param name="instruction">The instruction to render.</param>
    /// <param name="program">The program the instruction came from, or <see langword="null"/> to leave indices unresolved.</param>
    /// <returns>The listing line.</returns>
    public static string Format(QvmInstruction instruction, QvmProgram? program)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        string mnemonic = QvmOpcodeInfo.TryGet(instruction.Opcode, out QvmOpcodeInfo info)
            ? info.Mnemonic
            : IllegalMnemonic;

        StringBuilder line = new();
        line.Append(CultureInfo.InvariantCulture, $"{instruction.Offset,6}: ");
        line.Append(mnemonic.PadRight(8));
        line.Append(FormatOperand(instruction, info));

        string? comment = FormatComment(instruction, program);
        if (comment is not null)
        {
            while (line.Length < CommentColumn)
            {
                line.Append(' ');
            }

            line.Append("; ").Append(comment);
        }

        return line.ToString().TrimEnd();
    }

    private static QvmInstruction DecodeInlineString(ReadOnlySpan<byte> code, int offset, QvmOpcode opcode)
    {
        ReadOnlySpan<byte> rest = code[(offset + 1)..];
        int terminator = rest.IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new QvmFormatException(
                $"The inline string of the {opcode} at offset {offset} is not terminated before the end of the code section.");
        }

        return new QvmInstruction(offset, opcode, terminator + 2, text: Encoding.Latin1.GetString(rest[..terminator]));
    }

    private static QvmInstruction DecodeCall(ReadOnlySpan<byte> code, int offset, QvmOpcode opcode)
    {
        Require(code, offset, 5, opcode);

        uint argumentCount = BinaryPrimitives.ReadUInt32LittleEndian(code[(offset + 1)..]);
        long length = 5L + (4L * argumentCount);
        if (length > code.Length - offset)
        {
            throw new QvmFormatException(
                $"The CALL at offset {offset} declares {argumentCount} arguments, which run past the end of the code section.");
        }

        int[] offsets = new int[argumentCount];
        for (int i = 0; i < offsets.Length; i++)
        {
            uint argument = BinaryPrimitives.ReadUInt32LittleEndian(code[(offset + 5 + (i * 4))..]);
            if (argument > int.MaxValue)
            {
                throw new QvmFormatException($"Argument {i} of the CALL at offset {offset} points to offset {argument}, which is not addressable.");
            }

            offsets[i] = (int)argument;
        }

        return new QvmInstruction(
            offset,
            opcode,
            (int)length,
            (int)argumentCount,
            argumentOffsets: new ReadOnlyCollection<int>(offsets));
    }

    private static void Require(ReadOnlySpan<byte> code, int offset, int length, QvmOpcode opcode)
    {
        if (code.Length - offset < length)
        {
            throw new QvmFormatException(
                $"The {opcode} at offset {offset} needs {length} bytes but only {code.Length - offset} remain in the code section.");
        }
    }

    private static string FormatOperand(QvmInstruction instruction, QvmOpcodeInfo info) => info.OperandKind switch
    {
        QvmOperandKind.SByte or QvmOperandKind.Int16 or QvmOperandKind.Int32 =>
            instruction.Integer.ToString(CultureInfo.InvariantCulture),
        QvmOperandKind.Index8 or QvmOperandKind.Index16 or QvmOperandKind.Index32 =>
            string.Create(CultureInfo.InvariantCulture, $"#{instruction.Integer}"),
        QvmOperandKind.Single => instruction.Single.ToString("R", CultureInfo.InvariantCulture),
        QvmOperandKind.InlineString => Quote(instruction.Text ?? string.Empty),
        QvmOperandKind.BranchOffset => instruction.Integer.ToString("+0;-0;+0", CultureInfo.InvariantCulture),
        QvmOperandKind.CallArguments => string.Create(CultureInfo.InvariantCulture, $"argc={instruction.Integer}"),
        QvmOperandKind.BlockSize => string.Create(CultureInfo.InvariantCulture, $"size={instruction.Integer}"),
        _ => string.Empty,
    };

    private static string? FormatComment(QvmInstruction instruction, QvmProgram? program)
    {
        switch (instruction.Opcode)
        {
            case QvmOpcode.Bra or QvmOpcode.Bf or QvmOpcode.Bt or QvmOpcode.Blk:
                return string.Create(CultureInfo.InvariantCulture, $"-> {instruction.NextOffset}");

            case QvmOpcode.Call when instruction.ArgumentOffsets.Count > 0:
                return "args " + string.Join(", ", instruction.ArgumentOffsets.Select(
                    argument => string.Create(CultureInfo.InvariantCulture, $"@{argument}")));

            case QvmOpcode.PushSI or QvmOpcode.PushSIB or QvmOpcode.PushSIW when program is not null:
                return program.TryGetString(instruction.Integer, out string value)
                    ? "string " + Quote(value)
                    : "string index out of range";

            case QvmOpcode.PushII or QvmOpcode.PushIIB or QvmOpcode.PushIIW when program is not null:
                return program.TryGetSymbolName(instruction.Integer, out string name)
                    ? "symbol " + Quote(name)
                    : "symbol index out of range";

            default:
                return null;
        }
    }

    private static string Quote(string value)
    {
        StringBuilder quoted = new(value.Length + 2);
        quoted.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case '\n':
                    quoted.Append("\\n");
                    break;
                case '\r':
                    quoted.Append("\\r");
                    break;
                case '\t':
                    quoted.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        quoted.Append(CultureInfo.InvariantCulture, $"\\x{(int)character:X2}");
                    }
                    else
                    {
                        quoted.Append(character);
                    }

                    break;
            }
        }

        quoted.Append('"');
        return quoted.ToString();
    }
}

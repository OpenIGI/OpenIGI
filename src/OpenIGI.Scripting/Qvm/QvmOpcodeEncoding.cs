namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// How a bytecode version numbers the opcodes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="QvmOpcode"/> is the <em>meaning</em> of an instruction and is numbered the way version 5
/// encodes it, because that is the version this port was written against. Version 7 gives the same
/// instructions different numbers, so it needs a translation rather than a second opcode set: the
/// language, the operand shapes and the interpreter are unchanged, and only the byte in the file moves.
/// </para>
/// <para>
/// Version 7 moves the control-flow group — <c>RET</c> through <c>CALL</c>, which version 5 puts at
/// <c>0x13</c>–<c>0x18</c> — down to <c>0x02</c>–<c>0x07</c>, and shifts everything from <c>PUSH</c> to
/// <c>POP</c> up by six to fill the gap. <c>BRK</c> and <c>NOP</c> keep <c>0x00</c> and <c>0x01</c>, and
/// <b>the whole arithmetic block from <c>ADD</c> at <c>0x19</c> to <c>BLK</c> at <c>0x2F</c> is
/// untouched</b>, which is why a version 5 reader gets some way through a version 7 file before it
/// desynchronises.
/// </para>
/// <para>
/// Read out of I.G.I. 2's own <c>QVM_Open</c>, which fills the 49-slot handler table one index at a
/// time, and checked against every script in a retail installation: with this map a linear sweep lands
/// exactly on the code-section boundary in all 1,784, which is what proves the operand widths.
/// </para>
/// </remarks>
public sealed class QvmOpcodeEncoding
{
    private readonly QvmOpcode?[] _fromByte = new QvmOpcode?[byte.MaxValue + 1];
    private readonly byte?[] _toByte = new byte?[QvmOpcodeInfo.Count];

    private QvmOpcodeEncoding(int version, IEnumerable<(byte Byte, QvmOpcode Opcode)> map)
    {
        Version = version;

        foreach ((byte value, QvmOpcode opcode) in map)
        {
            _fromByte[value] = opcode;
            _toByte[(byte)opcode] = value;
        }
    }

    /// <summary>Gets the bytecode version this encoding belongs to.</summary>
    public int Version { get; }

    /// <summary>Gets the version 5 encoding, in which the byte and the opcode are the same number.</summary>
    public static QvmOpcodeEncoding Version5 { get; } = new(
        QvmProgram.IgiVersion,
        Enumerable.Range(QvmOpcodeInfo.MinimumOpcode, QvmOpcodeInfo.Count)
            .Select(static v => ((byte)v, (QvmOpcode)v)));

    /// <summary>Gets the version 7 encoding — I.G.I. 2's.</summary>
    /// <remarks>
    /// <c>0x02</c> and <c>0x06</c> are deliberately absent. They are where version 7 puts <c>RET</c> and
    /// <c>JSR</c>, and its own handler table points both at the illegal-instruction handler, so a file
    /// carrying one is malformed rather than using a feature this port has not implemented.
    /// </remarks>
    public static QvmOpcodeEncoding Version7 { get; } = new(
        QvmProgram.Igi2Version,
        [
            ((byte)0x00, QvmOpcode.Brk),
            ((byte)0x01, QvmOpcode.Nop),
            ((byte)0x03, QvmOpcode.Bra),
            ((byte)0x04, QvmOpcode.Bf),
            ((byte)0x05, QvmOpcode.Bt),
            ((byte)0x07, QvmOpcode.Call),
            ((byte)0x08, QvmOpcode.Push),
            ((byte)0x09, QvmOpcode.PushB),
            ((byte)0x0A, QvmOpcode.PushW),
            ((byte)0x0B, QvmOpcode.PushF),
            ((byte)0x0C, QvmOpcode.PushA),
            ((byte)0x0D, QvmOpcode.PushS),
            ((byte)0x0E, QvmOpcode.PushSI),
            ((byte)0x0F, QvmOpcode.PushSIB),
            ((byte)0x10, QvmOpcode.PushSIW),
            ((byte)0x11, QvmOpcode.PushI),
            ((byte)0x12, QvmOpcode.PushII),
            ((byte)0x13, QvmOpcode.PushIIB),
            ((byte)0x14, QvmOpcode.PushIIW),
            ((byte)0x15, QvmOpcode.Push0),
            ((byte)0x16, QvmOpcode.Push1),
            ((byte)0x17, QvmOpcode.PushM),
            ((byte)0x18, QvmOpcode.Pop),

            // 0x19 to 0x2F carry the same numbers in both versions.
            ((byte)0x19, QvmOpcode.Add),
            ((byte)0x1A, QvmOpcode.Sub),
            ((byte)0x1B, QvmOpcode.Mul),
            ((byte)0x1C, QvmOpcode.Div),
            ((byte)0x1D, QvmOpcode.Shl),
            ((byte)0x1E, QvmOpcode.Shr),
            ((byte)0x1F, QvmOpcode.And),
            ((byte)0x20, QvmOpcode.Or),
            ((byte)0x21, QvmOpcode.Xor),
            ((byte)0x22, QvmOpcode.LAnd),
            ((byte)0x23, QvmOpcode.LOr),
            ((byte)0x24, QvmOpcode.Eq),
            ((byte)0x25, QvmOpcode.Ne),
            ((byte)0x26, QvmOpcode.Lt),
            ((byte)0x27, QvmOpcode.Le),
            ((byte)0x28, QvmOpcode.Gt),
            ((byte)0x29, QvmOpcode.Ge),
            ((byte)0x2A, QvmOpcode.Assign),
            ((byte)0x2B, QvmOpcode.Plus),
            ((byte)0x2C, QvmOpcode.Minus),
            ((byte)0x2D, QvmOpcode.Inv),
            ((byte)0x2E, QvmOpcode.Not),
            ((byte)0x2F, QvmOpcode.Blk),
        ]);

    /// <summary>Gets the encoding a bytecode version uses.</summary>
    /// <param name="version">The version word from the program header.</param>
    /// <returns>The encoding.</returns>
    /// <exception cref="QvmFormatException">No encoding is known for that version.</exception>
    public static QvmOpcodeEncoding ForVersion(int version) => version switch
    {
        QvmProgram.IgiVersion => Version5,
        QvmProgram.Igi2Version => Version7,
        _ => throw new QvmFormatException(
            $"No opcode encoding is known for QVM version {version}; this port reads "
            + $"{QvmProgram.IgiVersion} and {QvmProgram.Igi2Version}."),
    };

    /// <summary>Translates an encoded byte into the opcode it means.</summary>
    /// <param name="value">The byte from the code stream.</param>
    /// <param name="opcode">Receives the opcode when the byte encodes one.</param>
    /// <returns><see langword="false"/> when this version does not define that byte.</returns>
    public bool TryDecode(byte value, out QvmOpcode opcode)
    {
        QvmOpcode? found = _fromByte[value];
        opcode = found ?? default;
        return found.HasValue;
    }

    /// <summary>Translates an opcode into the byte this version encodes it as.</summary>
    /// <param name="opcode">The opcode to encode.</param>
    /// <param name="value">Receives the byte when this version encodes the opcode.</param>
    /// <returns><see langword="false"/> when this version has no encoding for that opcode.</returns>
    public bool TryEncode(QvmOpcode opcode, out byte value)
    {
        byte? found = (byte)opcode < _toByte.Length ? _toByte[(byte)opcode] : null;
        value = found ?? default;
        return found.HasValue;
    }
}

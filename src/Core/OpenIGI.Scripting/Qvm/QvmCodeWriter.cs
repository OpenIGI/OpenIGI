using System.Buffers.Binary;
using System.Text;

namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// Assembles a QVM code section, resolving branch displacements and call argument offsets.
/// </summary>
/// <remarks>
/// Enough of an assembler to write readable tests: emit instructions, place labels, and let the
/// writer work out the encodings the interpreter and disassembler have to agree on.
/// </remarks>
public sealed class QvmCodeWriter
{
    private readonly List<byte> _code = [];
    private readonly List<(int Position, QvmLabel Label, bool Relative)> _fixups = [];
    private readonly QvmOpcodeEncoding _encoding;
    private int _labelCount;

    /// <summary>Initializes a new instance of the <see cref="QvmCodeWriter"/> class for I.G.I.'s version 5 encoding.</summary>
    public QvmCodeWriter()
        : this(QvmOpcodeEncoding.Version5)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QvmCodeWriter"/> class.</summary>
    /// <param name="encoding">The opcode numbering to write, which differs between the two games' bytecode versions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is <see langword="null"/>.</exception>
    public QvmCodeWriter(QvmOpcodeEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _encoding = encoding;
    }

    /// <summary>Gets the opcode numbering this writer emits.</summary>
    public QvmOpcodeEncoding OpcodeEncoding => _encoding;

    /// <summary>Gets the current code offset, which is where the next instruction will start.</summary>
    public int Position => _code.Count;

    /// <summary>Creates an unplaced label.</summary>
    /// <returns>The label.</returns>
    public QvmLabel CreateLabel() => new(_labelCount++);

    /// <summary>Places a label at the current offset.</summary>
    /// <param name="label">The label to place.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter MarkLabel(QvmLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);
        label.Position = _code.Count;
        return this;
    }

    /// <summary>Emits an opcode that takes no operand.</summary>
    /// <param name="opcode">The opcode.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter Emit(QvmOpcode opcode)
    {
        AddOpcode(opcode);
        return this;
    }

    /// <summary>Emits <c>PUSH</c> with a 32-bit immediate.</summary>
    /// <param name="value">The immediate.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter Push(int value) => EmitInt32(QvmOpcode.Push, value);

    /// <summary>Emits <c>PUSHB</c> with a signed byte immediate.</summary>
    /// <param name="value">The immediate.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter PushB(sbyte value)
    {
        AddOpcode(QvmOpcode.PushB);
        _code.Add((byte)value);
        return this;
    }

    /// <summary>Emits <c>PUSHW</c> with a signed 16-bit immediate.</summary>
    /// <param name="value">The immediate.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter PushW(short value)
    {
        AddOpcode(QvmOpcode.PushW);
        AddInt16(value);
        return this;
    }

    /// <summary>Emits <c>PUSHF</c> with a 32-bit floating-point immediate.</summary>
    /// <param name="value">The immediate.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter PushF(float value)
    {
        AddOpcode(QvmOpcode.PushF);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        _code.AddRange(bytes);
        return this;
    }

    /// <summary>Emits <c>PUSHA</c> with a 32-bit address immediate.</summary>
    /// <param name="value">The immediate.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter PushA(int value) => EmitInt32(QvmOpcode.PushA, value);

    /// <summary>Emits <c>PUSHS</c> with an inline string.</summary>
    /// <param name="value">The string.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter PushS(string value) => EmitInline(QvmOpcode.PushS, value);

    /// <summary>Emits <c>PUSHI</c> with an inline identifier.</summary>
    /// <param name="name">The identifier.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter PushI(string name) => EmitInline(QvmOpcode.PushI, name);

    /// <summary>Emits the narrowest <c>PUSHSI</c> form that fits a string-table index.</summary>
    /// <param name="index">The string-table index.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter PushStringIndex(int index) =>
        EmitIndex(index, QvmOpcode.PushSIB, QvmOpcode.PushSIW, QvmOpcode.PushSI);

    /// <summary>Emits the narrowest <c>PUSHII</c> form that fits a symbol-table index.</summary>
    /// <param name="index">The symbol-name-table index.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter PushSymbolIndex(int index) =>
        EmitIndex(index, QvmOpcode.PushIIB, QvmOpcode.PushIIW, QvmOpcode.PushII);

    /// <summary>Emits a branch to a label.</summary>
    /// <param name="opcode">One of <c>BRA</c>, <c>BF</c> or <c>BT</c>.</param>
    /// <param name="target">The label to branch to.</param>
    /// <returns>This writer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opcode"/> is not a branch.</exception>
    public QvmCodeWriter Branch(QvmOpcode opcode, QvmLabel target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (opcode is not (QvmOpcode.Bra or QvmOpcode.Bf or QvmOpcode.Bt))
        {
            throw new ArgumentOutOfRangeException(nameof(opcode), opcode, "Only BRA, BF and BT take a branch target.");
        }

        AddOpcode(opcode);
        _fixups.Add((_code.Count, target, Relative: true));
        AddInt32(0);
        return this;
    }

    /// <summary>Emits a <c>CALL</c> whose arguments are the blocks starting at the given labels.</summary>
    /// <param name="arguments">One label per argument, in argument order.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter Call(params QvmLabel[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        AddOpcode(QvmOpcode.Call);
        AddInt32(arguments.Length);
        foreach (QvmLabel argument in arguments)
        {
            _fixups.Add((_code.Count, argument, Relative: false));
            AddInt32(0);
        }

        return this;
    }

    /// <summary>Emits <c>BLK</c> skipping a run of bytes.</summary>
    /// <param name="size">The number of bytes to skip.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter Blk(int size) => EmitInt32(QvmOpcode.Blk, size);

    /// <summary>Emits raw bytes, for tests that need an encoding no emitter produces.</summary>
    /// <param name="bytes">The bytes to append.</param>
    /// <returns>This writer.</returns>
    public QvmCodeWriter Raw(params byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _code.AddRange(bytes);
        return this;
    }

    /// <summary>Resolves every fixup and returns the finished code section.</summary>
    /// <returns>The code bytes.</returns>
    /// <exception cref="InvalidOperationException">A referenced label was never placed.</exception>
    public byte[] ToArray()
    {
        byte[] code = [.. _code];

        foreach ((int position, QvmLabel label, bool relative) in _fixups)
        {
            if (!label.IsMarked)
            {
                throw new InvalidOperationException($"Label {label.Id} is referenced by the code but was never marked.");
            }

            // A branch displacement is measured from the end of its five-byte instruction; a call
            // argument offset is absolute within the code section.
            int value = relative ? label.Position - (position + 4) : label.Position;
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(position), value);
        }

        return code;
    }

    private QvmCodeWriter EmitIndex(int index, QvmOpcode narrow, QvmOpcode medium, QvmOpcode wide)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if (index <= byte.MaxValue)
        {
            AddOpcode(narrow);
            _code.Add((byte)index);
        }
        else if (index <= ushort.MaxValue)
        {
            AddOpcode(medium);
            AddInt16((short)index);
        }
        else
        {
            EmitInt32(wide, index);
        }

        return this;
    }

    private QvmCodeWriter EmitInline(QvmOpcode opcode, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        AddOpcode(opcode);
        _code.AddRange(Encoding.Latin1.GetBytes(value));
        _code.Add(0);
        return this;
    }

    private QvmCodeWriter EmitInt32(QvmOpcode opcode, int value)
    {
        AddOpcode(opcode);
        AddInt32(value);
        return this;
    }

    private void AddOpcode(QvmOpcode opcode)
    {
        if (!_encoding.TryEncode(opcode, out byte value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(opcode), opcode, $"QVM version {_encoding.Version} has no encoding for {opcode}.");
        }

        _code.Add(value);
    }

    private void AddInt16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        _code.AddRange(bytes);
    }

    private void AddInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _code.AddRange(bytes);
    }
}

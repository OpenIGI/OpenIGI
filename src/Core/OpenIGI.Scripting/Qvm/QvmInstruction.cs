using System.Collections.ObjectModel;

namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// One decoded QVM instruction: its position, its opcode and whichever operand form it carries.
/// </summary>
/// <remarks>
/// Only the operand member that matches <see cref="QvmOpcodeInfo.OperandKind"/> is meaningful; the
/// others hold their default. The decoder never guesses, so an instruction whose operand is a table
/// index leaves <see cref="Text"/> unresolved — the disassembler resolves it against a program.
/// </remarks>
public sealed class QvmInstruction
{
    private static readonly ReadOnlyCollection<int> NoArguments = new([]);

    internal QvmInstruction(
        int offset,
        QvmOpcode opcode,
        int length,
        int integer = 0,
        float single = 0f,
        string? text = null,
        ReadOnlyCollection<int>? argumentOffsets = null)
    {
        Offset = offset;
        Opcode = opcode;
        Length = length;
        Integer = integer;
        Single = single;
        Text = text;
        ArgumentOffsets = argumentOffsets ?? NoArguments;
    }

    /// <summary>Gets the byte offset of the instruction within the code section.</summary>
    public int Offset { get; }

    /// <summary>Gets the opcode.</summary>
    public QvmOpcode Opcode { get; }

    /// <summary>Gets the encoded length of the instruction in bytes.</summary>
    /// <remarks>
    /// This is the encoding length, not the distance the program counter moves: <see cref="QvmOpcode.Brk"/>
    /// halts without advancing, branches jump, and <see cref="QvmOpcode.Blk"/> additionally skips
    /// <see cref="Integer"/> bytes of block content.
    /// </remarks>
    public int Length { get; }

    /// <summary>Gets the integer operand: an immediate, a table index, a branch displacement, an argument count or a block size.</summary>
    public int Integer { get; }

    /// <summary>Gets the floating-point immediate of <see cref="QvmOpcode.PushF"/>.</summary>
    public float Single { get; }

    /// <summary>Gets the inline string of <see cref="QvmOpcode.PushS"/> and <see cref="QvmOpcode.PushI"/>, or <see langword="null"/>.</summary>
    public string? Text { get; }

    /// <summary>Gets the code offsets of a <see cref="QvmOpcode.Call"/>'s argument blocks, in argument order.</summary>
    /// <remarks>Empty for every other opcode. Each offset addresses a block terminated by <see cref="QvmOpcode.Brk"/>.</remarks>
    public IReadOnlyList<int> ArgumentOffsets { get; }

    /// <summary>Gets the offset the instruction's operand describes, for the opcodes that name one.</summary>
    /// <remarks>
    /// For a branch this is the target — the displacement applied to the end of the instruction. For
    /// <see cref="QvmOpcode.Blk"/> it is the first byte after the skipped block. Otherwise it is the
    /// next instruction.
    /// </remarks>
    public int NextOffset => Opcode switch
    {
        QvmOpcode.Bra or QvmOpcode.Bf or QvmOpcode.Bt or QvmOpcode.Blk => Offset + Length + Integer,
        _ => Offset + Length,
    };

    /// <inheritdoc/>
    public override string ToString() => QvmDisassembler.Format(this);
}

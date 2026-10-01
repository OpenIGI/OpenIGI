using System.Collections.Frozen;

namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// The static description of one opcode: its mnemonic and how it encodes its operand.
/// </summary>
/// <param name="Opcode">The opcode this entry describes.</param>
/// <param name="Mnemonic">The mnemonic the original assembler and disassembler use.</param>
/// <param name="OperandKind">How the operand that follows the opcode byte is encoded.</param>
public readonly record struct QvmOpcodeInfo(QvmOpcode Opcode, string Mnemonic, QvmOperandKind OperandKind)
{
    /// <summary>The lowest defined opcode value.</summary>
    public const int MinimumOpcode = 0x00;

    /// <summary>The highest defined opcode value.</summary>
    public const int MaximumOpcode = 0x2F;

    /// <summary>The number of defined opcodes.</summary>
    /// <remarks>
    /// The original registers 49 handler slots but only names 48 of them: its mnemonic table maps
    /// 0x00–0x2F and answers <c>ILLEGAL</c> for everything above, so 0x30 is a reserved slot rather
    /// than an instruction.
    /// </remarks>
    public const int Count = MaximumOpcode - MinimumOpcode + 1;

    private static readonly QvmOpcodeInfo[] Table = BuildTable();

    private static readonly FrozenDictionary<string, QvmOpcode> ByMnemonic =
        Table.ToFrozenDictionary(entry => entry.Mnemonic, entry => entry.Opcode, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the instruction length in bytes for operands whose size does not depend on their content.
    /// </summary>
    /// <remarks>
    /// <see cref="QvmOperandKind.InlineString"/> and <see cref="QvmOperandKind.CallArguments"/> are
    /// variable length, so their entries report the length of the fixed part only. The decoder
    /// measures those instructions from the code stream.
    /// </remarks>
    public int FixedLength => OperandKind switch
    {
        QvmOperandKind.None => 1,
        QvmOperandKind.SByte or QvmOperandKind.Index8 => 2,
        QvmOperandKind.Int16 or QvmOperandKind.Index16 => 3,
        QvmOperandKind.InlineString => 1,
        QvmOperandKind.CallArguments => 5,
        _ => 5,
    };

    /// <summary>Gets the description of an opcode.</summary>
    /// <param name="opcode">The opcode to describe.</param>
    /// <returns>The table entry for <paramref name="opcode"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opcode"/> is not one of the 49 defined opcodes.</exception>
    public static QvmOpcodeInfo Get(QvmOpcode opcode) =>
        TryGet(opcode, out QvmOpcodeInfo info)
            ? info
            : throw new ArgumentOutOfRangeException(nameof(opcode), opcode, "There is no such QVM opcode.");

    /// <summary>Looks up the description of an opcode.</summary>
    /// <param name="opcode">The opcode to describe.</param>
    /// <param name="info">Receives the table entry when the opcode is defined.</param>
    /// <returns><see langword="true"/> when <paramref name="opcode"/> is one of the 49 defined opcodes.</returns>
    public static bool TryGet(QvmOpcode opcode, out QvmOpcodeInfo info)
    {
        if ((byte)opcode > MaximumOpcode)
        {
            info = default;
            return false;
        }

        info = Table[(byte)opcode];
        return true;
    }

    /// <summary>Looks up an opcode by its mnemonic, ignoring case.</summary>
    /// <param name="mnemonic">The mnemonic, for example <c>PUSHIIB</c>.</param>
    /// <param name="opcode">Receives the opcode when the mnemonic is known.</param>
    /// <returns><see langword="true"/> when the mnemonic names an opcode.</returns>
    public static bool TryParseMnemonic(string mnemonic, out QvmOpcode opcode)
    {
        ArgumentNullException.ThrowIfNull(mnemonic);
        return ByMnemonic.TryGetValue(mnemonic, out opcode);
    }

    /// <summary>Gets every opcode description, ordered by opcode value.</summary>
    /// <returns>The 49 table entries.</returns>
    public static IReadOnlyList<QvmOpcodeInfo> All() => Table;

    private static QvmOpcodeInfo[] BuildTable()
    {
        QvmOpcodeInfo[] table = new QvmOpcodeInfo[Count];

        void Add(QvmOpcode opcode, string mnemonic, QvmOperandKind operand) =>
            table[(byte)opcode] = new QvmOpcodeInfo(opcode, mnemonic, operand);

        Add(QvmOpcode.Brk, "BRK", QvmOperandKind.None);
        Add(QvmOpcode.Nop, "NOP", QvmOperandKind.None);
        Add(QvmOpcode.Push, "PUSH", QvmOperandKind.Int32);
        Add(QvmOpcode.PushB, "PUSHB", QvmOperandKind.SByte);
        Add(QvmOpcode.PushW, "PUSHW", QvmOperandKind.Int16);
        Add(QvmOpcode.PushF, "PUSHF", QvmOperandKind.Single);
        Add(QvmOpcode.PushA, "PUSHA", QvmOperandKind.Int32);
        Add(QvmOpcode.PushS, "PUSHS", QvmOperandKind.InlineString);
        Add(QvmOpcode.PushSI, "PUSHSI", QvmOperandKind.Index32);
        Add(QvmOpcode.PushSIB, "PUSHSIB", QvmOperandKind.Index8);
        Add(QvmOpcode.PushSIW, "PUSHSIW", QvmOperandKind.Index16);
        Add(QvmOpcode.PushI, "PUSHI", QvmOperandKind.InlineString);
        Add(QvmOpcode.PushII, "PUSHII", QvmOperandKind.Index32);
        Add(QvmOpcode.PushIIB, "PUSHIIB", QvmOperandKind.Index8);
        Add(QvmOpcode.PushIIW, "PUSHIIW", QvmOperandKind.Index16);
        Add(QvmOpcode.Push0, "PUSH0", QvmOperandKind.None);
        Add(QvmOpcode.Push1, "PUSH1", QvmOperandKind.None);
        Add(QvmOpcode.PushM, "PUSHM", QvmOperandKind.None);
        Add(QvmOpcode.Pop, "POP", QvmOperandKind.None);
        Add(QvmOpcode.Ret, "RET", QvmOperandKind.None);
        Add(QvmOpcode.Bra, "BRA", QvmOperandKind.BranchOffset);
        Add(QvmOpcode.Bf, "BF", QvmOperandKind.BranchOffset);
        Add(QvmOpcode.Bt, "BT", QvmOperandKind.BranchOffset);
        Add(QvmOpcode.Jsr, "JSR", QvmOperandKind.None);
        Add(QvmOpcode.Call, "CALL", QvmOperandKind.CallArguments);
        Add(QvmOpcode.Add, "ADD", QvmOperandKind.None);
        Add(QvmOpcode.Sub, "SUB", QvmOperandKind.None);
        Add(QvmOpcode.Mul, "MUL", QvmOperandKind.None);
        Add(QvmOpcode.Div, "DIV", QvmOperandKind.None);
        Add(QvmOpcode.Shl, "SHL", QvmOperandKind.None);
        Add(QvmOpcode.Shr, "SHR", QvmOperandKind.None);
        Add(QvmOpcode.And, "AND", QvmOperandKind.None);
        Add(QvmOpcode.Or, "OR", QvmOperandKind.None);
        Add(QvmOpcode.Xor, "XOR", QvmOperandKind.None);
        Add(QvmOpcode.LAnd, "LAND", QvmOperandKind.None);
        Add(QvmOpcode.LOr, "LOR", QvmOperandKind.None);
        Add(QvmOpcode.Eq, "EQ", QvmOperandKind.None);
        Add(QvmOpcode.Ne, "NE", QvmOperandKind.None);
        Add(QvmOpcode.Lt, "LT", QvmOperandKind.None);
        Add(QvmOpcode.Le, "LE", QvmOperandKind.None);
        Add(QvmOpcode.Gt, "GT", QvmOperandKind.None);
        Add(QvmOpcode.Ge, "GE", QvmOperandKind.None);
        Add(QvmOpcode.Assign, "ASSIGN", QvmOperandKind.None);
        Add(QvmOpcode.Plus, "PLUS", QvmOperandKind.None);
        Add(QvmOpcode.Minus, "MINUS", QvmOperandKind.None);
        Add(QvmOpcode.Inv, "INV", QvmOperandKind.None);
        Add(QvmOpcode.Not, "NOT", QvmOperandKind.None);
        Add(QvmOpcode.Blk, "BLK", QvmOperandKind.BlockSize);

        return table;
    }
}

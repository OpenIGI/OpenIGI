namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// How an opcode encodes its operand, and therefore how long the instruction is.
/// </summary>
public enum QvmOperandKind
{
    /// <summary>No operand: the instruction is one byte.</summary>
    None,

    /// <summary>A signed 8-bit immediate.</summary>
    SByte,

    /// <summary>A signed 16-bit immediate.</summary>
    Int16,

    /// <summary>A signed 32-bit immediate.</summary>
    Int32,

    /// <summary>A 32-bit IEEE-754 immediate.</summary>
    Single,

    /// <summary>An unsigned 8-bit table index.</summary>
    Index8,

    /// <summary>An unsigned 16-bit table index.</summary>
    Index16,

    /// <summary>An unsigned 32-bit table index.</summary>
    Index32,

    /// <summary>A NUL-terminated string stored inline in the code stream.</summary>
    InlineString,

    /// <summary>A signed 32-bit displacement measured from the end of the instruction.</summary>
    BranchOffset,

    /// <summary>An argument count followed by that many 32-bit code offsets.</summary>
    CallArguments,

    /// <summary>An unsigned 32-bit byte count that the instruction skips over.</summary>
    BlockSize,
}

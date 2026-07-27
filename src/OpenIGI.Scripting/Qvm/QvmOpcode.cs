namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// The 49 opcodes of the QVM instruction set, in their numeric order.
/// </summary>
/// <remarks>
/// The values are the byte encodings. They come from the mnemonic table the retail executable ships
/// for its own disassembler; the semantics come from the handler table its VM registers at start-up.
/// </remarks>
public enum QvmOpcode : byte
{
    /// <summary>Ends the running block: the interpreter stops and leaves the program counter in place.</summary>
    /// <remarks>Terminates both the top-level program and each lazily evaluated argument block.</remarks>
    Brk = 0x00,

    /// <summary>Does nothing.</summary>
    Nop = 0x01,

    /// <summary>Pushes a 32-bit integer immediate.</summary>
    Push = 0x02,

    /// <summary>Pushes a sign-extended 8-bit integer immediate.</summary>
    PushB = 0x03,

    /// <summary>Pushes a sign-extended 16-bit integer immediate.</summary>
    PushW = 0x04,

    /// <summary>Pushes a 32-bit floating-point immediate.</summary>
    PushF = 0x05,

    /// <summary>Pushes a 32-bit immediate as an address.</summary>
    PushA = 0x06,

    /// <summary>Pushes an inline NUL-terminated string constant.</summary>
    PushS = 0x07,

    /// <summary>Pushes a string constant by 32-bit string-table index.</summary>
    PushSI = 0x08,

    /// <summary>Pushes a string constant by 8-bit string-table index.</summary>
    PushSIB = 0x09,

    /// <summary>Pushes a string constant by 16-bit string-table index.</summary>
    PushSIW = 0x0A,

    /// <summary>Pushes an inline NUL-terminated identifier, to be resolved when it is consumed.</summary>
    PushI = 0x0B,

    /// <summary>Pushes a symbol by 32-bit symbol-name-table index.</summary>
    PushII = 0x0C,

    /// <summary>Pushes a symbol by 8-bit symbol-name-table index.</summary>
    PushIIB = 0x0D,

    /// <summary>Pushes a symbol by 16-bit symbol-name-table index.</summary>
    PushIIW = 0x0E,

    /// <summary>Pushes the integer zero.</summary>
    Push0 = 0x0F,

    /// <summary>Pushes the integer one.</summary>
    Push1 = 0x10,

    /// <summary>Pushes the integer minus one.</summary>
    PushM = 0x11,

    /// <summary>Discards the top of the stack; the statement terminator.</summary>
    Pop = 0x12,

    /// <summary>Return. Never registered with a handler: executing it raises <see cref="Runtime.QvmError.IllegalInstruction"/>.</summary>
    Ret = 0x13,

    /// <summary>Branches unconditionally by a signed 32-bit displacement from the end of the instruction.</summary>
    Bra = 0x14,

    /// <summary>Pops a number and branches when it is zero.</summary>
    Bf = 0x15,

    /// <summary>Pops a number and branches when it is non-zero.</summary>
    Bt = 0x16,

    /// <summary>Jump to subroutine. Never registered with a handler: executing it raises <see cref="Runtime.QvmError.IllegalInstruction"/>.</summary>
    Jsr = 0x17,

    /// <summary>Calls the function symbol on the stack, passing argument blocks that the callee evaluates on demand.</summary>
    Call = 0x18,

    /// <summary>Adds the two values on the stack; concatenates them when both are strings.</summary>
    Add = 0x19,

    /// <summary>Subtracts the top of the stack from the value below it.</summary>
    Sub = 0x1A,

    /// <summary>Multiplies the two values on the stack.</summary>
    Mul = 0x1B,

    /// <summary>Divides the value below the top of the stack by the top.</summary>
    Div = 0x1C,

    /// <summary>Shifts left. Integers only.</summary>
    Shl = 0x1D,

    /// <summary>Shifts right, arithmetically. Integers only.</summary>
    Shr = 0x1E,

    /// <summary>Bitwise AND. Integers only.</summary>
    And = 0x1F,

    /// <summary>Bitwise OR. Integers only.</summary>
    Or = 0x20,

    /// <summary>Bitwise exclusive OR. Integers only.</summary>
    Xor = 0x21,

    /// <summary>Logical AND, yielding an integer 0 or 1.</summary>
    LAnd = 0x22,

    /// <summary>Logical OR, yielding an integer 0 or 1.</summary>
    LOr = 0x23,

    /// <summary>Equality comparison, yielding an integer 0 or 1.</summary>
    Eq = 0x24,

    /// <summary>Inequality comparison, yielding an integer 0 or 1.</summary>
    Ne = 0x25,

    /// <summary>Less-than comparison, yielding an integer 0 or 1.</summary>
    Lt = 0x26,

    /// <summary>Less-than-or-equal comparison, yielding an integer 0 or 1.</summary>
    Le = 0x27,

    /// <summary>Greater-than comparison, yielding an integer 0 or 1.</summary>
    Gt = 0x28,

    /// <summary>Greater-than-or-equal comparison, yielding an integer 0 or 1.</summary>
    Ge = 0x29,

    /// <summary>Stores the top of the stack into the variable symbol below it and pushes the stored value back.</summary>
    Assign = 0x2A,

    /// <summary>Unary plus.</summary>
    Plus = 0x2B,

    /// <summary>Unary negation.</summary>
    Minus = 0x2C,

    /// <summary>Bitwise complement. Integers only.</summary>
    Inv = 0x2D,

    /// <summary>Logical negation, yielding an integer 0 or 1. Integers only.</summary>
    Not = 0x2E,

    /// <summary>Skips a block: the 32-bit operand is the number of bytes to jump over.</summary>
    Blk = 0x2F,
}

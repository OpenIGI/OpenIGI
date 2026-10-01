namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// The fault codes the VM reports. A running program never throws; it stops and leaves one of these.
/// </summary>
/// <remarks>
/// The values are the original's, taken from its error decoder at <c>0x4BD080</c>. Codes 4 and 12 are
/// absent from that decoder — it prints "Undefined error" for them — but the branch handler does
/// raise 12, so it is named here.
/// </remarks>
public enum QvmError
{
    /// <summary>The program ran to completion.</summary>
    None = 0,

    /// <summary>A value was popped from an empty stack.</summary>
    /// <remarks>
    /// The original never raises this at run time: its pop reads whatever the stack memory holds.
    /// OpenIGI refuses instead, which only differs where the original would read stale memory.
    /// </remarks>
    StackUnderflow = 1,

    /// <summary>More values were pushed than the program's stack can hold.</summary>
    /// <remarks>
    /// As with <see cref="StackUnderflow"/>, the original's push is unchecked and would overrun its
    /// stack allocation.
    /// </remarks>
    StackOverflow = 2,

    /// <summary>An internal inconsistency: the original's "Internal error (read: BUG!!!)".</summary>
    InternalError = 3,

    /// <summary>The opcode byte has no handler. <c>RET</c> and <c>JSR</c> always land here.</summary>
    IllegalInstruction = 5,

    /// <summary>The operands have types the instruction cannot combine, such as a bitwise operator on floats.</summary>
    IllegalOperand = 6,

    /// <summary>An identifier did not resolve to a registered symbol.</summary>
    UnknownIdentifier = 7,

    /// <summary>The target of an assignment resolved to a symbol that is not a variable.</summary>
    IdentifierNotVariable = 8,

    /// <summary>The callee of a call resolved to a symbol that is not a function.</summary>
    IdentifierNotFunction = 9,

    /// <summary>A call executed with no callee symbol on the stack.</summary>
    NoFunctionOnStack = 10,

    /// <summary>An assignment's value has a type the target variable cannot store.</summary>
    SyntaxError = 11,

    /// <summary>A conditional branch popped something that is not a number.</summary>
    /// <remarks>The original's error decoder has no entry for this code and reports "Undefined error".</remarks>
    BranchOperandNotNumeric = 12,

    /// <summary>A call passed more arguments than the VM can hold.</summary>
    ArgumentOverflow = 13,

    /// <summary>A referenced datablock is not loaded.</summary>
    DatablockNotPresent = 14,

    /// <summary>A symbol-name or string-table index addresses no entry.</summary>
    IndexOutOfRange = 15,
}

namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// The type tag a value carries on the VM stack.
/// </summary>
/// <remarks>
/// The numeric values are the original's tags. It keeps a parallel type stack beside the value stack
/// and switches on these; the same tags declare what a symbol holds and what a native call returns.
/// </remarks>
public enum QvmValueKind
{
    /// <summary>No declared type. A native registered without one returns its integer slot.</summary>
    None = 0,

    /// <summary>A 32-bit integer.</summary>
    Integer = 1,

    /// <summary>A floating-point number: 32-bit on the stack, 64-bit in a variable.</summary>
    Float = 2,

    /// <summary>A string constant, stored inline on the stack.</summary>
    String = 3,

    /// <summary>An identifier that has not been resolved to a symbol yet, stored inline on the stack.</summary>
    Identifier = 4,

    /// <summary>An address immediate, pushed only by <see cref="Qvm.QvmOpcode.PushA"/>.</summary>
    Address = 5,

    /// <summary>A resolved symbol.</summary>
    Symbol = 6,

    /// <summary>
    /// A second inline-string tag the original pops like <see cref="String"/>. Its VM never pushes
    /// one; it belongs to the compiler front end the retail executable also ships.
    /// </summary>
    Buffer = 7,
}

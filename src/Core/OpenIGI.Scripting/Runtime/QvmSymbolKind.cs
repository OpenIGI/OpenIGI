namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// What a registered symbol is, and therefore how the VM may use it.
/// </summary>
/// <remarks>The numeric values are the original's, stored in the first word of its symbol descriptor.</remarks>
public enum QvmSymbolKind
{
    /// <summary>A variable: readable, and the only thing an assignment may target.</summary>
    Variable = 0,

    /// <summary>A native function: the only thing a call may target.</summary>
    Function = 1,

    /// <summary>A named integer constant: readable, never assignable.</summary>
    Constant = 2,
}

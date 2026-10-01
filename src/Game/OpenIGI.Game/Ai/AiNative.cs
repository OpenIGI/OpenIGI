namespace OpenIGI.Game.Ai;

/// <summary>What kind of value an AI native hands back to the script.</summary>
/// <remarks>
/// The registrar picks one of three wrappers, each passing a different tag to the common
/// <c>Script_RegisterNative</c> (<c>0x4BAAC0</c>): <c>0x4B8890</c> passes 0, <c>0x4B88C0</c> passes 1 and
/// <c>0x4B88F0</c> passes 2. <b>The VM reads tags 0 and 1 identically</b> — its rule is "3 is a string, 2 is a
/// float, anything else is an integer" (see <c>script-vm.md</c>) — so the split between
/// <see cref="Void"/> and <see cref="Int"/> is the engine author documenting intent, not a type the
/// interpreter enforces. It is kept because it separates the eighteen actions from the getters exactly.
/// </remarks>
public enum AiNativeReturn
{
    /// <summary>Registered with tag 0 — a command. Every <c>AIAction_*</c> and every setter.</summary>
    Void = 0,

    /// <summary>Registered with tag 1 — an integer getter.</summary>
    Int = 1,

    /// <summary>Registered with tag 2 — a real getter.</summary>
    Real = 2,
}

/// <summary>One entry of the AI script API.</summary>
/// <param name="Name">The name a script calls it by.</param>
/// <param name="ArgumentCount">How many arguments the registrar declares.</param>
/// <param name="Return">What it hands back.</param>
/// <param name="Address">The handler's address in the original, for the RE trail.</param>
/// <remarks>
/// An argument count is a <b>declaration</b>, and M2 established that the VM's arity check is not a
/// hard gate — a script that disagrees is a wire-arity error, not a value-count one. It is recorded here
/// because it is how the eighteen actions are told apart from one another at a call site.
/// </remarks>
public readonly record struct AiNative(string Name, int ArgumentCount, AiNativeReturn Return, uint Address)
{
    /// <summary>Gets a value indicating whether this is one of the eighteen <c>AIAction_*</c> entries.</summary>
    public bool IsAction => Name.StartsWith("AIAction_", StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => $"{Name}/{ArgumentCount} -> {Return} (0x{Address:X})";
}

namespace OpenIGI.Formats.Ai;

/// <summary>
/// What a nav-graph node is for, as a bit set. A node's criteria is stored in the file as the
/// <em>constant's name</em>; the engine resolves that name to a bit at load time.
/// </summary>
/// <remarks>
/// <para>
/// The values are not written down anywhere in the binary. <c>0x5186C0</c> registers a criteria name into
/// the next free slot of a 32-entry table and returns <c>1 &lt;&lt; slot</c>, so the numbering is
/// <b>registration order</b>. <c>AIGraph</c>'s registrar (<c>0x508F80</c>) registers DOOR, then VIEW, then
/// STAIR — giving 1, 2, 4.
/// </para>
/// <para>
/// Because it is a bit set, a node could in principle carry several. None does: the file stores a single
/// name, and across all 300 retail graphs only four values occur — the empty string 7022 times,
/// <c>NODECRITERIA_STAIR</c> 669, <c>NODECRITERIA_DOOR</c> 465 and <c>NODECRITERIA_VIEW</c> 161.
/// </para>
/// <para>
/// <see cref="View"/> is the one the engine tests on a hot path: the nearest-node query
/// (<c>0x5096F0</c>) <b>skips every node carrying it</b>, so a view node can never be chosen as the
/// start or end of a walk. They are observation posts, not path nodes.
/// </para>
/// </remarks>
[Flags]
public enum GraphNodeCriteria
{
    /// <summary>An ordinary path node — the empty criteria string.</summary>
    None = 0,

    /// <summary>A doorway. Registered first, so bit 0.</summary>
    Door = 1,

    /// <summary>An observation post. Registered second, so bit 1. Excluded from nearest-node queries.</summary>
    View = 2,

    /// <summary>A staircase. Registered third, so bit 2.</summary>
    Stair = 4,
}

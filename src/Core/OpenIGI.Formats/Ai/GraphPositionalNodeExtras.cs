namespace OpenIGI.Formats.Ai;

/// <summary>
/// The two pieces of per-node data only the positional variant of a graph file carries, both of which the
/// original reads and throws away.
/// </summary>
/// <param name="NodeId">The node these belong to.</param>
/// <param name="ExtraValue">
/// An optional single-precision value between the radius and the material. Present when the next tag is
/// <see cref="GraphValueType.Real32"/>. <c>0x4FA160</c> reads it into a local that it then reuses as a link
/// id, so the value never reaches the node.
/// </param>
/// <param name="FirstList">The first of the hook's two counted lists of integer pairs.</param>
/// <param name="SecondList">The second.</param>
/// <remarks>
/// <para>
/// The lists come from <c>0x509480</c>, the per-node message the positional loader sends the graph's own
/// class so a subclass can read its own data inline. <c>AIGraph</c>'s handler reads
/// <c>{count, count × (i32, i32)}</c> twice and stores <b>none</b> of it — both integers of every pair go
/// into the same local. So this is data the shipped engine parses only to advance the cursor.
/// </para>
/// <para>
/// It is not always empty: of the 130 positional nodes in the retail corpus, 41 carry at least one pair,
/// with list lengths up to three. Retained here rather than skipped, because a format this thoroughly
/// discarded is exactly the kind a mod or a level editor would put meaning back into.
/// </para>
/// <para>
/// Whether <see cref="ExtraValue"/> is present is a property of the <em>file</em>, not the node: of the
/// seven positional files, six have it on every node and one (level 8's <c>graph22.dat</c>, 65 nodes) on
/// none. That reads as two exporter versions rather than an optional field.
/// </para>
/// </remarks>
public readonly record struct GraphPositionalNodeExtras(
    int NodeId,
    float? ExtraValue,
    IReadOnlyList<(int First, int Second)> FirstList,
    IReadOnlyList<(int First, int Second)> SecondList);

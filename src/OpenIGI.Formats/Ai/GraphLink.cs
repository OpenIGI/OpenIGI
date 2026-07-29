namespace OpenIGI.Formats.Ai;

/// <summary>An undirected edge between two nav-graph nodes.</summary>
/// <param name="NodeA">The first node's id.</param>
/// <param name="NodeB">The second node's id.</param>
/// <param name="LinkType">The edge's type.</param>
/// <remarks>
/// <para>
/// A link is created the moment its <c>eLinkType</c> field arrives (<c>0x4F9B80</c>), and it is registered
/// on <b>both</b> endpoints — the engine keeps one 24-byte record and puts a reference to it on each node's
/// list. So there is nothing directed about it, which is why the route table's reachability is symmetric
/// in every retail graph.
/// </para>
/// <para>
/// <see cref="LinkType"/> is <b>1 for every one of the 24433 links</b> in the retail corpus. The field is
/// read as a plain integer and stored; no consumer that reaches it through the graph API branches on it.
/// It is carried rather than dropped because a mod could use it.
/// </para>
/// </remarks>
public readonly record struct GraphLink(int NodeA, int NodeB, int LinkType)
{
    /// <summary>Gets the id at the other end of this link.</summary>
    /// <param name="from">One of the two endpoints.</param>
    /// <returns>The other endpoint.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> is not an endpoint.</exception>
    public int Other(int from) => from == NodeA ? NodeB
        : from == NodeB ? NodeA
        : throw new ArgumentOutOfRangeException(nameof(from), from, "Not an endpoint of this link.");
}

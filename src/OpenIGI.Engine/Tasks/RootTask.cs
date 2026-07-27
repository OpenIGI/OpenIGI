namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A task with no behaviour at all: it neither acts on a tick nor passes the tick to its children.
/// </summary>
/// <remarks>
/// <para>
/// The original's root type is registered as <c>"QTask"</c> with an instance size of 32 — the bare
/// header — and everything else descends from it.
/// </para>
/// <para>
/// This is also the right shape for <c>Static</c> and <c>Idle</c>, whose update methods are the shared
/// do-nothing stub (<c>nullsub_2</c> at <c>0x4EE970</c>). Because the update message propagates one
/// level at a time, that stub freezes their whole subtree — which is the point: static level geometry
/// costs nothing per tick. A group that <em>should</em> keep ticking is a <see cref="ContainerTask"/>.
/// </para>
/// </remarks>
public sealed class RootTask : GameTask
{
}

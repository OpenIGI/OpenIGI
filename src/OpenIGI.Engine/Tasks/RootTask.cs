namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A task with no behaviour of its own, used as a tree root and as a plain container.
/// </summary>
/// <remarks>
/// The original's root type is registered as <c>"QTask"</c> with an instance size of 32 — the bare
/// header — and the level root hangs off it. Retail level data also spawns behaviourless
/// <c>Container</c> tasks purely to group things, which is the same shape.
/// </remarks>
public sealed class RootTask : GameTask
{
}

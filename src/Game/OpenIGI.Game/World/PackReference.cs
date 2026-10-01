namespace OpenIGI.Game.World;

/// <summary>Where one model pack is, and what it is called.</summary>
/// <param name="Directory">The resource directory it is opened from, with its trailing slash.</param>
/// <param name="Name">Its name, without the <c>.mtp</c> extension.</param>
/// <remarks>
/// <para>
/// The two together because a pack's archives are addressed <em>relative to the directory it was opened
/// from</em>: the original calls <c>MTP_SetRootDir</c> before each <c>MTP_Load</c>, and opening a pack
/// against the wrong directory silently binds another pack's archives rather than failing.
/// </para>
/// <para>
/// A list of these is a mission's load order, and order is behaviour — a model name that appears in more
/// than one pack resolves to the first that holds it.
/// </para>
/// </remarks>
public readonly record struct PackReference(string Directory, string Name)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Directory}{Name}.mtp";
}

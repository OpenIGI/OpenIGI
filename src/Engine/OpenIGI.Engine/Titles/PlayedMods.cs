using OpenIGI.Core.Content;

namespace OpenIGI.Engine.Titles;

/// <summary>The mods a session plays with that change what happens in play, which its saves record.</summary>
/// <param name="Mods">The mods, in their order.</param>
public sealed record PlayedMods(IReadOnlyList<ModReference> Mods)
{
    /// <summary>Gets a session played with none.</summary>
    public static PlayedMods None { get; } = new([]);
}

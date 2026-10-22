using OpenIGI.Core.Content;

namespace OpenIGI.Modding;

/// <summary>One mod as found: its manifest, where it lives, and its code when it has any.</summary>
/// <param name="Manifest">The mod's manifest.</param>
/// <param name="Directory">The mod's directory.</param>
/// <param name="Code">The mod's code, or <see langword="null"/> for a mod made of content alone.</param>
public sealed record InstalledMod(ModManifest Manifest, string Directory, IOpenIgiMod? Code);

namespace OpenIGI.Engine.Resources;

/// <summary>One folder of overrides in a <see cref="ResourceOverrides"/> stack.</summary>
/// <param name="Id">A stable identity, used to switch the layer on and off by name.</param>
/// <param name="Name">What a person calls it, for the console and the log.</param>
/// <param name="Root">The folder, or <see langword="null"/> for a layer that has none.</param>
/// <param name="Enabled">Whether the layer takes part in resolution.</param>
/// <remarks>
/// A layer is a content pack, the game folder's own <c>overrides</c> directory, or a folder named on the
/// command line. They all mean the same thing: a tree that mirrors the installation. Which one wins a
/// path is decided by where the layer sits in the stack, never by what kind it is.
/// </remarks>
public sealed record OverrideLayer(string Id, string Name, string? Root, bool Enabled = true);

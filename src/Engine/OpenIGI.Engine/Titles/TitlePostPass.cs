namespace OpenIGI.Engine.Titles;

/// <summary>A pass a title's mod runs over the lit scene before the tone map.</summary>
/// <param name="Name">The pass's name, unique among the title's: the mod's id and its own name.</param>
/// <param name="ShadersDirectory">The mod's compiled shaders.</param>
/// <param name="Module">The module the pass runs, among them.</param>
/// <param name="Toggle">The setting that switches it on, or <see langword="null"/> for a pass that is always on.</param>
/// <param name="Parameters">The settings whose values it reads, in order.</param>
public sealed record TitlePostPass(string Name, string ShadersDirectory, string Module, string? Toggle, IReadOnlyList<string> Parameters);

using OpenIGI.Core.Graphics;

namespace OpenIGI.Engine.Titles;

/// <summary>A pass a title's mod runs over the picture, before the tone map or after it.</summary>
/// <param name="Name">The pass's name, unique among the title's: the mod's id and its own name.</param>
/// <param name="ShadersDirectory">The mod's compiled shaders.</param>
/// <param name="Module">The module the pass runs, among them.</param>
/// <param name="Toggle">The setting that switches it on, or <see langword="null"/> for a pass that is always on.</param>
/// <param name="Parameters">The settings whose values it reads, in order.</param>
/// <param name="Stage">Where it runs: over the lit scene, or over the finished picture.</param>
public sealed record TitlePostPass(
    string Name,
    string ShadersDirectory,
    string Module,
    string? Toggle,
    IReadOnlyList<string> Parameters,
    PostPassStage Stage = PostPassStage.Scene);

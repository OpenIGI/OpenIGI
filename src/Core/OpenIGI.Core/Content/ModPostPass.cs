namespace OpenIGI.Core.Content;

/// <summary>A pass a mod runs over the lit scene before the tone map: one of its compiled shader modules.</summary>
/// <param name="Name">The pass's name.</param>
/// <param name="Module">The module, compiled into the mod's <c>shaders/</c> folder.</param>
public sealed record ModPostPass(string Name, string Module)
{
    /// <summary>Gets the mod's setting that switches the pass on, or <see langword="null"/> for a pass that is always on.</summary>
    public string? Toggle { get; init; }

    /// <summary>Gets the mod's settings whose values the pass reads, up to sixteen, in the order it reads them.</summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];
}

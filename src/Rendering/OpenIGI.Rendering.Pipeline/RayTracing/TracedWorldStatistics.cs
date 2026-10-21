namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>What the traced world holds, as a line a player or a bug report can read.</summary>
/// <param name="Geometries">How many geometries the world's structures were built from.</param>
/// <param name="Skinned">How many of those are skinned, refitted as they move.</param>
/// <param name="Casters">How many are terrain, which enters the world by its own door.</param>
/// <param name="Placements">How many placements the top level holds.</param>
/// <param name="AlbedoLayers">How many textures a ray can shade a hit from.</param>
/// <param name="AlbedoExhausted">Whether the albedo array ever turned a texture away for want of a layer.</param>
/// <remarks>
/// Every way the world goes wrong is a silence rather than an error: with no skinned geometry rays pass
/// through every soldier, with no terrain a floor reflects sky, with no albedo every hit shades a flat grey.
/// Nothing in the picture tells those apart from a world that works and has nothing in front of it, which
/// is why the counts are said out loud.
/// </remarks>
public readonly record struct TracedWorldStatistics(
    int Geometries,
    int Skinned,
    int Casters,
    int Placements,
    int AlbedoLayers,
    bool AlbedoExhausted)
{
    /// <summary>Describes the world in one line.</summary>
    /// <returns>The counts, and the albedo array's exhaustion where it happened.</returns>
    public override string ToString() =>
        $"{Geometries} geometry(ies), {Skinned} skinned, {Casters} terrain caster(s), {Placements} placement(s), "
        + $"{AlbedoLayers} albedo layer(s)"
        + (AlbedoExhausted
            ? " (EXHAUSTED -- geometry admitted after that shades from the frame and the cards only)"
            : string.Empty);
}

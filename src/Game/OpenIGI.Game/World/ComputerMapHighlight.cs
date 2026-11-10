using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>A place a mission's computer marks on its map, whichever game's mission it is.</summary>
/// <remarks>
/// Beside the objectives rather than beside a renderer, because both what lists the marks -- a level's catalogue, or a title's
/// player's own computer -- and what draws them name it.
/// </remarks>
/// <param name="Position">Where it is, in engine units.</param>
/// <param name="TitleResource">The string resource of the words on its label; empty for none, or where <see cref="Title"/> has them.</param>
/// <param name="Sprite">The file name of the symbol the level gave it; empty for none.</param>
public readonly record struct ComputerMapHighlight(Vector3d Position, string TitleResource, string Sprite)
{
    /// <summary>Gets the words on the label where whoever listed the mark already has them, in place of the resource's lookup.</summary>
    public string? Title { get; init; }
}

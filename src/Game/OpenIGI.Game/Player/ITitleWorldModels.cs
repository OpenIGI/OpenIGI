using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>A title player whose level holds models of the title's own for the host to draw in the world: the weapons lying about.</summary>
/// <remarks>
/// Optional beside <see cref="ITitlePlayer"/>, as <see cref="ITitleFirstPerson"/> is: the host draws the list among the
/// world's own geometry through the title's presenter, which knows the title's models. A model leaves the list when the
/// title says it is gone, a weapon picked up.
/// </remarks>
public interface ITitleWorldModels
{
    /// <summary>Gets the models as of the last tick.</summary>
    IReadOnlyList<TitleWorldModel> WorldModels { get; }
}

/// <summary>One rigid model a title player's level draws in the world.</summary>
/// <param name="Model">The model's name, as the title's packs resolve it.</param>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Orientation">Its orientation, as the world's placements carry theirs.</param>
public sealed record TitleWorldModel(string Model, Vector3d Position, Matrix3f Orientation);

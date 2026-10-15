using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

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

    /// <summary>Gets depth-tested light sprites as of the last tick.</summary>
    IReadOnlyList<TitleWorldSprite> WorldSprites => [];
}

/// <summary>A camera-facing world sprite, with its position and half-size in world units.</summary>
/// <param name="Sprite">The title's sprite resource name.</param>
/// <param name="Position">The world position.</param>
/// <param name="HalfSize">Half the width and height.</param>
/// <param name="Colour">The modulating colour and opacity.</param>
public sealed record TitleWorldSprite(string Sprite, Vector3d Position, float HalfSize, OpenIGI.Core.Graphics.Bgra32 Colour);

/// <summary>One model a title player's level draws in the world: rigid, or a skinned body posed by its skeleton.</summary>
/// <param name="Model">The model's name, as the title's packs resolve it.</param>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Orientation">Its orientation, as the world's placements carry theirs.</param>
/// <param name="Pose">The skeleton a skinned model is posed by, in the model's frame and units; null for a rigid one.</param>
/// <param name="Instance">Which body a skinned model is, so bodies of one model keep a mesh each; null for a rigid one.</param>
/// <param name="Tick">The tick a skinned model's pose is for: a body is skinned once a tick.</param>
public sealed record TitleWorldModel(
    string Model,
    Vector3d Position,
    Matrix3f Orientation,
    BonePose? Pose = null,
    object? Instance = null,
    long Tick = 0);

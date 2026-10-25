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

    /// <summary>
    /// Gets the models as of the last tick before the title's view culling took any out: what an outline drawn through walls --
    /// the binoculars' marks -- is drawn over. The same as <see cref="WorldModels"/> for a title that culls nothing.
    /// </summary>
    IReadOnlyList<TitleWorldModel> OutlineModels => WorldModels;

    /// <summary>Gets depth-tested light sprites as of the last tick.</summary>
    IReadOnlyList<TitleWorldSprite> WorldSprites => [];

    /// <summary>Gets the marks lying on the ground as of the last tick: shadows and pools of blood.</summary>
    IReadOnlyList<TitleGroundDecal> GroundDecals => [];

    /// <summary>Gets the level's 2-D particles -- blood, dust, smoke -- in the engine both titles share; none when it has none.</summary>
    OpenIGI.Game.World.Particles.Particle2DModule? Particles => null;
}

/// <summary>A square laid on the ground, blended over it.</summary>
/// <param name="Texture">The resource name of its texture.</param>
/// <param name="Position">Where it lies, in world units.</param>
/// <param name="Normal">The ground's normal there, of unit length.</param>
/// <param name="HalfSize">Half its width, in world units.</param>
/// <param name="Hover">How far above the ground it is drawn, in world units.</param>
/// <param name="Colour">The colour its texture is multiplied by; the texture's alpha is multiplied by the colour's.</param>
public sealed record TitleGroundDecal(string Texture, Vector3d Position, Vector3f Normal, float HalfSize, float Hover, OpenIGI.Core.Graphics.Bgra32 Colour);

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
/// <param name="Instance">
/// Which thing the model is, the same from tick to tick: bodies of one model keep a mesh each, and a model named so is drawn
/// between where it was a tick ago and where it is. Null for a model drawn only where the last tick left it.
/// </param>
/// <param name="Tick">The tick the model is for: a body is skinned once a tick, and a named model moves once a tick.</param>
public sealed record TitleWorldModel(
    string Model,
    Vector3d Position,
    Matrix3f Orientation,
    BonePose? Pose = null,
    object? Instance = null,
    long Tick = 0)
{
    /// <summary>Gets the flashes at a rigid model's muzzle, its third magic vertex, or <see langword="null"/> for none.</summary>
    public IReadOnlyList<TitleMuzzleFlash>? MuzzleFlashes { get; init; }
}

/// <summary>
/// A flash at a weapon's muzzle: a quad across the barrel seen when looking down it, and a quad along the barrel turned to the eye
/// seen from the side, both added to what is behind them.
/// </summary>
/// <param name="FrontTexture">The resource name of the texture across the barrel.</param>
/// <param name="SideTexture">The resource name of the texture along the barrel, or <see langword="null"/> for none.</param>
/// <param name="Size">The flash's size: the quads' half-width in world units over 4096 (5120 across the barrel in first person).</param>
/// <param name="Intensity">Its strength, nought to one.</param>
public sealed record TitleMuzzleFlash(string FrontTexture, string? SideTexture, float Size, float Intensity);

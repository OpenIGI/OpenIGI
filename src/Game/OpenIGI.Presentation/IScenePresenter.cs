using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What a title needs in order to build the things standing in its world.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="LevelRoot">The level's task tree, which is what declares them.</param>
/// <param name="Resources">The registry the level's own files are read from.</param>
/// <param name="Models">
/// Resolves a model by the name a level refers to it by, or returns <see langword="null"/> when there is
/// none. A host that has not opened the mission's model packs supplies a lookup that always declines,
/// and the passes that need geometry then build nothing rather than half of something.
/// </param>
/// <param name="MaterialTextures">
/// Resolves a model name and one of its material slots to the texture that slot samples, or
/// <see langword="null"/> when nothing answers. A mesh alone cannot say: a material belongs to the
/// instance that uses the mesh, and only the packs know which instance that is.
/// </param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <remarks>
/// <para>
/// <b>The model lookup is the whole reason this is a separate request.</b> A ground reads its own height
/// fields out of an archive and a sky builds its dome from nothing, so
/// <see cref="GroundPresentationRequest"/> needs the registry and no more. A forest does not: it plants
/// instances of a <em>model</em>, thousands of them, and the model lives in a pack the mission opened
/// rather than in the level's own files. Widening the ground's request to carry a lookup it never uses
/// would have made every title implementing a ground answer for one it does not need.
/// </para>
/// <para>
/// A delegate rather than the pack list itself, because the two games disagree about packs — how many,
/// in what order, and which wins a name that appears in more than one — and none of that disagreement
/// belongs above the seam. What both agree on is that a name resolves to a mesh, or does not.
/// </para>
/// </remarks>
public readonly record struct ScenePresentationRequest(
    IGraphicsDevice Device,
    GameTask LevelRoot,
    ResourceRegistry Resources,
    Func<string, MefModel?> Models,
    Func<string, int, string?> MaterialTextures,
    IOpenIgiLogger Logger);

/// <summary>What one frame needs in order to draw a title's foliage.</summary>
/// <param name="Camera">Where the trees are seen from.</param>
/// <param name="HorizontalHalfTangent">The tangent of half the horizontal field of view.</param>
/// <param name="VerticalHalfTangent">The tangent of half the vertical field of view.</param>
/// <remarks>
/// Half-tangents rather than an angle and an aspect ratio, because that is the form the culling wants:
/// the planes a forest tests its stands against are built straight out of them, and converting an angle
/// back into a tangent at every plane is arithmetic in the wrong place.
/// </remarks>
public readonly record struct ForestFrame(
    Camera3D Camera,
    float HorizontalHalfTangent,
    float VerticalHalfTangent);

/// <summary>What one frame needs in order to draw a title's water.</summary>
/// <param name="Camera">Where the water is seen from.</param>
/// <param name="Projection">The view-to-clip transform.</param>
/// <param name="Seconds">The level's elapsed time, which the waves travel on.</param>
public readonly record struct WaterFrame(Camera3D Camera, Matrix4f Projection, double Seconds);

/// <summary>Draws a title's planted foliage.</summary>
/// <remarks>
/// A pass of its own rather than part of the world, because a forest is not placements: one game's is
/// tens of thousands of instances of a handful of models, described by a stand and a seed rather than
/// one by one, and culled by a structure built for them alone. The other game has no such thing and
/// returns no presenter, which is the interface working as intended.
/// </remarks>
public interface IForestPresenter : IDisposable
{
    /// <summary>Gets how many trees the last frame drew.</summary>
    int VisibleTreeCount { get; }

    /// <summary>Gets a one-line summary for an entity list.</summary>
    string Summary { get; }

    /// <summary>Draws the foliage.</summary>
    /// <param name="frame">The frame to draw for.</param>
    void Draw(in ForestFrame frame);
}

/// <summary>Draws a title's water.</summary>
/// <remarks>
/// Separate from the ground for the same reason the forest is separate from the world: water is a
/// surface that moves, and every frame of it is a fresh mesh rather than a static one re-transformed.
/// </remarks>
public interface IWaterPresenter : IDisposable
{
    /// <summary>Gets how many sheets of water the level places.</summary>
    int SheetCount { get; }

    /// <summary>Gets a one-line summary for an entity list.</summary>
    string Summary { get; }

    /// <summary>Draws the water.</summary>
    /// <param name="frame">The frame to draw for.</param>
    void Draw(in WaterFrame frame);
}

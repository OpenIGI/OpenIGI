using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>How the original's own impact effects are drawn: the dust a round kicks up, the chips it knocks loose, and blood.</summary>
/// <remarks>The title's presentation implements it and registers it for an <see cref="ImpactEffectPresentationRequest"/>.</remarks>
public interface IImpactEffectPresenter : IDisposable
{
    /// <summary>Gets how many frames <c>blood1.spr</c> turned out to have.</summary>
    /// <remarks>
    /// The mist's frame rate is <c>15 / frameCount</c> whole ticks, so the runtime needs this before it
    /// builds a body's systems. Retail ships seven frames, which gives two ticks each.
    /// </remarks>
    int MistFrameCount { get; }

    /// <summary>Gets whether the three retail sprites all resolved.</summary>
    bool SpritesAvailable { get; }

    /// <summary>Gets how many dust quads the last draw emitted.</summary>
    int LastPuffQuads { get; }

    /// <summary>Gets how many blood-mist quads the last draw emitted.</summary>
    int LastMistQuads { get; }

    /// <summary>Gets how many blood-droplet quads the last draw emitted.</summary>
    int LastDropletQuads { get; }

    /// <summary>Binds the impact chip models out of a mission's packs.</summary>
    /// <param name="mission">The loaded mission whose common pack owns them.</param>
    /// <returns>How many of the four models resolved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mission"/> is <see langword="null"/>.</exception>
    int Load(LoadedMission mission);

    /// <summary>Draws every live effect.</summary>
    /// <param name="effects">The runtime holding the particles and chips.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="blend">The frame's position between the last two ticks, 0–1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is <see langword="null"/>.</exception>
    void Draw(
        ImpactEffectRuntime effects,
        in Camera3D camera,
        in Matrix4f projection,
        float blend);
}

/// <summary>What the original's impact effects are drawn with.</summary>
/// <param name="Device">The graphics device to upload through and draw with.</param>
/// <param name="Resources">The level's resources, which hold the impact models and their textures.</param>
/// <param name="FileSystem">The game's files, which hold the effect sprites.</param>
public readonly record struct ImpactEffectPresentationRequest(
    IGraphicsDevice Device,
    ResourceRegistry Resources,
    VirtualFileSystem? FileSystem);

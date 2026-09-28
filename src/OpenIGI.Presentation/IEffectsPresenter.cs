using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>Draws the marks a title's rounds leave on the world.</summary>
/// <remarks>
/// The marks themselves are shared: <see cref="BulletMarks"/> is a ring of impacts the ballistics
/// produce, and any title fills it the same way. What is not shared is how one is drawn — the decal
/// geometry, the depth bias that keeps it off the surface it sits on, and the texture it wears — which
/// is why this is a seam rather than a shared renderer.
/// </remarks>
public interface IBulletMarkPresenter : IDisposable
{
    /// <summary>Draws every live mark.</summary>
    /// <param name="marks">The ring of impacts to draw.</param>
    /// <param name="camera">Where they are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <param name="poses">
    /// Where placements are drawn this frame, so a mark held in one's frame moves with it; null draws
    /// every mark where it was made.
    /// </param>
    void Draw(BulletMarks marks, in Camera3D camera, in Matrix4f projection, PlacementPose? poses = null);
}

/// <summary>Draws the lamp on each of a level's security cameras that shows what it is doing.</summary>
/// <remarks>
/// A sprite billboard per camera, coloured by the camera's state. The implementation is title content
/// because the sprite it loads is named by an alias path belonging to one game, and a shared renderer
/// may not know one — even though <see cref="SecurityCamera"/> itself is shared. The architecture test
/// enforces exactly that, and caught this remark quoting the path when it was first written.
/// </remarks>
public interface ICameraStatusLightPresenter : IDisposable
{
    /// <summary>Gets whether the sprite loaded and the lamps can be drawn.</summary>
    bool IsReady { get; }

    /// <summary>Loads the sprite the lamps are drawn with.</summary>
    /// <param name="fileSystem">The file system the sprite archive is read through.</param>
    /// <returns><see langword="false"/> when the installation ships no such sprite.</returns>
    bool Load(VirtualFileSystem fileSystem);

    /// <summary>Draws one lamp per camera.</summary>
    /// <param name="cameras">The level's security cameras.</param>
    /// <param name="eye">Where the lamps are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <param name="tickFraction">How far the frame sits between the previous tick and the next.</param>
    /// <param name="smooth">Whether to interpolate a camera still turning between ticks.</param>
    void Draw(
        IReadOnlyList<SecurityCamera> cameras,
        in Camera3D eye,
        in Matrix4f projection,
        float tickFraction = 1f,
        bool smooth = false);
}

/// <summary>What a title needs in order to build the small world-effect presenters.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="Resources">The registry textures are resolved through.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <remarks>
/// The camera lamps take no resource registry of their own — they read a sprite through the virtual
/// file system when <see cref="ICameraStatusLightPresenter.Load"/> runs, which is later than
/// construction — so they ignore <see cref="Resources"/>. One request for two presenters rather than two
/// requests differing by a field one of them never reads.
/// </remarks>
public readonly record struct EffectsPresentationRequest(
    IGraphicsDevice Device,
    ResourceRegistry Resources,
    IIgiLogger Logger);

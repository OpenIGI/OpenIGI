using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What a title needs in order to read and build its own sky.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="LevelRoot">The level's task tree, which is where the sky is declared.</param>
/// <param name="FileSystem">Where a cloud layer's texture is read from.</param>
/// <param name="Resources">The registry the level's own files are read from.</param>
/// <param name="FarPlane">The projection's far plane, which one title sizes its dome to.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <remarks>
/// Both a file system and a registry, because the two games fetch a cloud texture differently: one
/// layer names a path its virtual file system resolves directly, and the other names a bare file the
/// engine rebases into the mission's own texture directory and reads through the registry. Handing over
/// both and letting each title take the one it uses is smaller than an abstraction over "somewhere a
/// texture comes from", and it keeps the alias vocabulary on the titles' side of the seam where the
/// layering rules put it.
/// </remarks>
public readonly record struct SkyPresentationRequest(
    IGraphicsDevice Device,
    GameTask LevelRoot,
    VirtualFileSystem FileSystem,
    ResourceRegistry Resources,
    float FarPlane,
    IOpenIgiLogger Logger);

/// <summary>What one frame needs in order to draw a title's sky.</summary>
/// <param name="Camera">The camera the sky is drawn about.</param>
/// <param name="Projection">The view-to-clip transform.</param>
/// <param name="Seconds">The level's elapsed time, which scrolls the clouds.</param>
/// <remarks>
/// Seconds rather than ticks, and that is a compromise worth naming. Both games advance a cloud layer's
/// scroll <em>per simulation tick</em> — I.G.I. 2's <c>FlatSkyLayer_RunHandler</c> is read and does
/// exactly that — so a tick count is what a presenter really wants. The host's own clock is a wall
/// clock, and giving the seam a tick count it would have to invent would move the guess rather than
/// remove it. A presenter that knows its title's tick rate converts; one that does not says so.
/// </remarks>
public readonly record struct SkyFrame(Camera3D Camera, Matrix4f Projection, float Seconds);

/// <summary>Draws a title's sky: whatever backdrop stands behind the world.</summary>
/// <remarks>
/// <para>
/// An interface because the host was building one game's sky by name. <c>SkyRenderer</c> finds its
/// <c>FlatSky</c> by walking the tree for a <c>DeclaredTask</c> called that, which is how a title with
/// no class for the type carries it — and I.G.I. 2 <em>has</em> a class for it, so the same walk finds
/// nothing and the sequel drew no sky at all. The two also disagree about where a layer's texture comes
/// from and about how large the dome is. None of that is the host's business.
/// </para>
/// <para>
/// The properties are the ones a frame needs from a sky that are not drawing: the fog colour is what the
/// frame clears to, so the horizon meets sky-coloured backing rather than black, and the fog amount and
/// plane height seed the volumetric fog. They are on the interface rather than behind a describe method
/// because the host acts on them, unlike <see cref="ITerrainPresenter.Describe"/>'s counters, which it
/// only prints.
/// </para>
/// </remarks>
public interface ISkyPresenter : IDisposable
{
    /// <summary>Gets a value indicating whether the level declared a sky at all.</summary>
    bool HasSky { get; }

    /// <summary>Gets the colour the frame should clear to.</summary>
    Vector3f FogColour { get; }

    /// <summary>Gets the level's authored fog amount.</summary>
    float FogAmount { get; }

    /// <summary>Gets how far above the camera the cloud plane sits, in the title's own position units.</summary>
    float PlaneHeight { get; }

    /// <summary>Gets whether this sky is the game's own painted one, to be kept rather than replaced.</summary>
    /// <remarks>
    /// <para>
    /// True for a sky whose colours are the ones the artist wanted on the screen and whose mood is
    /// authored per mission. A deferred frame otherwise substitutes its own atmosphere wherever no
    /// geometry wrote depth, which a sky dome never does — and an atmosphere cannot be a night sky for
    /// a game that authors night in the sun's COLOUR rather than its height.
    /// </para>
    /// <para>
    /// Defaulted to FALSE rather than true, though both games' skies are painted. Project I.G.I.'s
    /// deferred picture is a regression gate and its atmosphere is deliberate — the remaster replaces
    /// that game's flat gradient on purpose. Turning this on for it is its own decision, not a side
    /// effect of the sequel asking.
    /// </para>
    /// </remarks>
    bool IsGameOwnSky => false;

    /// <summary>Draws the sky behind everything.</summary>
    /// <param name="frame">The frame to draw for.</param>
    void Draw(in SkyFrame frame);

    /// <summary>Tints the sky to the sun's warmth.</summary>
    /// <param name="tint">A per-channel multiplier, one for the authored sky, warmer as the sun sinks.</param>
    /// <remarks>
    /// Defaulted to doing nothing, because it is a remaster affordance rather than something either
    /// original does: the sun tint is what carries a golden dusk through a window and into the
    /// screen-space bounce. A vanilla-accurate sky is entitled to ignore it.
    /// </remarks>
    void SetSunTint(Vector3f tint)
    {
    }
}

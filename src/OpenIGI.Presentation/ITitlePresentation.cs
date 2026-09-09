using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>Draws a title's static world geometry.</summary>
/// <remarks>
/// The host owns the frame and the order of its passes; the title owns what each pass draws. That is
/// the original engine's own <c>RenderMode</c> seam moved up one level — the engine always drew through
/// a registered mode, and it always drew <em>this game's</em> world through it.
/// </remarks>
public interface IWorldPresenter
{
    /// <summary>Draws the world's placed objects from one viewpoint.</summary>
    /// <param name="scene">The placed objects to draw.</param>
    /// <param name="camera">Where the world is seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <param name="minimumRadius">
    /// Objects whose bounding radius is below this are skipped. The shadow cascades use it to drop
    /// casters too small to read at their resolution; a main pass passes zero.
    /// </param>
    void Draw(WorldScene scene, in Camera3D camera, in Matrix4f projection, float minimumRadius);
}

/// <summary>Draws a title's skinned characters.</summary>
public interface ICharacterPresenter
{
    /// <summary>Draws every live character from one viewpoint.</summary>
    /// <param name="camera">Where the characters are seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    void Draw(in Camera3D camera, in Matrix4f projection);

    /// <summary>Draws the player's own body, which no character loop holds.</summary>
    /// <param name="camera">Where it is seen from.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <remarks>
    /// The player is not one of the title's characters -- it is the thing they are drawn around -- so it
    /// is never in <see cref="Draw"/>, and a pass that wants every caster has to ask for it separately.
    /// It costs a shadow only while something is showing it: a third-person view, a ladder, a death.
    /// Defaulted to nothing, so a title with no third-person body of its own ignores it.
    /// </remarks>
    void DrawPlayerBody(in Camera3D camera, in Matrix4f projection)
    {
        _ = camera;
        _ = projection;
    }
}

/// <summary>The renderers one title draws its world with.</summary>
/// <remarks>
/// <para>
/// Every presenter is nullable so that a title under construction renders the passes it has and skips
/// the rest, rather than failing the frame. An I.G.I. 2 build with terrain but no characters is a
/// useful build; one that will not present is not.
/// </para>
/// <para>
/// This carries the two passes that have a consumer today — <see cref="Rendering.ShadowCasterPass"/> draws the
/// world and the characters into the sun's cascades, and it is the one place a pass is handed its
/// renderers rather than reaching for them. The view model, the HUD and the menus are drawn by the
/// host against concrete renderers still, and move behind this interface when there is a second
/// implementation to shape them against: an abstraction extracted from one implementation is usually
/// the wrong one, and the cost of widening this later is a method, not a redesign.
/// </para>
/// </remarks>
public interface ITitlePresentation
{
    /// <summary>Gets the presenter for static world geometry, or null when the title has none loaded.</summary>
    IWorldPresenter? World { get; }

    /// <summary>Gets the presenter for skinned characters, or null when the title has none loaded.</summary>
    ICharacterPresenter? Characters { get; }
}

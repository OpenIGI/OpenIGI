using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// A mod's own way of drawing soldiers, set on <see cref="SoldierRenderer.BodyStyle"/>: it makes a body for each soldier it
/// draws, and poses them all once a frame. A soldier it makes no body for is drawn as the game draws him. <b>Not retail.</b>
/// </summary>
/// <remarks>
/// The game's own model goes on being posed under a body (its hit boxes, its level of detail, its corpse), and is simply not
/// drawn. Nothing here is asked while no style is set.
/// </remarks>
public interface ISoldierBodyStyle
{
    /// <summary>Makes the body drawn in a soldier's place, or declines him; asked each tick he has none.</summary>
    /// <param name="soldier">The soldier, as the body will see him.</param>
    /// <returns>His body, or <see langword="null"/> to leave him to the game.</returns>
    ISoldierBody? Create(SoldierBodySoldier soldier);

    /// <summary>Poses the bodies for a frame, before any of them is drawn: the place to skin them all at once. Called on every frame.</summary>
    /// <param name="bodies">Every body the style has made that is still in the level.</param>
    /// <param name="alpha">How far the frame is between the last two ticks, nought to one; one when frames are not smoothed between ticks.</param>
    /// <param name="eye">The eye the last frame was drawn from, in metres, or <see langword="null"/> before the first.</param>
    void Frame(IReadOnlyList<ISoldierBody> bodies, float alpha, Camera3D? eye);
}

/// <summary>One soldier's body as a <see cref="ISoldierBodyStyle"/> made it. Disposed when he leaves the level or the style is changed. <b>Not retail.</b></summary>
public interface ISoldierBody : IDisposable
{
    /// <summary>Gets a value indicating whether the gun in the soldier's hand is left undrawn: the body carries its own.</summary>
    bool HidesHeldWeapon { get; }

    /// <summary>Advances the body by a tick, once the game has posed his own model.</summary>
    void Tick();

    /// <summary>Draws the body, in a pass of the frame: the eye's own, or a shadow's.</summary>
    /// <param name="camera">The pass's camera, in metres.</param>
    /// <param name="isEye">Whether the pass is the eye's, not a shadow's.</param>
    /// <param name="modelView">The soldier's drawn placement into the pass's view.</param>
    void Draw(in Camera3D camera, bool isEye, in Matrix4f modelView);

    /// <summary>Where the gun in his hand is held this tick, when the body says so: the hand's position and turn in his own frame, in engine units.</summary>
    /// <param name="hand">The hand's position.</param>
    /// <param name="rotation">The hand's turn.</param>
    /// <returns>Whether the body says; if not, the game's hand bone does.</returns>
    bool TryGetHeldWeapon(out Vector3f hand, out Matrix3f rotation);

    /// <summary>Casts a round at the body as it was last drawn.</summary>
    /// <param name="origin">Where the round starts, in engine units.</param>
    /// <param name="rayEnd">Where it ends, in engine units.</param>
    /// <param name="hit">Where it met him, when it did.</param>
    /// <returns>Whether it met him; <see langword="null"/> to let his own model answer.</returns>
    bool? TryMeetRound(Vector3d origin, Vector3d rayEnd, out HumanHit hit);

    /// <summary>He has been killed and begins to fall.</summary>
    /// <param name="fall">How the game has him fall.</param>
    void Fell(SoldierFall fall);
}

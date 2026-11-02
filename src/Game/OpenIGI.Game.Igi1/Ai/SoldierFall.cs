using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A soldier falling dead, as its renderer needs it to draw the corpse: where the body lies and how it came down.</summary>
/// <param name="View">The soldier.</param>
/// <param name="Origin">Where the body's root lies, set down on the ground.</param>
/// <param name="Bone">The bone the killing round or blast struck.</param>
/// <param name="Travel">Which way the round came, origin to end.</param>
/// <param name="Category">The killing weapon's category.</param>
/// <param name="Velocity">What the body was already doing: its clip's root motion this tick, out of body space.</param>
/// <param name="DroppedWeapon">Whether it let go of a weapon, which is no longer drawn in its hand.</param>
/// <param name="Heading">
/// Which way the body faced while it was alive, or <see langword="null"/> for the way it faces now. The
/// original turns a body to the round before its canned fall (<c>0x45DAA0</c>); a simulated corpse starts
/// from where it really was, or it snaps round the instant it dies.
/// </param>
/// <param name="Struck">
/// Where the killing round met the body, in world units, or <see langword="null"/> when there is no such
/// point. The struck bone the hit reports is coarse — a round into the upper back can come back as the
/// pelvis — and pushing the hips of a standing man forwards sits him down backwards.
/// </param>
public readonly record struct SoldierFall(
    SoldierView View,
    Vector3d Origin,
    int Bone,
    Vector3d Travel,
    WeaponCategory Category,
    Vector3d Velocity,
    bool DroppedWeapon,
    Matrix3f? Heading = null,
    Vector3d? Struck = null);

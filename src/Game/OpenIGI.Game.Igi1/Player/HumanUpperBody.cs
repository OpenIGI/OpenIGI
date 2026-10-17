using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The torso half of a human's rig — the bone the weapon animations play on, and the bend that makes a
/// body drawn from outside look where its owner is looking.
/// </summary>
/// <remarks>
/// <para>
/// Every human skeleton the game ships (<c>000</c>, <c>001</c>, <c>002</c>, <c>003</c>, <c>006</c>) has
/// the same 33-bone hierarchy, and it groups as: bones 0–3 the pelvis and spine, 4–7 the neck and head
/// off the spine top, 8–13 and 14–19 the two arms off the spine top, <b>20–22 the weapon chain off spine
/// bone 1</b>, and 23–27 and 28–32 the two legs off the root (the original,
/// measured over 535 skinned bodies).
/// </para>
/// <para>
/// That shape is why the split is on <see cref="Bone"/> and could not be anywhere else. Bone 1's subtree
/// is exactly the torso, the head, both arms and the held weapon; everything outside it is the pelvis
/// and the legs. So an upper-body clip on bone 1 and a locomotion clip on bone 0 divide the body along
/// the line the animations were authored for, with no masking and nothing to tune.
/// </para>
/// </remarks>
public static class HumanUpperBody
{
    /// <summary>The bone a weapon's <c>UpperBody*</c> clip plays on.</summary>
    /// <remarks>
    /// The literal <c>1</c> in <c>0x489ED0</c>: <c>sub_4D6130(human+104, 1, 5, human+1616, id, loop,
    /// blend)</c>. Bone, orientation type, orientation source — and the only call to that function in
    /// the whole executable, which is how the upper-body track is known to be this bone and no other.
    /// </remarks>
    public const int Bone = 1;

    /// <summary>The first bone of the neck-and-head chain.</summary>
    /// <remarks>
    /// Bone 4, the head of the four-bone group <c>HumanDamage</c> maps to the neck zone — and the same
    /// bone the proximity test names as "bone 4 of model <c>000_01_1</c>". Bending it moves the head
    /// without moving the arms or the weapon.
    /// </remarks>
    public const int NeckBone = 4;

    /// <summary>How much of the aim pitch the chest takes.</summary>
    /// <remarks>
    /// <b>Not retail.</b> IGI1 never bends a body to an aim — see <see cref="SoldierView.AimAlong"/> for
    /// why that is a measurement rather than an omission. The division between chest and neck is a
    /// remaster choice; it sums to one so the rig ends up looking where its owner is.
    /// </remarks>
    public const float ChestShare = 0.55f;

    /// <summary>How much of the aim pitch the neck takes.</summary>
    public const float HeadShare = 0.45f;

    /// <summary>The most the chest will bend, in radians — 40°.</summary>
    /// <remarks>
    /// The player may look nearly straight up (<c>PlayerBody.PitchLimit</c> is 89°). A spine turned that
    /// far is a bow, not an aim, so the two caps together express about 70° of it and drop the rest —
    /// which is roughly what a person aiming a rifle upward actually does with their back and neck.
    /// </remarks>
    public const float ChestLimitRadians = 0.6981317f;

    /// <summary>The most the neck will bend, in radians — 35°.</summary>
    public const float HeadLimitRadians = 0.61086524f;
}

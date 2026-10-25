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
/// bone 1</b>, and 23–27 and 28–32 the two legs off the root (measured over 535
/// skinned bodies).
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

    /// <summary>The most of a look <see cref="Bone"/> takes before the rest goes to <see cref="NeckBone"/> — 55°.</summary>
    /// <remarks><c>Human_ApplyLook</c> (<c>0x489F70</c>) clamps to this float, <c>0x3F75BE0C</c>.</remarks>
    public const float LookLimitRadians = 0.9599311351776123f;
}

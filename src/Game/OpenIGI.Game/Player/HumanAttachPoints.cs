namespace OpenIGI.Game.Player;

/// <summary>
/// The bones a human hangs things off, read out of the palette offsets its own handlers index.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Gun</c> class allocates five message ids (<c>0x477E30</c>), and <c>Human</c> registers a
/// handler for each (<c>0x45E450</c>). Four of them answer "where is this attachment", and every one is
/// a constant offset into a bone matrix palette:
/// </para>
/// <code>
/// 0x4600C0  first person   *(view->palette + 420)  position   420  = 132*3  + 24   bone 3
///                          *(view->palette + 432)  rotation   432  = 132*3  + 36
///           third person   *(human->palette + 2928)          2928  = 132*22 + 24   bone 22
///                          *(human->palette + 2940)          2940  = 132*22 + 36
/// 0x4601A0                 *(view->palette + 6888)           6888  = 132*52 + 24   bone 52
/// 0x460210                 *(view->palette + 7284)           7284  = 132*55 + 24   bone 55
/// </code>
/// <para>
/// The last two are what makes a reload animate. A weapon's magazine and its moving part are objects of
/// their own, and their render handlers replace their transform with bone 52's and bone 55's on every
/// frame, so they follow the view rig's clip instead of riding the gun body.
/// </para>
/// <para>
/// The arithmetic is the evidence. <b>132</b> is the palette stride the skinning path indexes with, and
/// <b>+24</b> and <b>+36</b> are the world position and the world 3×3 inside a record — so every one of
/// the four offsets factors exactly, with no remainder, into a bone index and a known field. Two of the
/// three view-rig indices also land inside the 56-bone skeleton and outside the 33-bone one, which is an
/// independent check that the first-person handlers really do read the view's palette and not the body's.
/// </para>
/// <para>
/// Each handler adds the object's own <c>double</c> position at <c>+32</c> to the bone's position and
/// copies the bone's rotation whole, so an attachment is <b>the bone's world transform in the object's
/// space, offset by the object's world position</b> — no per-weapon offset anywhere.
/// </para>
/// </remarks>
public static class HumanAttachPoints
{
    /// <summary>The palette stride, in bytes, that the offsets factor by.</summary>
    public const int PaletteStride = 132;

    /// <summary>A palette record's world position, in bytes from the record's start.</summary>
    public const int PositionOffset = 24;

    /// <summary>A palette record's world 3×3 rotation, in bytes from the record's start.</summary>
    public const int RotationOffset = 36;

    /// <summary>The bone of the first-person view rig the held weapon hangs off.</summary>
    /// <remarks>From <c>0x4600C0</c>'s first-person arm — palette <c>+420</c> and <c>+432</c>.</remarks>
    public const int FirstPersonWeapon = 3;

    /// <summary>The bone of the body rig the held weapon hangs off when the player is not the camera.</summary>
    /// <remarks>
    /// From <c>0x4600C0</c>'s other arm — palette <c>+2928</c> and <c>+2940</c> on the human's own
    /// skeleton-0 rig. Worth noting against <c>animation.md</c> §4.3: bone 22 is a <b>weapon hand</b>,
    /// so event <c>1010</c>'s symmetric split between bones 22 and 27 is a pair of hands at least as
    /// readily as a pair of feet.
    /// </remarks>
    public const int ThirdPersonWeapon = 22;

    /// <summary>The body-rig bone HumanAI snapshots as its eye transform.</summary>
    /// <remarks>
    /// IGI1 <c>0x44F410</c> reads palette offsets 684/696; with the 132-byte stride and the
    /// position/rotation fields above, both resolve exactly to bone 5.
    /// </remarks>
    public const int AiEye = 5;

    /// <summary>A second attachment on the view rig, from <c>0x4601A0</c> — palette <c>+6888</c>.</summary>
    /// <remarks>
    /// <para>
    /// The bone a weapon's <b>magazine</b> hangs off. The three classes whose render handler is
    /// <c>0x472880</c> ask the human for this message and replace their whole transform with the
    /// answer: <c>GunClip</c> (<c>TASKTYPE_GUNCLIP</c>), <c>GunCABarrel</c> (<c>TASKTYPE_CABARREL</c>)
    /// and <c>Gunx2</c> (<c>TASKTYPE_GUNX2</c>), registered at <c>0x477220</c>, <c>0x472670</c> and
    /// <c>0x46DD00</c>.
    /// </para>
    /// <para>
    /// The message is the fourth of the five <c>Gun</c> allocates (<c>0x477E30</c>, read back through
    /// <c>0x477C30</c>), and <c>Human</c> answers it with <c>0x4601A0</c>.
    /// </para>
    /// </remarks>
    public const int FirstPersonSecondary = 52;

    /// <summary>A third attachment on the view rig, from <c>0x460210</c> — palette <c>+7284</c>.</summary>
    /// <remarks>
    /// The bone a weapon's <b>moving part</b> — a slide, a bolt, a grenade lever — hangs off.
    /// <c>GunMovingPart</c> (<c>TASKTYPE_GUNMOVINGPART</c>) and <c>GunCAClip</c>
    /// (<c>TASKTYPE_CACLIP</c>) both render through <c>0x477B10</c>, which asks the human for the fifth
    /// <c>Gun</c> message (<c>0x477C40</c>) and is answered by <c>0x460210</c>.
    /// </remarks>
    public const int FirstPersonTertiary = 55;

    /// <summary>Recovers the bone index a palette byte offset addresses.</summary>
    /// <param name="paletteOffset">The offset the original's handler indexes with.</param>
    /// <param name="field">The record field the offset lands on, when it lands on one.</param>
    /// <returns>The bone index, or <c>-1</c> when the offset does not factor.</returns>
    /// <remarks>
    /// Exposed because it is the whole derivation: an offset that factors into a bone and a known field
    /// with no remainder is what makes the reading a measurement rather than an assumption.
    /// </remarks>
    public static int BoneAt(int paletteOffset, out int field)
    {
        field = paletteOffset % PaletteStride;
        if (paletteOffset < 0 || (field != PositionOffset && field != RotationOffset))
        {
            return -1;
        }

        return paletteOffset / PaletteStride;
    }
}

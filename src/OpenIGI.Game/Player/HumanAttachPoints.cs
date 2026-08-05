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
    /// <b>Unnamed.</b> What asks for it has not been traced, so calling it a muzzle or a shell port
    /// would be a guess. The bone index is measured; the meaning is open.
    /// </remarks>
    public const int FirstPersonSecondary = 52;

    /// <summary>A third attachment on the view rig, from <c>0x460210</c> — palette <c>+7284</c>.</summary>
    /// <remarks>Unnamed, for the same reason as <see cref="FirstPersonSecondary"/>.</remarks>
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

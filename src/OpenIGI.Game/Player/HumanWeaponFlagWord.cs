namespace OpenIGI.Game.Player;

/// <summary>
/// The flag word the weapon machine's prologue builds, and which input slot each bit comes from.
/// </summary>
/// <remarks>
/// <para>
/// <c>0x410700</c> rebuilds this every tick from the human's current input frame and one word of the
/// first-person rig. The locomotion prologue <c>0x4105C0</c> maps the <b>same</b> slots onto the
/// <b>same</b> bits, which is what makes the mapping checkable rather than assumed.
/// </para>
/// <para>
/// The mask <c>0xFFF7001C</c> is what survives a rebuild: the ground and death bits, and everything
/// above <see cref="NextWeapon"/>. Every other bit is cleared and re-derived.
/// </para>
/// </remarks>
[Flags]
public enum HumanWeaponFlagWord : uint
{
    /// <summary>Nothing set.</summary>
    None = 0,

    /// <summary>
    /// The clip on bone 0 of the first-person rig has reached its end.
    /// </summary>
    /// <remarks>
    /// From bit <c>0x100</c> of that bone's node flags. <c>sub_4D4B60</c> clears and re-tests it every
    /// advance, so on a looping clip it marks the wrap tick and on a one-shot clip it latches — and
    /// every weapon state waits on a one-shot clip. This is how a state knows its animation is done.
    /// </remarks>
    ClipEnded = 0x1,

    /// <summary>That bone is cross-fading between two sources.</summary>
    /// <remarks>From bit <c>0x10000</c> of the same node flags.</remarks>
    Blending = 0x2,

    /// <summary>On the ground.</summary>
    /// <remarks>
    /// Preserved across the rebuild rather than written by it — the movement code owns this bit, in
    /// both machines.
    /// </remarks>
    OnGround = 0x4,

    /// <summary>Input slot 2, <see cref="PlayerAction.MoveLeft"/>.</summary>
    MoveLeft = 0x20,

    /// <summary>Input slot 3, <see cref="PlayerAction.MoveRight"/>.</summary>
    MoveRight = 0x40,

    /// <summary>Input slot 4, <see cref="PlayerAction.MoveUp"/>.</summary>
    MoveUp = 0x80,

    /// <summary>Input slot 5, <see cref="PlayerAction.MoveDown"/>.</summary>
    MoveDown = 0x100,

    /// <summary>Input slot 6, <see cref="PlayerAction.Fire"/>.</summary>
    /// <remarks>
    /// <b>The trigger.</b> <c>0x40E9C0</c> tests this bit as a level, with no reference to the previous
    /// frame, and that branch is the one that reaches every firing state.
    /// </remarks>
    Fire = 0x200,

    /// <summary>Input slot 7, <see cref="PlayerAction.AlternateFire"/>.</summary>
    AlternateFire = 0x400,

    /// <summary>Input slot 8, <see cref="PlayerAction.Reload"/>.</summary>
    Reload = 0x800,

    /// <summary>Input slot 9, <see cref="PlayerAction.Activate"/>.</summary>
    Activate = 0x1000,

    /// <summary>Input slot 10, <see cref="PlayerAction.Crouch"/>.</summary>
    Crouch = 0x2000,

    /// <summary>Input slot 11, <see cref="PlayerAction.Jump"/>.</summary>
    Jump = 0x4000,

    /// <summary>Input slot 12, <see cref="PlayerAction.Peek"/>.</summary>
    /// <remarks>The one bit the weapon prologue writes and the locomotion prologue does not.</remarks>
    Peek = 0x8000,

    /// <summary>Killed — the first of the two death bits, preserved across a rebuild.</summary>
    Dead = 0x10000,

    /// <summary>Dying — the second death bit, preserved across a rebuild.</summary>
    Dying = 0x20000,

    /// <summary>Input slot 21, <see cref="PlayerAction.NextWeapon"/>.</summary>
    NextWeapon = 0x80000,
}

/// <summary>Builds the weapon machine's flag word, as <c>0x410700</c> does.</summary>
public static class HumanWeaponFlagBuilder
{
    /// <summary>What a rebuild keeps of the previous word.</summary>
    /// <remarks>
    /// <c>0xFFF7001C</c>. Bits 2–4, the two death bits, and everything from <c>0x100000</c> up; note
    /// that <see cref="HumanWeaponFlagWord.NextWeapon"/> is <i>not</i> among them, so it is re-derived
    /// like the rest of the input.
    /// </remarks>
    public const uint PreservedMask = 0xFFF7001Cu;

    /// <summary>The slots the prologue reads, in the order it reads them, paired with the bit each sets.</summary>
    /// <remarks>Slots 2 through 12 run consecutively; slot 21 is the odd one out at the end.</remarks>
    public static IReadOnlyList<(PlayerInputSlot Slot, HumanWeaponFlagWord Flag)> InputBits { get; } =
    [
        (PlayerInputSlot.MoveLeft, HumanWeaponFlagWord.MoveLeft),
        (PlayerInputSlot.MoveRight, HumanWeaponFlagWord.MoveRight),
        (PlayerInputSlot.MoveUp, HumanWeaponFlagWord.MoveUp),
        (PlayerInputSlot.MoveDown, HumanWeaponFlagWord.MoveDown),
        (PlayerInputSlot.Fire, HumanWeaponFlagWord.Fire),
        (PlayerInputSlot.AlternateFire, HumanWeaponFlagWord.AlternateFire),
        (PlayerInputSlot.Reload, HumanWeaponFlagWord.Reload),
        (PlayerInputSlot.Activate, HumanWeaponFlagWord.Activate),
        (PlayerInputSlot.Crouch, HumanWeaponFlagWord.Crouch),
        (PlayerInputSlot.Jump, HumanWeaponFlagWord.Jump),
        (PlayerInputSlot.Peek, HumanWeaponFlagWord.Peek),
        (PlayerInputSlot.NextWeapon, HumanWeaponFlagWord.NextWeapon),
    ];

    /// <summary>Rebuilds the flag word for one tick.</summary>
    /// <param name="previous">Last tick's word, of which <see cref="PreservedMask"/> survives.</param>
    /// <param name="frame">This tick's input frame.</param>
    /// <param name="clipEnded">Whether view bone 0's clip has reached its end.</param>
    /// <param name="blending">Whether view bone 0 is cross-fading.</param>
    /// <returns>The new word.</returns>
    /// <remarks>
    /// A slot counts as pressed when it is <b>not zero</b>, never when it equals one: the sampler
    /// stores the masked bit value rather than a flag, so slot 21 holds <c>0x2000</c> when pressed.
    /// </remarks>
    public static HumanWeaponFlagWord Build(
        HumanWeaponFlagWord previous,
        in PlayerInputFrame frame,
        bool clipEnded,
        bool blending)
    {
        uint flags = (uint)previous & PreservedMask;

        if (clipEnded)
        {
            flags |= (uint)HumanWeaponFlagWord.ClipEnded;
        }

        if (blending)
        {
            flags |= (uint)HumanWeaponFlagWord.Blending;
        }

        foreach ((PlayerInputSlot slot, HumanWeaponFlagWord flag) in InputBits)
        {
            if (frame.IsSet(slot))
            {
                flags |= (uint)flag;
            }
        }

        return (HumanWeaponFlagWord)flags;
    }
}

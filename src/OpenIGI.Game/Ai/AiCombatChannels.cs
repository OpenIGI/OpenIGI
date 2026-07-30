using OpenIGI.Game.Player;

namespace OpenIGI.Game.Ai;

/// <summary>
/// The logical things an AI can ask its body for, and which input-frame slot each one is on for a
/// given <see cref="AiType"/>.
/// </summary>
/// <remarks>
/// <c>sub_48EF70(ai, channel, value)</c> writes <c>value</c> into the input frame at
/// <c>off_54151C[aiType][channel]</c> — ten channels per type — and prints
/// <c>"AI(%d) type(%s) channel(%d)"</c> when the entry is <c>-1</c>. The names come from the debug
/// string on each caller.
/// </remarks>
public enum AiCombatChannel
{
    /// <summary>Stand up. The value is a flag the walking code also drives.</summary>
    Stand = 0,

    /// <summary>Kneel — <c>"Kneel - Roll left"</c>, <c>"Kneel - Fire"</c>, <c>"Kneel - Reload"</c>.</summary>
    Kneel = 1,

    /// <summary>Lie down — <c>"LieDown - Fire"</c>, <c>"LieDown - Reload"</c>, <c>"Fall flat"</c>.</summary>
    LieDown = 2,

    /// <summary>Play the animation the value names — <c>"Playing animation - %d"</c>.</summary>
    /// <remarks>The stun writes <c>38.0</c> here, which is what shows the value is an id and not a flag.</remarks>
    PlayAnimation = 3,

    /// <summary>A second animation slot, written by the walk-to-and-act action.</summary>
    PlayAnimationAlternate = 4,

    /// <summary>Run — <c>"Run panicking"</c>.</summary>
    Run = 5,

    /// <summary>Take the firing pose. Raised every tick a burst is live.</summary>
    Fire = 6,

    /// <summary>Unread. No caller in the image names it.</summary>
    Unread7 = 7,

    /// <summary>Throw a grenade — <c>"Stand - Throwing grenade"</c>.</summary>
    ThrowGrenade = 8,

    /// <summary>
    /// Unread. Its only caller is the <c>ai+15836 == 5</c> branch of the guard per-tick, which nothing
    /// can enter — see the original.
    /// </summary>
    Unread9 = 9,
}

/// <summary>
/// The stances an AI can wait on, and which locomotion state each one is for a given
/// <see cref="AiType"/>.
/// </summary>
/// <remarks>
/// <c>sub_48EFE0(ai, channel)</c> compares the body's current locomotion state — <c>Human+764</c>, the
/// state machine at <c>Human+748</c> — against <c>off_541658[aiType][channel]</c>, five channels per
/// type, and is false when the entry is <c>-1</c>.
/// </remarks>
public enum AiStanceChannel
{
    /// <summary>Standing.</summary>
    Standing = 0,

    /// <summary>Kneeling.</summary>
    Kneeling = 1,

    /// <summary>Prone.</summary>
    Prone = 2,

    /// <summary>An animation the AI asked for is playing.</summary>
    AnimationPlaying = 3,

    /// <summary>Mounted on an emplacement. A guard has none, so its mounted branches are dead.</summary>
    Mounted = 4,
}

/// <summary>
/// The two per-<see cref="AiType"/> tables that stand between an AI and its body.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>off_54151C</c> (ten input slots per type, pointing at arrays from
/// <c>0x54138C</c>) and <c>off_541658</c> (five locomotion states per type, from <c>0x541590</c>).
/// Twenty-nine types share ten distinct rows of each, and the two agree on which channels a type has:
/// the <c>RPG</c> has <c>-1</c> for kneel in both, which is a cross-check that they describe the same
/// set of capabilities.
/// </para>
/// </remarks>
public static class AiCombatChannels
{
    /// <summary>The value a table entry carries when the type has no such channel.</summary>
    public const int None = -1;

    /// <summary>How many input channels a row has.</summary>
    public const int InputChannelCount = 10;

    /// <summary>How many stance channels a row has.</summary>
    public const int StanceChannelCount = 5;

    // The ten distinct input rows, in the order the arrays sit in the image from 0x54138C.
    private static readonly int[][] InputRows =
    [
        [20, 21, -1, 22, -1, -1, 23, -1, -1, -1],       // 0x54138C — RPG
        [20, 21, 22, 23, 24, 25, 27, 28, 29, 30],       // 0x5413B4 — PATROL_PISTOL
        [20, 21, 22, 23, 24, 25, 27, 28, 29, 30],       // 0x5413DC — GUARD_PISTOL
        [20, 21, -1, 23, 24, 25, 27, 28, 29, 30],       // 0x541404 — GUNNER
        [20, 21, -1, 23, 24, 25, 27, 28, 29, 30],       // 0x54142C — SNIPER
        [20, -1, -1, 21, 22, -1, 23, 24, -1, -1],       // 0x541454 — ANYA and EKK
        [20, 21, 22, 23, 24, 25, 27, 28, 29, 30],       // 0x54147C — PRIBOI
        [20, 21, 22, 23, 24, 25, 27, 28, 29, -1],       // 0x5414A4 — CIVILIAN
        [20, 21, 22, 23, 24, 25, 27, 28, 29, 30],       // 0x5414CC — every patrol
        [20, 21, 22, 23, 24, 25, 27, 28, 29, 30],       // 0x5414F4 — every guard
    ];

    // The ten distinct stance rows, from 0x541590 onward.
    private static readonly int[][] StanceRows =
    [
        [0, 7, -1, 14, -1],         // 0x541590 — RPG, its own small state machine
        [0, 19, 26, 35, -1],        // 0x5415A4 — PATROL_PISTOL
        [0, 19, 26, 35, -1],        // 0x5415B8 — GUARD_PISTOL
        [0, 19, -1, 35, 36],        // 0x5415CC — GUNNER
        [0, 19, -1, 35, -1],        // 0x5415E0 — SNIPER
        [0, -1, -1, 11, -1],        // 0x5415F4 — ANYA and EKK
        [0, 19, 26, 35, -1],        // 0x541608 — PRIBOI
        [0, -1, -1, 35, -1],        // 0x54161C — CIVILIAN
        [0, 19, 26, 35, 36],        // 0x541630 — every patrol
        [0, 19, 26, 35, -1],        // 0x541644 — every guard
    ];

    // Which row each of the 29 types uses, in AiType order.
    private static readonly int[] RowOfType =
    [
        0, 3, 4, 5, 5, 6, 7, 8, 8, 8, 1, 9, 9, 9, 2,
        8, 8, 8, 8, 8, 9, 9, 9, 8, 8, 8, 9, 9, 9,
    ];

    /// <summary>Finds the input-frame slot a type drives a channel through.</summary>
    /// <param name="type">The AI type.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>The slot, or <see cref="None"/> when the type has no such channel.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is outside its table.</exception>
    public static int SlotOf(AiType type, AiCombatChannel channel)
    {
        int index = (int)channel;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(channel));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, InputChannelCount, nameof(channel));
        return InputRows[RowOf(type)][index];
    }

    /// <summary>Finds the input slot as the port's own enumeration names it.</summary>
    /// <param name="type">The AI type.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="slot">The slot, when the type has one.</param>
    /// <returns><see langword="false"/> when the type has no such channel.</returns>
    public static bool TrySlotOf(AiType type, AiCombatChannel channel, out PlayerInputSlot slot)
    {
        int value = SlotOf(type, channel);
        slot = (PlayerInputSlot)value;
        return value != None;
    }

    /// <summary>Finds the locomotion state a type's stance channel names.</summary>
    /// <param name="type">The AI type.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>The state, or <see cref="None"/> when the type has no such stance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is outside its table.</exception>
    public static int StateOf(AiType type, AiStanceChannel channel)
    {
        int index = (int)channel;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(channel));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, StanceChannelCount, nameof(channel));
        return StanceRows[RowOf(type)][index];
    }

    /// <summary>Tells whether a body in a given locomotion state is in a stance.</summary>
    /// <param name="type">The AI type.</param>
    /// <param name="channel">The stance.</param>
    /// <param name="locomotionState">The body's current state — <c>Human+764</c>.</param>
    /// <returns><see langword="true"/> when the type has that stance and the body is in it.</returns>
    /// <remarks>A type with no such stance is never in it, which is what the <c>-1</c> guard means.</remarks>
    public static bool IsInStance(AiType type, AiStanceChannel channel, int locomotionState)
    {
        int state = StateOf(type, channel);
        return state != None && state == locomotionState;
    }

    private static int RowOf(AiType type)
    {
        int index = (int)type;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(type));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, RowOfType.Length, nameof(type));
        return RowOfType[index];
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenIGI.Game.Player;

/// <summary>
/// One tick of player input: the 40 floats <c>HumanPlayerInput</c> samples, records and replays.
/// </summary>
/// <remarks>
/// <para>
/// The original samples this in <c>0x45E060</c> and posts it to the parent task, which keeps the newest
/// at <c>HumanPlayer+432</c> and the one before it at <c>+272</c> (see <see cref="PlayerInputState"/>).
/// The frame is <see cref="SizeInBytes"/> bytes and the record/playback buffers hold 9000 of them, so a
/// demo is capped at five minutes at 30 Hz.
/// </para>
/// <para>
/// Every slot is a <see cref="float"/>, including the booleans — and a boolean slot holds the
/// <em>masked bit value</em> rather than 1, because the original stores <c>mask &amp; bit</c>. Nothing
/// may compare a slot against 1; <see cref="IsSet"/> is the only correct test.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct PlayerInputFrame : IEquatable<PlayerInputFrame>
{
    /// <summary>The number of <see cref="float"/> slots in a frame.</summary>
    public const int SlotCount = 40;

    /// <summary>The size of a frame in bytes, as the original memcpys it.</summary>
    public const int SizeInBytes = SlotCount * sizeof(float);

    /// <summary>The most frames the original's record and playback buffers hold.</summary>
    public const int MaximumRecordedFrames = 9000;

    /// <summary>The lower bound the mouse sensitivity is clamped to.</summary>
    public const float MinimumSensitivity = 0.05f;

    /// <summary>The upper bound the mouse sensitivity is clamped to.</summary>
    public const float MaximumSensitivity = 1.0f;

    private SlotArray _slots;

    /// <summary>Gets or sets a slot by index.</summary>
    /// <param name="slot">The slot, <c>0</c> to <see cref="SlotCount"/> minus one.</param>
    /// <returns>The stored value.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="slot"/> is outside the frame.</exception>
    public float this[int slot]
    {
        get => _slots[slot];
        set => _slots[slot] = value;
    }

    /// <summary>Gets or sets the horizontal look delta, already scaled by the sensitivity.</summary>
    public float LookX
    {
        get => this[(int)PlayerInputSlot.LookX];
        set => this[(int)PlayerInputSlot.LookX] = value;
    }

    /// <summary>Gets or sets the vertical look delta, already scaled and inverted if the profile asks.</summary>
    public float LookY
    {
        get => this[(int)PlayerInputSlot.LookY];
        set => this[(int)PlayerInputSlot.LookY] = value;
    }

    /// <summary>Gets the slots as a span, in the order the sampler writes them.</summary>
    /// <returns>A span of <see cref="SlotCount"/> values over this frame's storage.</returns>
    [UnscopedRef]
    public Span<float> AsSpan() => _slots;

    /// <summary>Reports whether an action slot is engaged.</summary>
    /// <param name="slot">The slot to test.</param>
    /// <returns><see langword="true"/> when the slot is non-zero.</returns>
    /// <remarks>
    /// The test is against zero, not against one: a boolean slot carries the bit's masked value, which
    /// for a high bit is a large number rather than 1. Comparing to 1 would read almost every action as
    /// released.
    /// </remarks>
    public readonly bool IsSet(PlayerInputSlot slot) => _slots[(int)slot] != 0f;

    /// <summary>Builds a frame from its slots.</summary>
    /// <param name="slots">Exactly <see cref="SlotCount"/> values.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="slots"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="slots"/> is not <see cref="SlotCount"/> long.</exception>
    public static PlayerInputFrame FromSlots(params float[] slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Length != SlotCount)
        {
            throw new ArgumentException($"A frame has exactly {SlotCount} slots.", nameof(slots));
        }

        PlayerInputFrame frame = default;
        slots.CopyTo(frame.AsSpan());
        return frame;
    }

    /// <summary>Clamps a raw mouse sensitivity the way the sampler does.</summary>
    /// <param name="sensitivity">The profile's stored sensitivity.</param>
    /// <returns>The value the look deltas are multiplied by.</returns>
    /// <remarks>
    /// The original tests the low bound first and the high bound second, so a NaN — which fails both
    /// comparisons — comes out as <see cref="MinimumSensitivity"/> rather than propagating.
    /// </remarks>
    public static float ClampSensitivity(float sensitivity) =>
        sensitivity >= MinimumSensitivity
            ? (sensitivity > MaximumSensitivity ? MaximumSensitivity : sensitivity)
            : MinimumSensitivity;

    /// <inheritdoc/>
    public readonly bool Equals(PlayerInputFrame other)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] != other._slots[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is PlayerInputFrame other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode()
    {
        HashCode hash = default;
        for (int i = 0; i < SlotCount; i++)
        {
            hash.Add(_slots[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Compares two frames slot by slot.</summary>
    /// <param name="left">The first frame.</param>
    /// <param name="right">The second frame.</param>
    /// <returns><see langword="true"/> when every slot matches.</returns>
    public static bool operator ==(PlayerInputFrame left, PlayerInputFrame right) => left.Equals(right);

    /// <summary>Compares two frames slot by slot.</summary>
    /// <param name="left">The first frame.</param>
    /// <param name="right">The second frame.</param>
    /// <returns><see langword="true"/> when any slot differs.</returns>
    public static bool operator !=(PlayerInputFrame left, PlayerInputFrame right) => !left.Equals(right);

    [InlineArray(SlotCount)]
    private struct SlotArray
    {
        private float _element0;
    }
}

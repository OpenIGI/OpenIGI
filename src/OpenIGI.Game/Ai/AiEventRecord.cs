using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>The twenty-eight-byte record one AI event is posted as.</summary>
/// <param name="Type">The event, at <c>+0</c>.</param>
/// <param name="Side">The side the event belongs to, at <c>+4</c>. <c>-1</c> when it has none.</param>
/// <param name="Filter">The admission tests to apply, at <c>+8</c>.</param>
/// <param name="Radius">The radius <see cref="AiEventFilter.InRange"/> tests against, at <c>+12</c>.</param>
/// <param name="Position">Where the event happened, at <c>+16</c>.</param>
/// <param name="Source">The task that caused it, at <c>+20</c>.</param>
/// <param name="Subject">The task it is about, at <c>+24</c>.</param>
/// <remarks>
/// <para>
/// <b><see cref="Filter"/> is not a priority.</b> It sits where a priority would plausibly go and it is read
/// by <c>0x450110</c> as a mask of admission tests; the priority that arbitration uses lives on a
/// <em>pending action</em>, which is a different record. Worth stating because the two structures are one
/// call apart in the pump.
/// </para>
/// <para>
/// <c>0x450D80</c>, the short helper the engine posts its own alarm and animation events with, writes the
/// type, puts its third argument in <see cref="Filter"/> and zeroes <c>+16</c>..<c>+24</c> — leaving
/// <see cref="Side"/> and <see cref="Radius"/> <b>uninitialised stack</b>. Every caller passes a filter of
/// zero, so neither field is ever read; a caller that passed a filter would be reading rubbish.
/// </para>
/// </remarks>
public readonly record struct AiEventRecord(
    AiEvent Type,
    int Side,
    AiEventFilter Filter,
    float Radius,
    Vector3d Position,
    int Source,
    int Subject)
{
    /// <summary>The record's size in bytes in the original.</summary>
    public const int Size = 28;

    /// <summary>Builds the unfiltered form <c>0x450D80</c> posts.</summary>
    /// <param name="type">The event.</param>
    /// <returns>The record.</returns>
    /// <remarks>
    /// Side is <c>-1</c> and radius 0 here rather than whatever the original's stack held, because every
    /// shipped caller passes a filter of zero and so never reads either.
    /// </remarks>
    public static AiEventRecord Simple(AiEvent type) =>
        new(type, -1, AiEventFilter.None, 0.0f, default, 0, 0);
}

namespace OpenIGI.Audio;

/// <summary>Identifies a playing voice.</summary>
/// <param name="Value">The opaque identifier; zero is the null handle.</param>
/// <remarks>
/// <para>
/// A voice handle carries a generation alongside its slot index, and every device call checks it. This
/// matters because voices are a fixed pool that gets stolen from: a caller holding a handle to a
/// looping sound whose voice was reclaimed for something more urgent must not later stop or re-aim
/// whatever took its place. A stale handle is ignored.
/// </para>
/// <para>
/// The original has the same hazard and does not guard against it — its callers keep a raw pointer into
/// the 32-voice array, for instance the per-task sound handle at <c>HumanAI+16032</c>. Guarding is a
/// deliberate divergence; it buys a class of bug that then cannot exist.
/// </para>
/// </remarks>
public readonly record struct AudioVoiceHandle(int Value)
{
    /// <summary>Gets the handle that refers to no voice.</summary>
    public static AudioVoiceHandle None => default;

    /// <summary>Gets a value indicating whether the handle refers to a voice.</summary>
    public bool IsValidHandle => Value != 0;
}

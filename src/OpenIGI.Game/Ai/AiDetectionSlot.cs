namespace OpenIGI.Game.Ai;

/// <summary>
/// One AI's tracking record for one other human — a 48-byte slot at a fixed index, not a dynamic entry.
/// </summary>
/// <remarks>
/// <para>
/// The vision subsystem holds a 48-byte header followed by one slot per human, so slot <c>i</c> sits at
/// <c>vision + 48 × (i + 1)</c> and the index is the human's position in the engine's global registry. A slot
/// is <em>linked onto</em> the enemy or friendly list when it becomes relevant and unlinked when it stops, but
/// the storage never moves.
/// </para>
/// <para>
/// Offsets, from <c>0x489500</c> (register) and <c>0x4898D0</c> (test): <c>+8</c> a reference-counted task
/// handle, <c>+16</c> the cone-test cooldown, <c>+20</c> the ray cooldown, <c>+24</c> the tick it was first
/// registered, <c>+32</c> the distance as a double, <c>+40</c> in-cone, <c>+41</c> visible, <c>+42</c>
/// registered.
/// </para>
/// </remarks>
public sealed class AiDetectionSlot
{
    /// <summary>The size of the record in bytes in the original.</summary>
    public const int Size = 48;

    /// <summary>Gets or sets a value indicating whether the slot is linked onto a list — the byte at <c>+42</c>.</summary>
    /// <remarks>
    /// Set by <c>0x489500</c> and cleared by <c>0x4894E0</c>, which also releases the task handle. This is
    /// <b>not</b> a visibility flag: it means the candidate currently passes the side and category filters, so
    /// the AI is tracking it at all.
    /// </remarks>
    public bool IsRegistered { get; set; }

    /// <summary>Gets or sets a value indicating whether the target is inside a view cone — the byte at <c>+40</c>.</summary>
    public bool IsInCone { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the target is both inside a cone and on a clear line — the byte
    /// at <c>+41</c>, and the one the per-tick's detection sweep posts events from.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>Gets or sets ticks left before the cone test may run again — <c>+16</c>.</summary>
    public int ConeCooldown { get; set; }

    /// <summary>Gets or sets ticks left before the line-of-sight ray may run again — <c>+20</c>.</summary>
    public int RayCooldown { get; set; }

    /// <summary>Gets or sets the distance from the eye to the target, refreshed on every sweep — <c>+32</c>.</summary>
    public double Distance { get; set; }

    /// <summary>Gets or sets the tick the slot was registered on — <c>+24</c>.</summary>
    public int FirstSeenTick { get; set; }

    /// <summary>Resets the slot to what <c>0x489500</c> writes when it registers a candidate.</summary>
    /// <param name="tick">The current tick.</param>
    public void Register(int tick)
    {
        IsRegistered = true;
        IsInCone = false;
        IsVisible = false;
        ConeCooldown = 0;
        RayCooldown = 0;
        Distance = 0;
        FirstSeenTick = tick;
    }

    /// <summary>Unlinks the slot, as <c>0x4894E0</c> does.</summary>
    /// <remarks>
    /// It clears only <c>+42</c> and releases the handle — <b>the in-cone and visible flags are left as they
    /// were</b>. They are meaningless while unregistered, and re-registering resets them, so nothing reads the
    /// stale values; it is worth knowing all the same.
    /// </remarks>
    public void Unregister() => IsRegistered = false;
}

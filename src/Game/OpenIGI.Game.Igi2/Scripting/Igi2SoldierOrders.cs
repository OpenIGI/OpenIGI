using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi2.Scripting;

/// <summary>What a soldier's own script tells it, in I.G.I. 2's terms: written into a script by <see cref="Igi2SoldierScript"/>.</summary>
/// <remarks>Each is <see langword="null"/> where the script says nothing, and the soldier does as its type does.</remarks>
public sealed record Igi2SoldierOrders
{
    /// <summary>Gets the patrol path it walks when idle, by its task id.</summary>
    public int? PatrolPathId { get; init; }

    /// <summary>Gets the command the patrol starts from.</summary>
    public int PatrolStart { get; init; }

    /// <summary>Gets the patrol path it walks once the alarm sounds, by its task id.</summary>
    public int? AlarmPatrolPathId { get; init; }

    /// <summary>Gets the command the alarm's patrol starts from.</summary>
    public int AlarmPatrolStart { get; init; }

    /// <summary>Gets the mounted gun it mans, by its task id.</summary>
    public int? GunnerId { get; init; }

    /// <summary>Gets whether it raises the alarm before fighting or after.</summary>
    public AiAlarmAccess? AlarmAccess { get; init; }

    /// <summary>Gets how far it sees, in metres.</summary>
    public float? ViewLength { get; init; }

    /// <summary>Gets how wide it sees, in degrees.</summary>
    public float? ViewGamma { get; init; }

    /// <summary>Gets whether it cannot be hurt.</summary>
    public bool? IsInvulnerable { get; init; }

    /// <summary>
    /// Gets the tunings set on creation after the default handler, each an <c>AIFunction_Set…</c> native and its value, in order:
    /// as I.G.I. 2's own sniper scripts set their cones, reach and accuracy.
    /// </summary>
    public IReadOnlyList<(string Native, float Value)> Tunings { get; init; } = [];
}

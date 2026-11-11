namespace OpenIGI.Game.Ai;

/// <summary>
/// What a guard's own script tells it, in either game: the path it patrols when idle and when the alarm sounds, the alarm it
/// runs for and how, the gun it mans, and how far and wide it sees. Read off a script (<see cref="AiGuardScript.Read"/>) and
/// written into one (<see cref="AiGuardScript.Build"/>), the same natives in both games.
/// </summary>
/// <remarks>Each is <see langword="null"/> where the script does not say, and the guard does as its kind does.</remarks>
public sealed record AiGuardOrders
{
    /// <summary>Gets an empty set of orders: a script that says nothing of its own.</summary>
    public static AiGuardOrders None { get; } = new();

    /// <summary>Gets the patrol path the guard walks when idle, <c>AIAction_Patrol</c>'s path id.</summary>
    public int? PatrolPathId { get; init; }

    /// <summary>Gets the command the patrol starts from.</summary>
    public int PatrolStart { get; init; }

    /// <summary>Gets the patrol path it walks once the alarm sounds, <c>AIAction_Patrol</c> on <c>AIEVENT_ALARMON</c>.</summary>
    public int? AlarmPatrolPathId { get; init; }

    /// <summary>Gets the command the alarm's patrol starts from.</summary>
    public int AlarmPatrolStart { get; init; }

    /// <summary>Gets the alarm control the guard runs for and hears, <c>AIFunction_SetAlarmControlID</c>.</summary>
    public int? AlarmControlId { get; init; }

    /// <summary>Gets the alarm trigger it raises the alarm at, <c>AIFunction_SetAlarmTriggerID</c>.</summary>
    public int? AlarmTriggerId { get; init; }

    /// <summary>Gets whether it raises the alarm before fighting or after, <c>AIFunction_SetAlarmAccess</c>.</summary>
    public AiAlarmAccess? AlarmAccess { get; init; }

    /// <summary>Gets the mounted gun it mans, <c>AIFunction_SetGunnerID</c>.</summary>
    public int? GunnerId { get; init; }

    /// <summary>Gets how far it sees, in metres, <c>AIFunction_SetViewLength</c>.</summary>
    public float? ViewLength { get; init; }

    /// <summary>Gets how wide it sees, in degrees, <c>AIFunction_SetViewGamma</c>.</summary>
    public float? ViewGamma { get; init; }

    /// <summary>Gets whether it cannot be hurt, <c>AIFunction_SetInvulnerability</c>.</summary>
    public bool? IsInvulnerable { get; init; }

    /// <summary>
    /// Gets the tunings set on creation after the default handler, each an <c>AIFunction_Set…</c> native and its value, in order:
    /// as I.G.I. 2's own sniper scripts set their cones, reach and accuracy. Written, not read.
    /// </summary>
    public IReadOnlyList<(string Native, float Value)> Tunings { get; init; } = [];

    /// <summary>Gets whether the script says anything at all of its own.</summary>
    public bool IsEmpty => this with { Tunings = None.Tunings } == None && Tunings.Count == 0;
}

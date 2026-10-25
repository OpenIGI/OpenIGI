using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// A running <c>AIAction_FireAtTask</c> or <c>AIAction_FireAtNode</c> (<c>0x48EDE0</c> / <c>0x48EE00</c>): the
/// target it aims at, how long it has left, and where it is in its bursts.
/// </summary>
internal sealed class Igi1ScriptedFire
{
    /// <summary>Gets the target's live position, or <see langword="null"/> once it is gone.</summary>
    public required Func<Vector3d?> LocateTarget { get; init; }

    /// <summary>Gets or sets the ticks the action has left to run.</summary>
    public required int RemainingTicks { get; set; }

    /// <summary>Gets the delay, in ticks, before fire is entered again after each burst.</summary>
    public required int BurstDelayTicks { get; init; }

    /// <summary>Gets whether an empty clip reloads through the visible channel rather than directly.</summary>
    public required bool ShowReload { get; init; }

    /// <summary>Gets or sets the ticks still to wait before the next burst.</summary>
    public int DelayTicks { get; set; }

    /// <summary>Gets or sets the rounds the current burst has left.</summary>
    public int BurstRoundsLeft { get; set; }

    /// <summary>Gets or sets the clip's count on the last tick, or <c>-1</c> before the first.</summary>
    public int PreviousAmmo { get; set; } = -1;
}

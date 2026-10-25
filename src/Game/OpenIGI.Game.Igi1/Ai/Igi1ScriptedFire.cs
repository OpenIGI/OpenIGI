using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// A running <c>AIAction_FireAtTask</c> or <c>AIAction_FireAtNode</c> (<c>0x48EDE0</c> / <c>0x48EE00</c>): the
/// target it aims at, how long it has left, and where it is between its bursts.
/// </summary>
/// <remarks>
/// The bursts are combat's own (<c>0x48B9C0</c> and <c>0x48D8E0</c>), run by <see cref="Bursts"/>: the shot count,
/// the aim error and the single-shot delays are drawn from the level's stream exactly as a fight draws them.
/// </remarks>
internal sealed class Igi1ScriptedFire
{
    /// <summary>Gets the target's live position, or <see langword="null"/> once it is gone.</summary>
    public required Func<Vector3d?> LocateTarget { get; init; }

    /// <summary>Gets the combat burst machinery the action fires with.</summary>
    public required AiGuardCombat Bursts { get; init; }

    /// <summary>Gets or sets the ticks the action has left to run (the parameter block's <c>+12</c>).</summary>
    public required int RemainingTicks { get; set; }

    /// <summary>Gets the wait, in ticks, armed after each burst (<c>+16</c>).</summary>
    public required int BurstDelayTicks { get; init; }

    /// <summary>Gets whether an empty clip reloads through the input slot rather than refilling at once (<c>+24</c>).</summary>
    public required bool ShowReload { get; init; }

    /// <summary>Gets or sets the wait before the burst set up fires (<c>+20</c>): -1 when none.</summary>
    public int Wait { get; set; } = -1;

    /// <summary>Gets or sets whether a burst is set up (<c>+25</c>).</summary>
    public bool BurstSetUp { get; set; }
}

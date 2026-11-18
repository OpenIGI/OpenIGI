using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>A player of a co-op squad as one tick finds it: who, where it stands or lies, and whether it is down.</summary>
/// <param name="Entity">Its entity.</param>
/// <param name="Slot">Its slot.</param>
/// <param name="At">Where its feet are, in world units.</param>
/// <param name="Yaw">Which way it faces.</param>
/// <param name="Down">Whether it is down, waiting to be revived.</param>
public readonly record struct SquadMember(int Entity, int Slot, Vector3d At, float Yaw, bool Down);

/// <summary>
/// The co-op squad's rules that hold for every title: a fallen player lies where it fell until a teammate stands by it long
/// enough, and while the whole squad stands, where each stands is kept as the checkpoint the squad goes back to when all are down.
/// </summary>
/// <remarks>The title stands its own players up; this only keeps the count and the places.</remarks>
public sealed class CoopSquad
{
    /// <summary>How near a teammate stands to a fallen player to revive it, in world units: two metres.</summary>
    public const double ReviveReachUnits = 2 * 4096;

    // How often, while the whole squad stands, where each stands is kept as the checkpoint: fifteen seconds of ticks.
    private const int CheckpointTicks = 15 * 30;

    // Where each player stood when the squad last stood together, by entity, and the tick it was kept on.
    private readonly Dictionary<int, (Vector3d At, float Yaw)> _checkpoint = [];
    private long? _checkpointTick;

    // Each fallen player's revival so far, in ticks, by entity.
    private readonly Dictionary<int, int> _reviving = [];

    /// <summary>Gets how long a teammate stands by a fallen player to bring it back, in ticks (<c>coop_revive_seconds</c>).</summary>
    public static int ReviveTicks => CoopCVars.ReviveSeconds.Value * 30;

    /// <summary>Gets how much tougher the soldiers are for a squad of a size: <c>coop_enemy_health</c> per player past the first.</summary>
    /// <param name="players">The players in the squad.</param>
    /// <returns>The scale on a soldier's health, one for a player alone.</returns>
    public static float EnemyHealthScale(int players) => 1f + (CoopCVars.EnemyHealthPerPlayer.Value / 100f * Math.Max(0, players - 1));

    /// <summary>Gets whether a checkpoint has been kept: somewhere the squad stood together.</summary>
    public bool HasCheckpoint => _checkpoint.Count > 0;

    /// <summary>Gets a value indicating whether a player of the squad stood at the last tick: someone who could revive the fallen.</summary>
    public bool AnyoneStanding { get; private set; } = true;

    /// <summary>
    /// Gets how far a fallen player's revival has gone, in <c>[0,1]</c>, or <see langword="null"/> when nobody is left standing to
    /// revive it -- a player alone, or the whole squad down -- so it is not told to wait for a teammate.
    /// </summary>
    /// <param name="entity">The player's entity, which the caller knows to be down.</param>
    /// <returns>The progress.</returns>
    public float? ReviveProgressOf(int entity) =>
        AnyoneStanding ? Math.Min(1f, _reviving.GetValueOrDefault(entity) / (float)ReviveTicks) : null;

    /// <summary>
    /// Runs a tick of the squad: the checkpoint kept every so often while nobody is down, and each fallen player tended by a
    /// standing one within <see cref="ReviveReachUnits"/> a tick nearer standing; one left alone starts again from nothing.
    /// </summary>
    /// <param name="now">The tick.</param>
    /// <param name="members">Every player of the squad.</param>
    /// <returns>The fallen players whose revival is done, for the title to stand up where they lie.</returns>
    public IReadOnlyList<SquadMember> Tick(long now, IReadOnlyList<SquadMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        List<Vector3d> up = [.. members.Where(static m => !m.Down).Select(static m => m.At)];
        bool anyDown = members.Any(static m => m.Down);
        AnyoneStanding = up.Count > 0;
        if (!anyDown && up.Count > 0 && (_checkpointTick is not { } kept || now - kept >= CheckpointTicks))
        {
            _checkpoint.Clear();
            foreach (SquadMember member in members)
            {
                _checkpoint[member.Entity] = (member.At, member.Yaw);
            }

            _checkpointTick = now;
        }

        List<SquadMember> revived = [];
        foreach (SquadMember fallen in members.Where(static m => m.Down))
        {
            bool tended = up.Exists(p => Vector3d.Distance(p, fallen.At) <= ReviveReachUnits);
            int ticks = tended ? _reviving.GetValueOrDefault(fallen.Entity) + 1 : 0;
            if (ticks < ReviveTicks)
            {
                _reviving[fallen.Entity] = ticks;
                continue;
            }

            _reviving.Remove(fallen.Entity);
            revived.Add(fallen);
        }

        foreach (int entity in _reviving.Keys.Where(e => !members.Any(m => m.Down && m.Entity == e)).ToArray())
        {
            _reviving.Remove(entity);
        }

        return revived;
    }

    /// <summary>Gets where a player stood when the squad last stood together, or <see langword="null"/>.</summary>
    /// <param name="entity">The player's entity.</param>
    /// <returns>Its place and facing.</returns>
    public (Vector3d At, float Yaw)? CheckpointOf(int entity) => _checkpoint.TryGetValue(entity, out var kept) ? kept : null;

    /// <summary>Forgets a player's revival under way: it left the squad.</summary>
    /// <param name="entity">The player's entity.</param>
    public void Forget(int entity) => _reviving.Remove(entity);

    /// <summary>Forgets every revival under way: the squad stood up together from the checkpoint.</summary>
    public void StoodUpTogether()
    {
        _reviving.Clear();
        AnyoneStanding = true;
    }
}

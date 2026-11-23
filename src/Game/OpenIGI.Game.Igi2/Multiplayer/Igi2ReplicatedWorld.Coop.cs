using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Saves;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>
/// A campaign mission played together: every player on the level player's side against the soldiers, armed as the level arms its
/// own player, down until a teammate revives it, and the squad back at its checkpoint when all are down.
/// </content>
public sealed partial class Igi2ReplicatedWorld
{
    private readonly CoopSquad _squad = new();

    // What the level's own player carried once the level began -- the mission's loadout -- written as its save writes it; each
    // partner is armed with it, as each client's own copy of the level arms that client's player. Null until the first tick.
    private string? _startKit;

    // The partners made before the loadout was known, armed once it is.
    private readonly List<Igi2NetworkHuman> _unarmed = [];

    // The players whose clients dropped, kept aside out of the level by their parked keys, with their sides.
    private readonly Dictionary<int, (Igi2NetworkHuman Human, int Team)> _parked = [];

    // Each soldier's own health, before the squad's size made it tougher.
    private readonly Dictionary<Igi2HumanSoldier, float> _soldierHealth = new(ReferenceEqualityComparer.Instance);
    private float _enemyScale = 1f;
    private bool? _isMatchMap;

    // Whether the level is a multiplayer map (its spawn areas), rather than a campaign mission played together.
    private bool IsMatchMap => _isMatchMap ??= _level.LevelTasks().OfType<Igi2SpawnAreaTask>().Any();

    /// <inheritdoc/>
    /// <remarks>A campaign mission: a fallen player lies where it fell until a teammate stands by it (<see cref="CoopSquad"/>).</remarks>
    public bool HoldsTheDead => !IsMatchMap;

    // Whether a round or blast does a player no harm: in a campaign mission played together without friendly fire
    // (coop_friendlyfire, as Project I.G.I.'s co-op has it), one from another of the session's players. Its own still hurts it.
    private bool Spared(Igi2HumanPlayer struck, Weapons.IIgi2WeaponOwner? by) =>
        HoldsTheDead
        && !CoopCVars.FriendlyFire.Value
        && by is Igi2HumanPlayer shooter
        && !ReferenceEquals(shooter, struck)
        && (ReferenceEquals(shooter, _level.Human) || _bySlot.Values.Any(p => ReferenceEquals(p.Human, shooter)));

    /// <inheritdoc/>
    public float? ReviveProgressOf(int entity) => HumanOf(entity) is { IsDead: true } ? _squad.ReviveProgressOf(entity) : null;

    /// <inheritdoc/>
    /// <remarks>
    /// Each fallen player tended by a standing one counts toward its revival, and stands up where it fell, whole. The soldiers'
    /// toughness is kept to the squad's size here too, once a tick.
    /// </remarks>
    public IReadOnlyList<int> TakeRevivals()
    {
        ScaleEnemies();
        List<int> revived = [];
        foreach (SquadMember fallen in _squad.Tick(CurrentTick, Members()))
        {
            _dead.Remove(fallen.Entity);
            HumanOf(fallen.Entity)?.Revive(fallen.At, fallen.Yaw);
            revived.Add(fallen.Slot);
        }

        return revived;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Not once the level's own flow has failed: I.G.I. 2's missions fail a few seconds after their "player dead" status message
    /// has shown (mission 1's <c>StatusMessage_356.nTicksSinceFinishedDisplay &gt; 5 * GAME_FREQUENCY</c>), a latch standing
    /// the squad up again does not lift, so the mission is only restarted.
    /// </remarks>
    public bool CanRetryFromCheckpoint =>
        HoldsTheDead && _squad.HasCheckpoint && _level.Outcome != OpenIGI.Game.Player.TitleMissionOutcome.Failed
        && Members() is { Count: > 0 } members && members.TrueForAll(static m => m.Down);

    /// <inheritdoc/>
    public void RetryFromCheckpoint()
    {
        foreach (SquadMember member in Members())
        {
            (Vector3d at, float yaw) = _squad.CheckpointOf(member.Entity) ?? (member.At, member.Yaw);
            HumanOf(member.Entity)?.Revive(at, yaw);
            _dead.Remove(member.Entity);
        }

        _squad.StoodUpTogether();
    }

    /// <inheritdoc/>
    public bool ParkPlayer(int slot, int parked)
    {
        if (!_bySlot.Remove(slot, out Igi2NetworkHuman? human))
        {
            return false;
        }

        if (_names.Remove(slot, out string? name))
        {
            _parkedNames[parked] = name;
        }

        _level.RemoveNetworkHuman(human);
        _looks.Remove(slot);
        _awaitedLooks.Remove(slot);
        _squad.Forget(slot + 1);
        _dead.Remove(slot + 1);
        _parked[parked] = (human, Teams.TeamOf(slot));
        Teams.Remove(slot);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Back where it was and as it was, carrying what it carried, wounded or down as it was left.</remarks>
    public int? UnparkPlayer(int parked, int slot)
    {
        if (!_parked.Remove(parked, out (Igi2NetworkHuman Human, int Team) kept))
        {
            return null;
        }

        _level.ReturnNetworkHuman(kept.Human);
        _bySlot[slot] = kept.Human;
        if (_parkedNames.Remove(parked, out string? name))
        {
            _names[slot] = name;
        }

        Teams.Assign(slot, kept.Team);
        return slot + 1;
    }

    /// <inheritdoc/>
    public void ForgetPlayer(int parked)
    {
        _parked.Remove(parked);
        _parkedNames.Remove(parked);
    }

    // The names of the players kept aside, by their parked keys.
    private readonly Dictionary<int, string> _parkedNames = [];

    // A fallen player's health byte carries its revival instead, for every client's panel and mark to show (as I.G.I.'s does).
    private ReplicatedHuman WithRevive(ReplicatedHuman human) =>
        (human.Flags & ReplicatedHumanCondition.Alive) != 0
            ? human
            : human with { Health = (byte)Math.Round((ReviveProgressOf(human.Id) ?? 0f) * 255f) };

    /// <inheritdoc/>
    public Vector3d? PositionOf(int slot) => HumanOf(slot == ServerSession.HostSlot ? HostEntity : slot + 1)?.Position;

    /// <inheritdoc/>
    public float? YawOf(int slot) => HumanOf(slot == ServerSession.HostSlot ? HostEntity : slot + 1)?.Gamma;

    // Every player of the squad as this tick has it.
    private List<SquadMember> Members()
    {
        List<SquadMember> members = [];
        if (IncludesHost)
        {
            members.Add(new SquadMember(HostEntity, ServerSession.HostSlot, _level.Human.Position, _level.Human.Gamma, _level.Human.IsDead));
        }

        foreach ((int slot, Igi2NetworkHuman other) in _bySlot)
        {
            members.Add(new SquadMember(slot + 1, slot, other.Human.Position, other.Human.Gamma, other.Human.IsDead));
        }

        return members;
    }

    /// <inheritdoc/>
    /// <remarks>Each player's own <c>tMissionScoreData</c>, the host's and every partner's alike.</remarks>
    public Scoring.MissionScore? ScoreOf(int slot) =>
        HumanOf(slot == ServerSession.HostSlot ? HostEntity : slot + 1) is { } human ? Igi2TitlePlayer.ScoreOf(human, 0) : null;

    // A player of the session by its entity: the level's own, or a client's.
    private Igi2HumanPlayer? HumanOf(int entity) =>
        entity == HostEntity ? (IncludesHost ? _level.Human : null) : _bySlot.GetValueOrDefault(entity - 1)?.Human;

    // A new partner armed as the level armed its own player, now or once the loadout is known.
    private void Arm(Igi2NetworkHuman partner)
    {
        if (_startKit is not { } kit)
        {
            _unarmed.Add(partner);
            return;
        }

        if (partner.Weapons is { } weapons && SaveScript.ReadState(kit, weapons.Inventory.ReadState) && weapons.Inventory.Current is { } inHand)
        {
            weapons.Activate(inHand);
        }
    }

    // The loadout, once the level's first tick has handed the level's own player its weapons; the partners waiting armed with it.
    private void KeepStartKit()
    {
        if (_startKit is not null || _level.Weapons is not { } own)
        {
            return;
        }

        _startKit = SaveScript.WriteState(own.Inventory.WriteState);
        Igi2NetworkHuman[] waiting = [.. _unarmed];
        _unarmed.Clear();
        foreach (Igi2NetworkHuman partner in waiting)
        {
            Arm(partner);
        }
    }

    // The soldiers made tougher for a bigger squad, every one of them, keeping what has been taken from each in proportion.
    private void ScaleEnemies()
    {
        if (IsMatchMap || _level.Soldiers is not { } soldiers)
        {
            return;
        }

        float scale = CoopSquad.EnemyHealthScale(_bySlot.Count + (IncludesHost ? 1 : 0));
        if (scale == _enemyScale)
        {
            return;
        }

        foreach (Igi2HumanSoldier soldier in soldiers.Bodies)
        {
            Igi2HumanDamage damage = soldier.Damage;
            if (!_soldierHealth.TryGetValue(soldier, out float own))
            {
                own = damage.MaxDamage / _enemyScale;
                _soldierHealth[soldier] = own;
            }

            if (!soldier.IsDead && damage.MaxDamage > 0f)
            {
                float share = damage.Damage / damage.MaxDamage;
                damage.MaxDamage = own * scale;
                damage.Damage = share * damage.MaxDamage;
            }
        }

        _enemyScale = scale;
    }
}

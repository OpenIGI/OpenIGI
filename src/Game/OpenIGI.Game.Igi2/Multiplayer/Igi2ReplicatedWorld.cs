using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>An I.G.I. 2 level as the server session sees it: a networked player per client, and every player in each snapshot.</summary>
/// <remarks>
/// <para>
/// The level's own player is the host's: on a listen server it is the host playing, and goes in every snapshot as entity
/// <see cref="HostEntity"/>; on a dedicated server <see cref="IncludesHost"/> is off and it stays out of them. Each client's
/// player is entity <c>slot + 1</c>.
/// </para>
/// <para>
/// Until the multiplayer maps' spawn areas are read, a client's player stands beside the level's own spawn, a pace apart per
/// slot.
/// </para>
/// </remarks>
public sealed class Igi2ReplicatedWorld : IReplicatedWorld
{
    /// <summary>The entity id of the level's own player.</summary>
    public const int HostEntity = 0;

    private const double SpawnSpacing = 8192;

    private readonly Igi2TitlePlayer _level;
    private readonly SortedDictionary<int, Igi2NetworkHuman> _bySlot = [];
    private readonly List<ReplicatedHuman> _scratch = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2ReplicatedWorld"/> class.</summary>
    /// <param name="level">The level, as its own player holds it.</param>
    public Igi2ReplicatedWorld(Igi2TitlePlayer level) => _level = level ?? throw new ArgumentNullException(nameof(level));

    /// <summary>Gets or sets a value indicating whether the level's own player is a player of the session (a listen server).</summary>
    public bool IncludesHost { get; set; } = true;

    /// <inheritdoc/>
    public int ChannelCount => Igi2HumanChannels.Count;

    /// <inheritdoc/>
    public long CurrentTick => _level.TickCount;

    /// <summary>Gets a client's networked player.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The player, or <see langword="null"/>.</returns>
    public Igi2NetworkHuman? PlayerOf(int slot) => _bySlot.GetValueOrDefault(slot);

    /// <inheritdoc/>
    public int? AddPlayer(int slot, string name)
    {
        Igi2HumanPlayer host = _level.Human;
        if (host.Model is not { } model)
        {
            return null;
        }

        Vector3d at = host.Position + new Vector3d(SpawnSpacing * (slot + 1), 0, 0);
        if (_level.AddNetworkHuman(model.Name, at, host.Gamma, host.Team) is not { } human)
        {
            return null;
        }

        _bySlot[slot] = human;
        return slot + 1;
    }

    /// <inheritdoc/>
    public void RemovePlayer(int slot)
    {
        if (_bySlot.Remove(slot, out Igi2NetworkHuman? human))
        {
            _level.RemoveNetworkHuman(human);
        }
    }

    /// <inheritdoc/>
    public UserCommandBuffer? CommandsFor(int slot) => _bySlot.GetValueOrDefault(slot)?.Commands;

    /// <inheritdoc/>
    public Snapshot Capture(long tick)
    {
        _scratch.Clear();
        if (IncludesHost)
        {
            _scratch.Add(Replicate(HostEntity, _level.Human));
        }

        foreach ((int slot, Igi2NetworkHuman human) in _bySlot)
        {
            _scratch.Add(Replicate(slot + 1, human.Human));
        }

        return new Snapshot(tick, [.. _scratch]);
    }

    /// <summary>One I.G.I. 2 human as a snapshot carries it.</summary>
    /// <param name="id">Its entity id.</param>
    /// <param name="human">The human.</param>
    /// <returns>Its replicated state.</returns>
    public static ReplicatedHuman Replicate(int id, Igi2HumanPlayer human)
    {
        ArgumentNullException.ThrowIfNull(human);
        float max = human.Damage.MaxDamage;
        float left = max > 0 ? Math.Clamp(1f - (human.Damage.Damage / max), 0f, 1f) : 1f;
        ReplicatedHumanCondition condition = ReplicatedHumanCondition.None;
        if (!human.IsDead)
        {
            condition |= ReplicatedHumanCondition.Alive;
        }

        if (human.IsKeepingLow)
        {
            condition |= ReplicatedHumanCondition.Low;
        }

        return new ReplicatedHuman(
            id,
            Int3.From(human.Position),
            human.Speed,
            Quantize.AngleToBits(human.Gamma),
            Quantize.AngleToBits(human.View.Pitch),
            (ushort)Math.Clamp(human.Locomotion.CurrentState, 0, ushort.MaxValue),
            (ushort)Math.Clamp(human.ActionState, 0, ushort.MaxValue),
            (ushort)Math.Clamp(human.Weapon.Inventory?.Current?.WeaponType.Id ?? 0, 0, ushort.MaxValue),
            (byte)Math.Round(left * 255),
            (byte)Math.Clamp(human.Team, 0, 255),
            condition);
    }
}

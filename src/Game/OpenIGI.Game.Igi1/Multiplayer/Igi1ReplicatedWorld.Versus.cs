using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>The world played player against player: teams, whose rounds hurt whom, and where the fallen are put back.</summary>
public sealed partial class Igi1ReplicatedWorld
{
    /// <inheritdoc/>
    public void SetTeam(int slot, int team) => _teams[slot] = team;

    /// <inheritdoc/>
    public void UseLayout(Game.Multiplayer.Modes.MpLayout layout) => _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    private Game.Multiplayer.Modes.MpLayout? _layout;

    // Where a player of a versus match is put: one of its team's spawns (anyone's in a free-for-all), the one farthest from
    // every other player still standing, as Source's deathmatch spawn picks; the level's own start without a layout.
    private PlayerSpawnPoint? VersusSpawn(int slot, PlayerSpawnPoint start)
    {
        if (_layout is not { Spawns.Length: > 0 } layout)
        {
            return null;
        }

        List<Vector3d> others = [];
        if (slot != ServerSession.HostSlot && IncludesHost && _game.World.Player is { } host && !_game.World.IsPlayerDead)
        {
            others.Add(host.Body.Position);
        }

        others.AddRange(_bySlot.Where(p => p.Key != slot && !p.Value.IsDead).Select(p => p.Value.Player.Body.Position));
        int team = _versus == Versus.Teams ? TeamOf(slot) : -1;
        Game.Multiplayer.Modes.MpSpawn? best = null;
        double farthest = double.MinValue;
        foreach (Game.Multiplayer.Modes.MpSpawn spawn in layout.SpawnsFor(team))
        {
            Vector3d at = new(spawn.Position[0], spawn.Position[1], spawn.Position[2]);
            double nearest = others.Count == 0 ? 0 : others.Min(o => Vector3d.Distance(o, at));
            if (nearest > farthest)
            {
                (best, farthest) = (spawn, nearest);
            }
        }

        return best is null
            ? null
            : start with { Position = new Vector3d(best.Position[0], best.Position[1], best.Position[2]), Gamma = best.Yaw * MathF.PI / 180f };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The players' rounds then meet each other always, an enemy's hurting and a team mate's not; nobody is held down for a
    /// revive, and a player put back is put at the level's start.
    /// </remarks>
    public bool TryPlayVersus(bool teams)
    {
        _versus = teams ? Versus.Teams : Versus.Everyone;
        return true;
    }

    /// <summary>Gets a value indicating whether the players fight each other rather than the level together.</summary>
    public bool IsVersus => _versus != Versus.None;

    // Whether a round from a player's gun hurts the player it met: always for a co-op room with friendly fire, and in a
    // versus match always but from a team mate.
    internal bool Hurts(ShotTally? by, IHarmedPlayer target)
    {
        if (_versus == Versus.None)
        {
            return CoopCVars.FriendlyFire.Value;
        }

        if (_versus == Versus.Everyone || by is null || EntityOf(by) is not { } shooter || EntityOf(target.Rig) is not { } struck)
        {
            return true;
        }

        return TeamOf(SlotOfEntity(shooter)) != TeamOf(SlotOfEntity(struck));
    }

    // Whether the players' rounds meet each other at all.
    internal bool PlayersMeetRounds => _versus != Versus.None || CoopCVars.FriendlyFire.Value;

    private static int SlotOfEntity(int entity) => entity == HostEntity ? ServerSession.HostSlot : entity - 1;

    private enum Versus
    {
        None,
        Teams,
        Everyone,
    }

    private Versus _versus;
    private readonly Dictionary<int, int> _teams = [];
}

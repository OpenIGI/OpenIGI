using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>
/// A co-op server's players as bodies a player's round can meet, while the room plays with friendly fire: each is struck
/// where the round crosses it and hurt by the hit handler a guard's round reaches it through. A round is never offered the
/// body it starts in, its shooter's.
/// </summary>
/// <param name="game">The level.</param>
/// <param name="session">The session's world, which says whether the players' rounds meet each other and whose hurt.</param>
internal sealed class CoopPlayerBodies(Igi1Gameplay game, Igi1ReplicatedWorld session) : IShootableBodies
{
    // A round starts at its shooter's eye, straight over its feet: a body whose feet are this near under the origin is the
    // shooter's own. Units: 25 cm, well inside the 55 cm two players' bodies keep apart (HumanSeparation). It was a 1 m square
    // about the origin, which took a player stood at another's back on a diagonal for the stabber's own body, and the knife
    // met nothing.
    private const double OwnBodyReach = 1024;

    // This pass's casts, for the shot log: each player's body offered, and what became of the cast.
    private readonly List<(World.IHarmedPlayer Player, string Outcome)> _pass = [];
    private string? _noBodies;

    /// <inheritdoc/>
    public IEnumerable<IShootableBody> Bodies()
    {
        _noBodies = !session.PlayersMeetRounds ? "players' rounds meet no players"
            : game.World.PlayerTask is null ? "no player task"
            : game.World.Soldiers is null ? "no soldiers"
            : null;
        if (!session.PlayersMeetRounds || game.World.PlayerTask is not { } task || game.World.Soldiers is not { } soldiers)
        {
            yield break;
        }

        foreach (World.IHarmedPlayer player in game.Harm.Players)
        {
            if (player.Rig is { } rig && !player.IsDead)
            {
                yield return new Body(game, session, soldiers, task, player, rig, this);
            }
        }
    }

    /// <inheritdoc/>
    public void RoundCast(Vector3d origin, Vector3d target, GameTask? shooter)
    {
        _pass.Clear();
        _noBodies = null;
    }

    /// <inheritdoc/>
    public void RoundPassed(Vector3d origin, Vector3d target, GameTask? struck)
    {
        if (Game.Multiplayer.NetCVars.ShotLog.Value)
        {
            session.NoteShotPass(origin, target, struck, _pass, _noBodies);
        }

        _pass.Clear();
    }

    private sealed class Body(
        Igi1Gameplay game, Igi1ReplicatedWorld session, SoldierSimulation soldiers, GameTask task, World.IHarmedPlayer player, Player.PlayerCamera rig, CoopPlayerBodies owner)
        : IShootableBody, IWeaponHitTarget
    {
        private HumanHit _met;

        public GameTask Task => task;

        public bool TryMeet(Vector3d origin, Vector3d target, out HumanHit hit)
        {
            hit = default;
            Vector3d feet = rig.Body.Position;
            double dx = origin.X - feet.X;
            double dy = origin.Y - feet.Y;
            if ((dx * dx) + (dy * dy) < OwnBodyReach * OwnBodyReach)
            {
                owner._pass.Add((player, "own body"));
                return false;
            }

            // The mission's own player is met in its own body; another player in the one it is drawn with, in its own stance.
            IReadOnlyList<HumanBodyCrossing> crossings;
            bool cast = ReferenceEquals(rig, game.World.Player)
                ? soldiers.TryCastPlayerBody(origin, target, feet, rig.Body.Yaw, out crossings)
                : soldiers.TryCastPartnerBody(player, origin, target, feet, rig.Body.Yaw, out crossings);
            if (!cast || crossings.Count == 0)
            {
                owner._pass.Add((player, cast ? "missed" : "no body to cast"));
                return false;
            }

            HumanBodyCrossing first = crossings[0];
            owner._pass.Add((player, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"met zone {first.Hit.Zone} at {first.Hit.Distance / 4096:F1} m")));
            hit = first.Hit with { Material = first.Material, IsExit = !first.Entering };
            _met = hit;
            return true;
        }

        // The same hit handler a guard's round reaches a player through, the round as the shooter fired it; a team mate's round
        // is stopped by the body and hurts nothing.
        public void WeaponHit(in WeaponHit hit)
        {
            Igi1WeaponHit round = hit.Context as Igi1WeaponHit ?? Igi1WeaponHit.FromShared(hit, _met);
            bool hurts = session.Hurts(round.Credit, player);
            float before = player.Health.Damage;
            if (hurts)
            {
                game.World.PlayerEvents?.TakeRound(player, round);
            }

            session.NoteRoundMet(round.Credit, hurts, player.Health.Damage - before);
        }
    }
}

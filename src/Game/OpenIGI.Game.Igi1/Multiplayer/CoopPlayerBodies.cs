using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>
/// A co-op server's players as bodies a player's round can meet, while the room plays with friendly fire: each is struck
/// where the round crosses it and hurt by the hit handler a guard's round reaches it through. A round is never offered the
/// body it starts in, its shooter's.
/// </summary>
/// <param name="game">The level.</param>
internal sealed class CoopPlayerBodies(Igi1Gameplay game) : IShootableBodies
{
    // A round starts at its shooter's eye: a body standing over the origin closer than this is the shooter's own. Units.
    private const double OwnBodyReach = 2048;

    /// <inheritdoc/>
    public IEnumerable<IShootableBody> Bodies()
    {
        if (!CoopCVars.FriendlyFire.Value || game.World.PlayerTask is not { } task || game.World.Soldiers is not { } soldiers)
        {
            yield break;
        }

        foreach (World.IHarmedPlayer player in game.Harm.Players)
        {
            if (player.Rig is { } rig && !player.IsDead)
            {
                yield return new Body(game, soldiers, task, player, rig);
            }
        }
    }

    /// <inheritdoc/>
    public void RoundPassed(Vector3d origin, Vector3d target, GameTask? struck)
    {
    }

    private sealed class Body(Igi1Gameplay game, SoldierSimulation soldiers, GameTask task, World.IHarmedPlayer player, Player.PlayerCamera rig)
        : IShootableBody, IWeaponHitTarget
    {
        private HumanHit _met;

        public GameTask Task => task;

        public bool TryMeet(Vector3d origin, Vector3d target, out HumanHit hit)
        {
            hit = default;
            Vector3d feet = rig.Body.Position;
            if (Math.Abs(origin.X - feet.X) < OwnBodyReach && Math.Abs(origin.Y - feet.Y) < OwnBodyReach)
            {
                return false;
            }

            if (!soldiers.TryCastPlayerBody(origin, target, feet, rig.Body.Yaw, out IReadOnlyList<HumanBodyCrossing> crossings) || crossings.Count == 0)
            {
                return false;
            }

            HumanBodyCrossing first = crossings[0];
            hit = first.Hit with { Material = first.Material, IsExit = !first.Entering };
            _met = hit;
            return true;
        }

        // The same hit handler a guard's round reaches a player through, the round as the shooter fired it.
        public void WeaponHit(in WeaponHit hit) =>
            game.World.PlayerEvents?.TakeRound(player, hit.Context as Igi1WeaponHit ?? Igi1WeaponHit.FromShared(hit, _met));
    }
}

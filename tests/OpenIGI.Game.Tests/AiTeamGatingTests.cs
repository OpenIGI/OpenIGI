using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A friendly soldier must never engage the player. Retail guards only fight humans on the other side:
/// the enemy the AI targets is the player, and every brain is told up front whether that player is hostile
/// via <see cref="AiSoldier.PlayerIsEnemy"/>. When the player shares the soldier's team, vision detection
/// and every player-origin noise event (gunshot, near-miss, impact, footstep) are refused, so the friendly
/// patrol stands down instead of aiming at and firing on us.
/// </summary>
public sealed class AiTeamGatingTests
{
    private const double Metre = 4096.0;

    private static AiSoldier Soldier(out Vector3d position, bool playerIsEnemy)
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
        position = graph.Nodes[0].Position;
        return new AiSoldier(graph, position, facing: 0f, phase: 0, seed: 1)
        {
            PlayerIsEnemy = playerIsEnemy,
        };
    }

    private static Vector3d Away(Vector3d from, double units) => new(from.X + units, from.Y, from.Z);

    /// <summary>
    /// A brain on the same team as the player (the friendly case) refuses the player's gunshot, so it never
    /// redirects its target or queues the hostile event that would lead to it firing on the player.
    /// </summary>
    [RequiresGameDataFact]
    public void ASameTeamSoldierIgnoresThePlayersGunshot()
    {
        AiSoldier friendly = Soldier(out Vector3d position, playerIsEnemy: false);

        Assert.False(friendly.HearGunshot(Away(position, 5 * Metre), silenced: false));
        Assert.Equal(0, friendly.Events.PendingCount(AiEvent.Gunshot));
    }

    /// <summary>
    /// The default brain (no team told, as every existing unit test constructs it) still treats the player as
    /// an enemy: an enemy soldier must keep hearing and engaging. This guards against a regression that would
    /// silently disarm every guard.
    /// </summary>
    [RequiresGameDataFact]
    public void AnEnemySoldierStillHearsThePlayersGunshot()
    {
        AiSoldier enemy = Soldier(out Vector3d position, playerIsEnemy: true);

        Assert.True(enemy.HearGunshot(Away(position, 5 * Metre), silenced: false));
        Assert.True(enemy.Events.PendingCount(AiEvent.Gunshot) > 0);
    }

    /// <summary>
    /// A same-team soldier is deaf to the PLAYER's gunshot (see above), but must still hear a hostile AI
    /// soldier firing nearby — it is on the player's side, not on the hostile soldier's. The origin gate
    /// therefore applies only to player-origin rounds; an AI-origin shot is delivered to every live soldier in
    /// earshot so a friendly can react to hostile gunfire.
    /// </summary>
    [RequiresGameDataFact]
    public void ASameTeamSoldierHearsHostileAiGunfire()
    {
        AiSoldier friendly = Soldier(out Vector3d position, playerIsEnemy: false);

        Assert.True(friendly.HearGunshot(Away(position, 5 * Metre), AiScriptApi.GunshotRange, fromPlayer: false));
        Assert.True(friendly.Events.PendingCount(AiEvent.Gunshot) > 0);
    }

    /// <summary>
    /// A same-team soldier refuses every player-origin noise event — the near-miss, the impact and the
    /// footstep — in the same way it refuses the gunshot, so no source of player noise redirects a friendly
    /// onto the player.
    /// </summary>
    [RequiresGameDataFact]
    public void ASameTeamSoldierIgnoresEveryPlayersNoiseEvent()
    {
        AiSoldier friendly = Soldier(out Vector3d position, playerIsEnemy: false);
        Vector3d nearby = Away(position, 3 * Metre);

        Assert.False(friendly.ReceiveGunshotMiss(nearby, nearby, shooterPosition: position));
        Assert.False(friendly.ReceiveGroundImpact(nearby, shooterPosition: position));
        Assert.False(friendly.ReceiveWalk(nearby, walkerPosition: position, radiusInUnits: 20 * Metre));
        Assert.Equal(0, friendly.Events.PendingCount(AiEvent.GroundImpact));
        Assert.Equal(0, friendly.Events.PendingCount(AiEvent.Walk));
    }

    /// <summary>
    /// The team gate also stops a same-team soldier from DETECTING the player in the first place. Run the
    /// vision sweep against a player stood directly in front of the friendly and confirm it never engages —
    /// the same check the earlier "friendlies shooting us" bug failed.
    /// </summary>
    [RequiresGameDataFact]
    public void ASameTeamSoldierNeverDetectsThePlayer()
    {
        AiSoldier friendly = Soldier(out Vector3d position, playerIsEnemy: false);
        AiSoldier enemy = Soldier(out Vector3d position2, playerIsEnemy: true);

        // A player stood five metres in front, well inside the cone and line of sight.
        Vector3d target = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        Vector3d target2 = new(position2.X, position2.Y + (5 * Metre), position2.Z + (5 * Metre / 4));

        for (int tick = 1; tick <= 60; tick++)
        {
            friendly.Tick(tick, target, rootMotionThisTick: 178.0);
            enemy.Tick(tick, target2, rootMotionThisTick: 178.0);
        }

        Assert.False(friendly.SeesEnemy, "a same-team soldier detected the player as an enemy");
        Assert.True(enemy.SeesEnemy, "an enemy soldier failed to detect the player");
    }
}

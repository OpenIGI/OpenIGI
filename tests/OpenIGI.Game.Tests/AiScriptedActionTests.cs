using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>End-to-end coverage for mission-script action bodies that are not ordinary combat.</summary>
public sealed class AiScriptedActionTests
{
    [Fact]
    public void FireAtTaskTracksTheTaskAndHonoursTheInterBurstDelay()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d target = new(position.X, position.Y + 4096, position.Z);
        int clip = 30;
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetActivateSupport(
            new AiActivationClaims(),
            (int id, out Vector3d found) =>
            {
                found = target;
                return id == 20;
            },
            _ => { });
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(clip, 30, 1, WeaponCategory.Gun));

        soldier.ApplyActions([
            new AiProducedAction(
                "AIAction_FireAtTask",
                [20, 1, 0, 0, 0],
                0,
                [20f, 1f, 0.5f, 0f, 0f]),
        ]);

        Vector3d irrelevantEnemy = new(position.X, position.Y - 100_000, position.Z);
        soldier.Tick(1, irrelevantEnemy, 0.0, enemyAlive: false);

        Assert.Equal(AiSoldierActivity.FiringAtTask, soldier.Activity);
        Assert.True(soldier.Combat.PullTrigger);
        Assert.Equal(target, soldier.Combat.TurnTo);

        // The body/weapon consumes the round; the AI notices the clip delta on the next tick and waits
        // the action's 0.5 seconds before starting another one-round burst.
        clip--;
        for (int tick = 2; tick <= 16; tick++)
        {
            soldier.Tick(tick, irrelevantEnemy, 0.0, enemyAlive: false);
            Assert.False(soldier.Combat.PullTrigger);
        }

        soldier.Tick(17, irrelevantEnemy, 0.0, enemyAlive: false);
        Assert.True(soldier.Combat.PullTrigger);
    }

    [Fact]
    public void FireAtTaskChoosesTheRequestedReloadRoute()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d target = new(position.X, position.Y + 4096, position.Z);
        AiSoldier soldier = SoldierWithEmptyWeapon(position, target);

        soldier.ApplyActions([
            new AiProducedAction("AIAction_FireAtTask", [20, 1, 0, 1, 0], 0),
        ]);
        soldier.Tick(1, target, 0.0, enemyAlive: false);
        Assert.True(soldier.Combat.PressReload);
        Assert.False(soldier.Combat.ReloadWeaponDirectly);
        Assert.Contains(soldier.RawInputWrites, write => write.Slot == 8 && write.Value == 1f);
        Assert.DoesNotContain(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.Stand);

        soldier.ApplyActions([
            new AiProducedAction("AIAction_FireAtTask", [20, 1, 0, 0, 0], 0),
        ]);
        soldier.Tick(2, target, 0.0, enemyAlive: false);
        Assert.False(soldier.Combat.PressReload);
        Assert.True(soldier.Combat.ReloadWeaponDirectly);
        Assert.DoesNotContain(soldier.RawInputWrites, write => write.Slot == 8);
    }

    [Fact]
    public void FallFlatHoldsProneThenRunsTheFollowingCombatAction()
    {
        Vector3d position = new(1000, 2000, 3000);
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(30, 30, 3, WeaponCategory.Gun));

        soldier.ApplyActions([
            new AiProducedAction("AIAction_FallFlat", [1, 0], 0, [1f, 0f]),
            new AiProducedAction("AIAction_SetCombat", [0], 0),
        ]);

        Vector3d enemy = new(position.X, position.Y - 100_000, position.Z);
        for (int tick = 1; tick <= 31; tick++)
        {
            soldier.Tick(tick, enemy, 0.0, enemyAlive: false);
            Assert.Equal(AiSoldierActivity.FallenFlat, soldier.Activity);
            Assert.Contains(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.LieDown);
        }

        soldier.Tick(32, enemy, 0.0, enemyAlive: false);
        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);
    }

    [Fact]
    public void ANonLethalHitPostsTakingDamageWithTheAttackerPosition()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d attacker = new(4000, 5000, 6000);
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1)
        {
            RuntimeId = 12,
        };

        soldier.ReceiveDamage(attacker);

        AiEventRecord? posted = soldier.Events.HeadOf(AiEvent.TakingDamage);
        Assert.NotNull(posted);
        Assert.Equal(attacker, posted.Value.Position);
        Assert.Equal(-1, posted.Value.Source);
    }

    [Fact]
    public void PlaySoundPreservesTheTextOperandAndAttachFlag()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        (string Name, bool Attached)? played = null;
        soldier.SetScriptSound((name, attached) => played = (name, attached));

        soldier.ApplyActions([
            new AiProducedAction(
                "AIAction_PlaySound",
                [0, 1, 4],
                4,
                [0f, 1f, 4f],
                ["guard_warning", "", ""]),
        ]);

        Assert.Equal(("guard_warning", true), played);
    }

    [Fact]
    public void StunnedHoldsAnimationThirtyEightForTheRequestedDuration()
    {
        Vector3d position = new(1000, 2000, 3000);
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.ApplyActions([
            new AiProducedAction("AIAction_Stunned", [1, 0], 0, [1f, 0f]),
        ]);

        Vector3d irrelevantEnemy = new(position.X, position.Y - 100_000, position.Z);
        for (int tick = 1; tick <= 31; tick++)
        {
            soldier.Tick(tick, irrelevantEnemy, 0.0, enemyAlive: false);
            Assert.Equal(AiSoldierActivity.Stunned, soldier.Activity);
            Assert.Equal(38, soldier.RequestedAnimation);
            Assert.Contains(
                soldier.ChannelWrites,
                write => write.Channel == AiCombatChannel.PlayAnimation && write.Value == 38.0f);
        }

        soldier.Tick(32, irrelevantEnemy, 0.0, enemyAlive: false);
        Assert.Equal(AiSoldierActivity.Idle, soldier.Activity);
        Assert.Equal(-1, soldier.RequestedAnimation);
    }

    [RequiresGameDataFact]
    public void RemovingAlarmActionsReleasesAnAlarmPanelClaim()
    {
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat")));
        Vector3d start = graph.Nodes[0].Position;
        Vector3d panel = graph.Nodes[1].Position;
        AiActivationClaims claims = new();
        AiSoldier soldier = new(graph, start, facing: 0f, phase: 0, seed: 1)
        {
            AlarmTriggerId = 20,
            AlarmControlId = 30,
        };
        soldier.SetActivateSupport(
            claims,
            (int id, out Vector3d found) =>
            {
                found = panel;
                return id == 20;
            },
            _ => { });

        soldier.ApplyActions([new AiProducedAction("AIAction_Activate", [20, 1, 0], 0)]);
        Assert.True(claims.Contains(20));
        Assert.True(claims.Contains(30));

        soldier.RemoveAlarmActions();

        Assert.False(claims.Contains(20));
        Assert.False(claims.Contains(30));
        Assert.Equal(AiSoldierActivity.Idle, soldier.Activity);
    }

    [RequiresGameDataFact]
    public void RunPanickingUsesItsArgumentAsAnEscapeRadius()
    {
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat")));
        GraphNode source = graph.Nodes.First(node => graph.LinksOf(node.Id).Count > 0);
        int expected = graph.LinksOf(source.Id)[0].Other(source.Id);

        int? destination = AiRunPanicking.FindDestination(
            graph, source.Position, source.Position, distanceMetres: 0.0);

        Assert.Equal(expected, destination);

        Vector3d alreadySafe = new(source.Position.X + 1.0, source.Position.Y, source.Position.Z);
        Assert.Null(AiRunPanicking.FindDestination(
            graph, alreadySafe, source.Position, distanceMetres: 0.0));
    }

    private static AiSoldier SoldierWithEmptyWeapon(Vector3d position, Vector3d target)
    {
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetActivateSupport(
            new AiActivationClaims(),
            (int id, out Vector3d found) =>
            {
                found = target;
                return id == 20;
            },
            _ => { });
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(0, 30, 1, WeaponCategory.Gun));
        return soldier;
    }
}

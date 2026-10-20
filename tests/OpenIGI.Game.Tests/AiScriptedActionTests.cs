using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>End-to-end coverage for mission-script action bodies that are not ordinary combat.</summary>
public sealed class AiScriptedActionTests
{
    // The archetypes' declared priority positions (their CREATE branches), and AIACTIONFLAG_PUSHABLE.
    private const int FlashbangPosition = 2;
    private const int CombatPosition = 5;
    private const int GunshotMissPosition = 10;
    private const int Pushable = (int)AiActionFlagWord.Pushable;

    [Fact]
    public void FireAtTaskTracksTheTaskAndHonoursTheInterBurstDelay()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d target = new(position.X, position.Y + 4096, position.Z);
        int clip = 30;
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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
        Igi1HumanAi soldier = SoldierWithEmptyWeapon(position, target);

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
    public void FallFlatHoldsProneForItsDuration()
    {
        Vector3d position = new(1000, 2000, 3000);
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.ApplyActions([new AiProducedAction("AIAction_FallFlat", [1, 0], GunshotMissPosition, [1f, 0f])]);

        Vector3d enemy = new(position.X, position.Y - 100_000, position.Z);
        for (int tick = 1; tick <= 31; tick++)
        {
            soldier.Tick(tick, enemy, 0.0, enemyAlive: false);
            Assert.Equal(AiSoldierActivity.FallenFlat, soldier.Activity);
            Assert.Contains(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.LieDown);
        }

        soldier.Tick(32, enemy, 0.0, enemyAlive: false);
        Assert.Equal(AiSoldierActivity.Idle, soldier.Activity);
        Assert.False(soldier.PendingActions.HasPending);
    }

    [Fact]
    public void TheCombatASetCombatPostsSuspendsTheFallFlatItFollowed()
    {
        // guard.qsc's GUNSHOTMISS branch: FallFlat, then one time in ten SetCombat. SetCombat posts COMBAT
        // and ends in its own start (0x48E560), leaving the fall on the stack; COMBAT outranks the miss, so
        // the next pump installs the fight. 0x450F10 gives every action the flag word of the LAST one named
        // -- SetCombat's pushable -- so the fall is not ended but suspended beneath the fight.
        Vector3d position = new(1000, 2000, 3000);
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(30, 30, 3, WeaponCategory.Gun));

        soldier.ApplyActions([
            new AiProducedAction("AIAction_FallFlat", [1, 0], GunshotMissPosition, [1f, 0f]),
            new AiProducedAction("AIAction_SetCombat", [Pushable], GunshotMissPosition),
        ]);
        Assert.Equal(["AIAction_FallFlat"], soldier.PendingActions.Actions.Select(action => action.Name));

        soldier.Tick(1, new Vector3d(position.X, position.Y - 100_000, position.Z), 0.0);

        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);
        Assert.DoesNotContain(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.LieDown);
        Assert.Equal(
            ["AIAction_Combat", "AIAction_FallFlat"],
            soldier.PendingActions.Actions.Select(action => action.Name));
    }

    [Fact]
    public void ANonLethalHitPostsTakingDamageWithTheAttackerPosition()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d attacker = new(4000, 5000, 6000);
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1)
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
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
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
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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

    [Fact]
    public void AReactionEndsANonPushableCombatAction()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(30, 30, 1, WeaponCategory.Gun));

        soldier.ApplyActions([new AiProducedAction("AIAction_Combat", [0], CombatPosition)]);
        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);

        soldier.ApplyActions([new AiProducedAction("AIAction_PlayAnimation", [39, 0], FlashbangPosition)]);

        Assert.Equal(AiSoldierActivity.Idle, soldier.Activity);
        Assert.Equal(AiCombatPhase.Ended, soldier.CombatPhase);
        Assert.Equal(39, soldier.RequestedAnimation);
        Assert.Equal(["AIAction_PlayAnimation"], soldier.PendingActions.Actions.Select(action => action.Name));
    }

    [Fact]
    public void AReactionSuspendsAPushableCombatActionWhichResumesAfterIt()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(30, 30, 1, WeaponCategory.Gun));
        Vector3d enemy = new(0, 5 * 4096, 0);

        soldier.ApplyActions([new AiProducedAction("AIAction_Combat", [Pushable], CombatPosition)]);
        soldier.ApplyActions([new AiProducedAction("AIAction_PlayAnimation", [39, 0], FlashbangPosition)]);

        Assert.Equal(
            ["AIAction_PlayAnimation", "AIAction_Combat"],
            soldier.PendingActions.Actions.Select(action => action.Name));
        Assert.NotEqual(AiCombatPhase.Ended, soldier.CombatPhase);

        soldier.Tick(1, enemy, 0.0);
        Assert.Equal(AiSoldierActivity.Idle, soldier.Activity);
        Assert.Equal(39, soldier.RequestedAnimation);
        Assert.False(soldier.Combat.PullTrigger);

        // The clip ends: its action finishes on its next run, and the fight beneath runs the tick after.
        soldier.ReportAnimationFinished();
        soldier.Tick(2, enemy, 0.0);
        Assert.Equal(-1, soldier.RequestedAnimation);
        Assert.Equal(["AIAction_Combat"], soldier.PendingActions.Actions.Select(action => action.Name));

        soldier.Tick(3, enemy, 0.0);
        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);
    }

    [RequiresGameDataFact]
    public void RetailGuardFlashbangBranchSuspendsAnActiveEngagement()
    {
        string guardPath = Path.Combine(GameData.RequireRoot(), "common", "ai", "guard.qvm");
        QvmProgram guard = QvmProgram.Read(File.ReadAllBytes(guardPath));
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(30, 30, 1, WeaponCategory.Gun));
        soldier.SetScript(new AiScriptHost(RetailAiNatives()), guard);

        // The first tick dispatches the archetype's creation, which declares the events it hears; the fight
        // is then what guard.qsc's COMBAT branch installs -- pushable, at COMBAT's position.
        soldier.Tick(1, new Vector3d(0, -100_000, 0), 0.0, enemyAlive: false);
        soldier.ApplyActions([new AiProducedAction("AIAction_Combat", [Pushable], CombatPosition)]);

        Vector3d flash = new(0, 4 * 4096, soldier.EyePosition().Z);
        Assert.True(soldier.ReceiveFlashbang(flash));

        soldier.Tick(2, new Vector3d(0, -100_000, 0), 0.0);

        Assert.Equal(AiSoldierActivity.Idle, soldier.Activity);
        Assert.NotEqual(AiCombatPhase.Ended, soldier.CombatPhase);
        Assert.Equal(39, soldier.RequestedAnimation);
        Assert.False(soldier.Combat.PullTrigger);

        // Beneath the fight, the pushable AIAction_Idle(1) the IDLE branch installed on the first tick.
        Assert.Equal(
            ["AIAction_PlayAnimation", "AIAction_Combat", "AIAction_Idle"],
            soldier.PendingActions.Actions.Select(action => action.Name));
    }

    [RequiresGameDataFact]
    public void RetailGuardFlashbangInterruptsCombatWhileDetectionContinues()
    {
        string guardPath = Path.Combine(GameData.RequireRoot(), "common", "ai", "guard.qvm");
        QvmProgram guard = QvmProgram.Read(File.ReadAllBytes(guardPath));
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(30, 30, 1, WeaponCategory.Gun));
        soldier.SetScript(new AiScriptHost(RetailAiNatives()), guard);

        Vector3d feet = new(0, 5 * 4096, 0);
        Vector3d enemy = new(feet.X, feet.Y, feet.Z + (5 * 4096 / 4));
        for (int tick = 1; tick <= 30 && soldier.Activity != AiSoldierActivity.Engaging; tick++)
        {
            soldier.Tick(tick, enemy, 0.0, enemyFeet: feet);
        }

        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);

        Vector3d flash = new(0, 4 * 4096, soldier.EyePosition().Z);
        Assert.True(soldier.ReceiveFlashbang(flash));
        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.Flashbang));

        soldier.Tick(31, enemy, 0.0, enemyFeet: feet);

        Assert.True(
            soldier.Activity == AiSoldierActivity.Idle,
            $"flashbang drained={soldier.Events.DrainedCount(AiEvent.Flashbang)} "
                + $"pending={soldier.Events.PendingCount(AiEvent.Flashbang)} "
                + $"phase={soldier.CombatPhase} anim={soldier.RequestedAnimation} "
                + $"activity={soldier.Activity}");
        Assert.NotEqual(AiCombatPhase.Ended, soldier.CombatPhase);
        Assert.Equal(39, soldier.RequestedAnimation);
        Assert.False(soldier.Combat.PullTrigger);

        // The reaction owns more than the one dispatch tick: the fight beneath it does not fire until the
        // renderer reports clip 39 complete, and nothing ranked below FLASHBANG gets through meanwhile.
        for (int tick = 32; tick <= 40; tick++)
        {
            soldier.Tick(tick, enemy, 0.0, enemyFeet: feet);
            Assert.Equal(AiSoldierActivity.Idle, soldier.Activity);
            Assert.Equal(39, soldier.RequestedAnimation);
            Assert.False(soldier.Combat.PullTrigger);
        }

        // The flinch over, the same engagement carries on -- it was suspended, not ended.
        soldier.ReportAnimationFinished();
        soldier.Tick(41, enemy, 0.0, enemyFeet: feet);
        Assert.Equal(-1, soldier.RequestedAnimation);
        soldier.Tick(42, enemy, 0.0, enemyFeet: feet);
        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);
    }

    [RequiresGameDataFact]
    public void RetailGuardGrenadeLandDispatchesThePanicBranchWhileDetectionContinues()
    {
        string root = GameData.RequireRoot();
        QvmProgram guard = QvmProgram.Read(File.ReadAllBytes(Path.Combine(root, "common", "ai", "guard.qvm")));
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            root, "missions", "location0", "level1", "graphs", "graph4019.dat")));
        Vector3d start = graph.Nodes[0].Position;
        bool sawPanic = false;

        // guard.qvm follows the retail random gate: grenade-land panics are not unconditional. Try
        // independent IGI streams until the authored branch is selected, while requiring every
        // successful attempt to have gone through the real event queue and combat-visible tick.
        for (uint seed = 1; seed <= 128 && !sawPanic; seed++)
        {
            Igi1HumanAi soldier = new(graph, start, facing: 0f, phase: 0, seed: (int)seed);
            soldier.SetCombat(
                AiType.GuardAk,
                new AiCombatParameters(new HumanAiConfigItemTask()),
                () => new AiCombatWeaponState(30, 30, 1, WeaponCategory.Gun));
            soldier.SetScript(
                new AiScriptHost(RetailAiNatives(), random: new IgiRandom(seed)),
                guard);

            Vector3d feet = new(start.X, start.Y + (5 * 4096), start.Z);
            for (int tick = 1; tick <= 30 && soldier.Activity != AiSoldierActivity.Engaging; tick++)
            {
                soldier.Tick(tick, feet, 0.0, enemyFeet: feet);
            }

            if (soldier.Activity != AiSoldierActivity.Engaging)
            {
                continue;
            }

            Assert.True(soldier.ReceiveGrenadeLand(start));
            soldier.Tick(31, feet, 0.0, enemyFeet: feet);

            sawPanic = soldier.Events.DrainedCount(AiEvent.GrenadeLand) == 1
                && soldier.Activity == AiSoldierActivity.Panicking
                && !soldier.Combat.PullTrigger;
        }

        Assert.True(sawPanic, "The retail grenade-land branch never produced a running panic reaction.");
    }

    [RequiresGameDataTheory]
    [InlineData(CombatPosition, true)]
    [InlineData((int)AiEvent.AlarmOn, false)]
    public void RemovingAlarmActionsEndsWhatTheNinthDeclaredEventsIdInstalled(int installedAt, bool survives)
    {
        // 0x44DBC0 compares each action's priority POSITION with the event TYPE at order[8]. A soldier with
        // no script hears every type in type order, so order[8] is ALARMON (8): what was installed at
        // position 8 ends, and an alarm run installed from COMBAT keeps its claims.
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat")));
        Vector3d start = graph.Nodes[0].Position;
        Vector3d panel = graph.Nodes[1].Position;
        AiActivationClaims claims = new();
        Igi1HumanAi soldier = new(graph, start, facing: 0f, phase: 0, seed: 1)
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

        soldier.ApplyActions([new AiProducedAction("AIAction_Activate", [20, 1, Pushable], installedAt)]);
        Assert.True(claims.Contains(20));
        Assert.True(claims.Contains(30));

        soldier.RemoveAlarmActions();

        Assert.Equal(survives, claims.Contains(20));
        Assert.Equal(survives, claims.Contains(30));
        Assert.Equal(survives, soldier.PendingActions.HasPending);
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

    private static NativeRegistry RetailAiNatives()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        natives.RegisterConstant("GAME_FREQUENCY", 30);
        natives.RegisterRealConstant("WORLD_METER", 4096.0);
        natives.RegisterRealConstant("MATH_PI", Math.PI);
        return natives;
    }

    private static Igi1HumanAi SoldierWithEmptyWeapon(Vector3d position, Vector3d target)
    {
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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

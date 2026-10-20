using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Runtime;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

public sealed class GuardGeneratorTests
{
    [Fact]
    public void FirstSpawnAndRespawnsConsumeTheAuthoredCap()
    {
        GuardGeneratorTask generator = new() { GenerateGuards = "1", MaxSpawns = 3 };

        Assert.Equal(GuardSpawnDecision.FirstSpawn, generator.Advance(isOn: true, childIsDead: false));
        Assert.Equal(1, generator.SpawnCount);
        Assert.True(generator.IsSpawned);

        Assert.Equal(GuardSpawnDecision.None, generator.Advance(isOn: true, childIsDead: false));
        Assert.Equal(GuardSpawnDecision.Respawn, generator.Advance(isOn: true, childIsDead: true));
        Assert.Equal(GuardSpawnDecision.Respawn, generator.Advance(isOn: true, childIsDead: true));
        Assert.Equal(3, generator.SpawnCount);

        Assert.Equal(GuardSpawnDecision.None, generator.Advance(isOn: true, childIsDead: true));
        Assert.Equal(3, generator.SpawnCount);
    }

    [Fact]
    public void ADisabledGeneratorDoesNotConsumeASpawn()
    {
        GuardGeneratorTask generator = new() { GenerateGuards = "0", MaxSpawns = 4 };

        Assert.Equal(GuardSpawnDecision.None, generator.Advance(isOn: false, childIsDead: false));
        Assert.False(generator.IsOn);
        Assert.False(generator.IsSpawned);
        Assert.Equal(0, generator.SpawnCount);
    }

    [Fact]
    public void NativeCounterNamesAreLiveScriptBindings()
    {
        GameLoop loop = new(new ManualTimeSource());
        AiTaskTypes.RegisterAll(loop.Types);
        GuardGeneratorTask generator = Assert.IsType<GuardGeneratorTask>(
            loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(GuardGeneratorTask.TypeName)));
        Assert.True(loop.Tasks.TryBindId(600, generator));
        Assert.True(loop.Variables.Export(generator));

        generator.MaxSpawns = 4;
        generator.Advance(isOn: true, childIsDead: false);

        Assert.True(loop.Variables.TryRead("GuardGenerator_600.isOn", out QvmValue isOn));
        Assert.True(loop.Variables.TryRead("GuardGenerator_600.isSpawned", out QvmValue isSpawned));
        Assert.True(loop.Variables.TryRead("GuardGenerator_600.nSpawns", out QvmValue count));
        Assert.Equal(1, isOn.ToInt32());
        Assert.Equal(1, isSpawned.ToInt32());
        Assert.Equal(1, count.ToInt32());
    }

    [Fact]
    public void RespawnedBrainKeepsLevelWiringButDropsDeathState()
    {
        AiCombatParameters parameters = new(new HumanAiConfigItemTask());
        Igi1HumanAi dead = new(graph: null, new Vector3d(1, 2, 3), facing: 0.25f, phase: 2, seed: 2)
        {
            RuntimeId = 3,
            PlayerIsEnemy = false,
        };
        dead.SetCombat(
            AiType.GuardAk,
            parameters,
            () => new AiCombatWeaponState(30, 30, 3, WeaponCategory.Gun));
        dead.Kill();

        Vector3d authored = new(10, 20, 30);
        Igi1HumanAi fresh = dead.Respawned(
            authored,
            0.5f,
            _ => new AiCombatWeaponState(30, 30, 3, WeaponCategory.Gun));

        Assert.NotSame(dead, fresh);
        Assert.Equal(AiSoldierActivity.Dead, dead.Activity);
        Assert.Equal(AiSoldierActivity.Idle, fresh.Activity);
        Assert.Equal(authored, fresh.Position);
        Assert.Equal(0.5f, fresh.Facing);
        Assert.Equal(dead.RuntimeId, fresh.RuntimeId);
        Assert.False(fresh.PlayerIsEnemy);
        Assert.Same(parameters, fresh.CombatParameters);
    }
}

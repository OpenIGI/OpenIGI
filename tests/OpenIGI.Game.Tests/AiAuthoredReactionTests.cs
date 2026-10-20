using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>Regression coverage for mission QVMs that intentionally override ordinary detection combat.</summary>
public sealed class AiAuthoredReactionTests
{
    private const double Metre = 4096.0;

    /// <summary>Level 14's staged actors see the player but explicitly ignore both detection events.</summary>
    [RequiresGameDataTheory]
    [InlineData(557)]
    [InlineData(750)]
    [InlineData(751)]
    public void Level14DetectionSuppressionsRemainAuthoritative(int taskId)
    {
        NativeRegistry natives = Natives();
        int stage = 0;
        natives.RegisterVariable(
            "EditVariable_1556.nValue",
            new QvmIntegerVariable(() => stage, value => stage = value));

        Igi1HumanAi soldier = Soldier();
        soldier.SetScript(new AiScriptHost(natives), MissionScript(14, taskId), Archetype("guard"));

        Vector3d far = Enemy(soldier, 500.0);
        soldier.Tick(1, far, rootMotionThisTick: 0.0); // CREATE, including authored short ranges.

        // Exercise the reaction contract independently of 750/751's staged short view ranges.
        soldier.Vision.Primary = AiViewCone.DefaultPrimary;
        soldier.Vision.Secondary = AiViewCone.DefaultSecondary;

        bool visible = false;
        bool combatAction = false;
        Vector3d near = Enemy(soldier, 5.0);
        for (int tick = 2; tick <= 90; tick++)
        {
            soldier.Tick(tick, near, rootMotionThisTick: 0.0);
            visible |= soldier.SeesEnemy;
            combatAction |= soldier.LastActions.Any(action =>
                action.Name is "AIAction_SetCombat" or "AIAction_Combat");
        }

        Assert.True(visible, $"Level 14 task {taskId} never reached its visible reaction path");
        Assert.False(combatAction, $"Level 14 task {taskId} bypassed its authored no-op branch");
        Assert.NotEqual(AiSoldierActivity.Engaging, soldier.Activity);
    }

    /// <summary>Level 6 civilians keep their authored panic response instead of being forced into combat.</summary>
    [RequiresGameDataTheory]
    [InlineData(526, 754)]
    [InlineData(532, 758)]
    [InlineData(533, 757)]
    public void Level6DetectionRunsTheAuthoredPanic(int taskId, int variableId)
    {
        NativeRegistry natives = Natives();
        int detected = 0;
        natives.RegisterVariable(
            $"EditVariable_{variableId}.nValue",
            new QvmIntegerVariable(() => detected, value => detected = value));
        // Tasks 526 and 533 also contain a friendly-detection write to 758.
        if (variableId != 758)
        {
            int other = 0;
            natives.RegisterVariable(
                "EditVariable_758.nValue",
                new QvmIntegerVariable(() => other, value => other = value));
        }

        Igi1HumanAi soldier = Soldier();
        soldier.SetScript(new AiScriptHost(natives), MissionScript(6, taskId), Archetype("civilian"));

        Vector3d far = Enemy(soldier, 500.0);
        soldier.Tick(1, far, rootMotionThisTick: 0.0); // CREATE

        bool visible = false;
        bool panicAction = false;
        bool engaged = false;
        Vector3d near = Enemy(soldier, 5.0);
        for (int tick = 2; tick <= 90; tick++)
        {
            soldier.Tick(tick, near, rootMotionThisTick: 0.0);
            visible |= soldier.SeesEnemy;
            panicAction |= soldier.LastActions.Any(action => action.Name == "AIAction_RunPanicking");
            engaged |= soldier.Activity == AiSoldierActivity.Engaging;
        }

        Assert.True(visible, $"Level 6 task {taskId} never detected the target");
        Assert.True(panicAction, $"Level 6 task {taskId} did not run its panic branch");
        Assert.Equal(1, detected);
        Assert.False(engaged, $"Level 6 task {taskId} was forced into combat over its panic script");
    }

    private static Igi1HumanAi Soldier() => new(
        graph: null,
        position: new Vector3d(0, 0, 0),
        facing: 0f,
        phase: 0,
        seed: 1);

    private static Vector3d Enemy(Igi1HumanAi soldier, double metres) => new(
        soldier.Position.X,
        soldier.Position.Y + (metres * Metre),
        soldier.Position.Z + Igi1HumanAi.HumanSeparationEyeHeight);

    private static NativeRegistry Natives()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        natives.RegisterConstant("GAME_FREQUENCY", 30);
        natives.RegisterRealConstant("WORLD_METER", Metre);
        natives.RegisterRealConstant("MATH_PI", Math.PI);
        return natives;
    }

    private static QvmProgram MissionScript(int level, int taskId) => Read(Path.Combine(
        GameData.RequireRoot(), "missions", "location0", $"level{level}", "ai", $"{taskId}.qvm"));

    private static QvmProgram Archetype(string name) => Read(Path.Combine(
        GameData.RequireRoot(), "common", "ai", $"{name}.qvm"));

    private static QvmProgram Read(string path) => QvmProgram.Read(File.ReadAllBytes(path));
}

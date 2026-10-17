using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Weapons;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>
/// <c>AIAction_Combat</c>'s dispatch by <c>AIType</c> — <c>0x44D160</c>.
/// </summary>
public sealed class AiCombatArmTests
{
    [Fact]
    public void StandingOnNodeUsesTheAuthoredOrientedSquare()
    {
        const float gamma = MathF.PI / 4.0f;
        GraphNode node = new(1, Vector3d.Zero, gamma, 1.0f, -1, string.Empty, GraphNodeCriteria.None);

        // Local (1.2 m, 1.2 m) is inside the radius+0.3 square. Rotating it into world space puts it
        // about 1.70 m from the centre, proving this is not the old circular approximation.
        double coordinate = 1.2 * GraphNode.MetreInUnits;
        Vector3d inside = new(0.0, Math.Sqrt(2.0) * coordinate, 0.0);
        Assert.True(AiSoldier.IsInsideNodeBox(inside, node, devianceMetres: 0.3));

        double outsideCoordinate = 1.31 * GraphNode.MetreInUnits;
        Vector3d outside = new(0.0, Math.Sqrt(2.0) * outsideCoordinate, 0.0);
        Assert.False(AiSoldier.IsInsideNodeBox(outside, node, devianceMetres: 0.3));
    }

    [Theory]
    [InlineData(AiType.Sniper)]
    [InlineData(AiType.Priboi)]
    [InlineData(AiType.GuardUzi)]
    [InlineData(AiType.GuardAk)]
    [InlineData(AiType.GuardSpas)]
    [InlineData(AiType.GuardPistol)]
    [InlineData(AiType.MafiaGuardUzi)]
    [InlineData(AiType.MafiaGuardAk)]
    [InlineData(AiType.MafiaGuardSpas)]
    [InlineData(AiType.SpetnazGuardUzi)]
    [InlineData(AiType.SpetnazGuardAk)]
    [InlineData(AiType.SpetnazGuardSpas)]
    public void TheSniperPriboiAndEveryGuardStandAndFight(AiType type) =>
        Assert.Equal(AiCombatArm.Guard, AiCombatArms.For(type));

    [Theory]
    [InlineData(AiType.PatrolUzi)]
    [InlineData(AiType.PatrolAk)]
    [InlineData(AiType.PatrolSpas)]
    [InlineData(AiType.PatrolPistol)]
    [InlineData(AiType.SecurityPatrolUzi)]
    [InlineData(AiType.SecurityPatrolSpas)]
    [InlineData(AiType.MafiaPatrolUzi)]
    [InlineData(AiType.MafiaPatrolAk)]
    [InlineData(AiType.MafiaPatrolSpas)]
    [InlineData(AiType.SpetnazPatrolUzi)]
    [InlineData(AiType.SpetnazPatrolAk)]
    [InlineData(AiType.SpetnazPatrolSpas)]
    public void EveryPatrolTypeMovesAndFights(AiType type) =>
        Assert.Equal(AiCombatArm.Patrol, AiCombatArms.For(type));

    [Fact]
    public void TheCivilianHasNoCombatArm() =>
        Assert.Equal(AiCombatArm.None, AiCombatArms.For(AiType.Civilian));

    [Fact]
    public void TheSpecialistsGetTheirOwnArms()
    {
        Assert.Equal(AiCombatArm.Rpg, AiCombatArms.For(AiType.Rpg));
        Assert.Equal(AiCombatArm.Gunner, AiCombatArms.For(AiType.Gunner));
        Assert.Equal(AiCombatArm.NamedCharacter, AiCombatArms.For(AiType.Anya));
        Assert.Equal(AiCombatArm.NamedCharacter, AiCombatArms.For(AiType.Ekk));
    }

    [Fact]
    public void CivilianCombatActionInstallsNoGenericGuardMachine()
    {
        Vector3d position = new(1000, 2000, 3000);
        AiSoldier civilian = Soldier(AiType.Civilian, position);
        civilian.ApplyActions([new AiProducedAction("AIAction_Combat", [0], 0)]);

        Assert.Equal(AiCombatArm.None, civilian.Arm);
        Assert.Equal(AiSoldierActivity.Idle, civilian.Activity);
    }

    [Theory]
    [InlineData(AiType.Anya)]
    [InlineData(AiType.Ekk)]
    public void NamedCharactersUseThePatrolDecisionMachine(AiType type)
    {
        AiSoldier named = Soldier(type, new Vector3d(1000, 2000, 3000));
        Assert.NotNull(named.Patrol);
    }

    /// <summary>The faction prefix means nothing; GUARD against PATROL is what decides.</summary>
    [Fact]
    public void TheFactionPrefixDoesNotChangeTheArm()
    {
        Assert.Equal(AiCombatArms.For(AiType.GuardAk), AiCombatArms.For(AiType.MafiaGuardAk));
        Assert.Equal(AiCombatArms.For(AiType.GuardAk), AiCombatArms.For(AiType.SpetnazGuardAk));
        Assert.Equal(AiCombatArms.For(AiType.PatrolAk), AiCombatArms.For(AiType.MafiaPatrolAk));
        Assert.Equal(AiCombatArms.For(AiType.PatrolAk), AiCombatArms.For(AiType.SpetnazPatrolAk));
    }

    /// <summary>
    /// The correction that matters: more of the roster moves and fights than stands and fights.
    /// </summary>
    [Fact]
    public void MostOfTheRosterIsAPatrolType()
    {
        int patrol = 0;
        int guard = 0;
        foreach (AiType type in Enum.GetValues<AiType>())
        {
            switch (AiCombatArms.For(type))
            {
                case AiCombatArm.Patrol: patrol++; break;
                case AiCombatArm.Guard: guard++; break;
                default: break;
            }
        }

        Assert.Equal(12, patrol);
        Assert.Equal(12, guard);

        // Every one of those patrol types used to be given the guard arm.
        Assert.True(patrol > 0);
    }

    private static AiSoldier Soldier(AiType type, Vector3d position)
    {
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            type,
            new AiCombatParameters(new HumanAiConfigItemTask()),
            () => new AiCombatWeaponState(30, 30, 3, WeaponCategory.Gun));
        return soldier;
    }
}

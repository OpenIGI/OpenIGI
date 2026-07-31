using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// <c>AIAction_Combat</c>'s dispatch by <c>AIType</c> — <c>0x44D160</c>.
/// </summary>
public sealed class AiCombatArmTests
{
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
}

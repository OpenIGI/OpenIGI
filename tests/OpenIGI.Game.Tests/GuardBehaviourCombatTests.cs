using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A behaviour in a real guard's fight: told once it is over, and able to end it from there without ending it again.</summary>
public sealed class GuardBehaviourCombatTests : AiSoldierCombatWiringFixture
{
    [RequiresGameDataFact]
    public void A_behaviour_that_ends_the_fight_as_it_hears_it_ended_is_told_once()
    {
        Igi1HumanAi soldier = Soldier(out Vector3d position);
        Ending ending = new();
        GuardControl? control = null;
        soldier.Attach(
            handed =>
            {
                control = handed;
                ending.Control = handed;
                return ending;
            },
            NullLogger.Instance);
        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));

        for (int tick = 1; tick <= 300 && !soldier.HasCombatRecord; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            Spend(soldier);
        }

        Assert.True(soldier.HasCombatRecord, "the soldier never took up the fight");
        control!.EndFight();

        Assert.Equal(1, ending.Ended);
        Assert.False(soldier.HasCombatRecord);
    }

    private sealed class Ending : IGuardCombat
    {
        public GuardControl? Control { get; set; }

        public int Ended { get; private set; }

        public void CombatEnded()
        {
            Ended++;
            Control?.EndFight();
        }
    }
}

using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A guard's script written from its orders, read back as either game's guard's orders are read.</summary>
public sealed class AiGuardScriptTests
{
    /// <summary>Both games lay a guard's script out as one event switch, so what is written is what is read, squad or none.</summary>
    /// <param name="squads">Whether the guard runs in a squad, as I.G.I. 2's do.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_written_script_says_the_orders_it_was_written_from(bool squads)
    {
        AiGuardOrders orders = new()
        {
            PatrolPathId = 2405,
            PatrolStart = 2,
            AlarmPatrolPathId = 2605,
            AlarmPatrolStart = 1,
            AlarmControlId = 1031,
            AlarmTriggerId = 1032,
            GunnerId = 77,
            AlarmAccess = AiAlarmAccess.AfterCombat,
            ViewLength = 120f,
            ViewGamma = 45f,
            IsInvulnerable = true,
        };

        AiGuardOrders read = AiGuardScript.Read(QvmProgram.Read(AiGuardScript.Build(orders, squads)));

        Assert.Equal(orders, read);
    }

    /// <summary>A guard told nothing gets a script that says nothing: the default handler for every event.</summary>
    [Fact]
    public void Orders_that_say_nothing_write_a_script_that_says_nothing()
    {
        Assert.True(AiGuardScript.Read(QvmProgram.Read(AiGuardScript.Build(AiGuardOrders.None, squads: true))).IsEmpty);
        Assert.True(AiGuardScript.Read(QvmProgram.Read(AiGuardScript.Build(AiGuardOrders.None, squads: false))).IsEmpty);
    }
}

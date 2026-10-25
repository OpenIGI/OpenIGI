using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A soldier's script written from its orders, read back as a guard's orders are read off Project I.G.I.'s.</summary>
public sealed class Igi2SoldierScriptTests
{
    /// <summary>Both games lay a soldier's script out as one event switch, so what is written is what is read.</summary>
    [Fact]
    public void A_written_script_says_the_orders_it_was_written_from()
    {
        Igi2SoldierOrders orders = new()
        {
            PatrolPathId = 2405,
            PatrolStart = 2,
            AlarmPatrolPathId = 2605,
            AlarmPatrolStart = 1,
            GunnerId = 77,
            AlarmAccess = AiAlarmAccess.AfterCombat,
            ViewLength = 120f,
            ViewGamma = 45f,
            IsInvulnerable = true,
        };

        Igi1GuardOrders read = Igi1GuardScript.Read(QvmProgram.Read(Igi2SoldierScript.Build(orders)));

        Assert.Equal(orders.PatrolPathId, read.PatrolPathId);
        Assert.Equal(orders.PatrolStart, read.PatrolStart);
        Assert.Equal(orders.AlarmPatrolPathId, read.AlarmPatrolPathId);
        Assert.Equal(orders.AlarmPatrolStart, read.AlarmPatrolStart);
        Assert.Equal(orders.GunnerId, read.GunnerId);
        Assert.Equal(orders.AlarmAccess, read.AlarmAccess);
        Assert.Equal(orders.ViewLength, read.ViewLength);
        Assert.Equal(orders.ViewGamma, read.ViewGamma);
        Assert.Equal(orders.IsInvulnerable, read.IsInvulnerable);
    }

    /// <summary>A soldier told nothing gets a script that says nothing: the default handler for every event.</summary>
    [Fact]
    public void Orders_that_say_nothing_write_a_script_that_says_nothing()
    {
        Assert.True(Igi1GuardScript.Read(QvmProgram.Read(Igi2SoldierScript.Build(new Igi2SoldierOrders()))).IsEmpty);
    }
}

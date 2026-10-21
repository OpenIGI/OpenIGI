using OpenIGI.Game.Player;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

public sealed class BinocularSweepTests
{
    [Fact]
    public void RetailSweepUsesTwelveColumnsAtTheRecoveredNormalizedPositions()
    {
        BinocularSweep sweep = new();

        IReadOnlyList<BinocularSweepColumn> columns = sweep.Tick(0);

        Assert.Equal(BinocularSweep.ColumnCount, columns.Count);
        Assert.Equal(0.125f, columns[0].X, 5);
        Assert.Equal(0.015625f, columns[1].X - columns[0].X, 5);
        Assert.Equal(0.00625f, columns[0].Width, 5);
        Assert.All(columns, column =>
        {
            Assert.InRange(column.Phase, 0f, 1f);
            Assert.Equal(0.8875f - (column.Phase * 0.125f), column.Y, 5);
            Assert.Equal(column.Phase * 0.125f, column.Height, 5);
        });
    }

    [Fact]
    public void RetailSweepDoesNotAdvanceAgainWhenTheRenderRepeatsASimulationTick()
    {
        BinocularSweep sweep = new();

        IReadOnlyList<BinocularSweepColumn> first = sweep.Tick(42);
        IReadOnlyList<BinocularSweepColumn> repeated = sweep.Tick(42);

        Assert.Equal(first, repeated);
    }

    [Fact]
    public void RetailSweepRestartsFromTheFixedNativeRandomSeed()
    {
        BinocularSweep sweep = new();
        IReadOnlyList<BinocularSweepColumn> expected = sweep.Tick(0);

        sweep.Tick(1);
        sweep.Reset();

        Assert.Equal(expected, sweep.Tick(0));
    }
}

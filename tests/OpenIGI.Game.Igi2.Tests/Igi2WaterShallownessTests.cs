using OpenIGI.Game.Igi2.World;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>How shallow water is, the delta <c>Water_VertexFunction</c> hands the water shader.</summary>
public sealed class Igi2WaterShallownessTests
{
    private const double Metre = 4096.0;

    [Fact]
    public void At_the_ground_the_water_is_wholly_shallow() =>
        Assert.Equal(1f, Igi2WaterPresenter.ShallownessAt(10.0 * Metre, 10.0 * Metre, 3.5f));

    [Fact]
    public void It_thins_in_proportion_over_one_absorption_length() =>
        Assert.Equal(0.5f, Igi2WaterPresenter.ShallownessAt(2.0 * Metre, 0.0, 4f), 5);

    [Fact]
    public void Beyond_an_absorption_length_the_water_is_deep() =>
        Assert.Equal(0f, Igi2WaterPresenter.ShallownessAt(5.0 * Metre, 0.0, 4f));

    [Fact]
    public void Ground_above_the_surface_counts_by_its_distance() =>
        Assert.Equal(0.75f, Igi2WaterPresenter.ShallownessAt(0.0, 1.0 * Metre, 4f), 5);

    [Fact]
    public void No_ground_and_no_absorption_length_are_deep()
    {
        Assert.Equal(0f, Igi2WaterPresenter.ShallownessAt(0.0, null, 4f));
        Assert.Equal(0f, Igi2WaterPresenter.ShallownessAt(0.0, 0.0, 0f));
    }
}

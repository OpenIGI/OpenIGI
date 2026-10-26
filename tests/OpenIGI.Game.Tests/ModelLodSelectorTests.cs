using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The two games' level-of-detail picks that are not shared: I.G.I. 2's rigid one by distance, and its model-detail bias.</summary>
public sealed class ModelLodSelectorTests
{
    private const float Unit = 4096f;

    // A model's own distances, world units: to its second to fifth levels and its cutoff.
    private static readonly float[] Distances = [10f * Unit, 20f * Unit, 30f * Unit, 40f * Unit, 50f * Unit];

    [Theory]
    [InlineData(5f, 0)]
    [InlineData(15f, 1)]
    [InlineData(25f, 2)]
    [InlineData(35f, 3)]
    [InlineData(45f, 4)]
    [InlineData(55f, -1)]
    public void A_rigid_model_with_distances_steps_a_level_each_time_it_passes_one(float metres, int level)
    {
        double squared = (double)metres * Unit * metres * Unit;
        Assert.Equal(level, ModelLodSelector.SelectByDistance(squared, 1f, 1f, 1f, Unit, 10f, 5, Distances));
    }

    [Fact]
    public void The_field_of_view_counts_for_no_more_than_one_and_the_bias_stretches_the_switches_but_not_the_cutoff()
    {
        double squared = 15.0 * Unit * 15.0 * Unit;
        Assert.Equal(1, ModelLodSelector.SelectByDistance(squared, 1f, 2f, 1f, Unit, 10f, 5, Distances));

        // At a model detail of three quarters the squared distance is taken four times over: 15 m reads as 30 m.
        float biasSquared = ModelLodRules.Igi2(0.75f).BiasSquared;
        Assert.Equal(4f, biasSquared);
        Assert.Equal(3, ModelLodSelector.SelectByDistance(squared, 1f, 1f, biasSquared, Unit, 10f, 5, Distances));
        Assert.Equal(4, ModelLodSelector.SelectByDistance(45.0 * Unit * 45.0 * Unit, 1f, 1f, biasSquared, Unit, 10f, 5, Distances));
    }

    [Fact]
    public void A_rigid_model_without_distances_steps_down_in_equal_parts_of_its_reach_and_is_gone_past_it()
    {
        // A one-metre radius at a cutoff of ten: a reach of ten metres in five parts of two.
        Assert.Equal(-1, ModelLodSelector.SelectByDistance(11.0 * Unit * 11.0 * Unit, 1f, 1f, 1f, Unit, 10f, 5, []));
        Assert.Equal(4, ModelLodSelector.SelectByDistance(9.0 * Unit * 9.0 * Unit, 1f, 1f, 1f, Unit, 10f, 5, []));
        Assert.Equal(0, ModelLodSelector.SelectByDistance(1.0 * Unit * 1.0 * Unit, 1f, 1f, 1f, Unit, 10f, 5, []));
    }

    [Fact]
    public void The_model_detail_bias_holds_a_body_at_a_coarser_level_unless_a_cutscene_runs()
    {
        Assert.Equal(0, ModelLodRules.Igi2(1f).Biased(0, 5, held: false));
        Assert.Equal(1, ModelLodRules.Igi2(0.75f).Biased(0, 5, held: false));
        Assert.Equal(2, ModelLodRules.Igi2(0.25f).Biased(1, 5, held: false));
        Assert.Equal(3, ModelLodRules.Igi2(0.25f).Biased(3, 5, held: false));
        Assert.Equal(0, ModelLodRules.Igi2(0.25f).Biased(0, 5, held: true));

        // One beyond its cutoff is drawn at the coarse level too, as the original's draw does.
        Assert.Equal(1, ModelLodRules.Igi2(0.75f).Biased(-1, 5, held: false));
    }
}

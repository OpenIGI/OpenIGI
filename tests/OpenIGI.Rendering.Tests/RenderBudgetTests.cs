using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// The per-stage frame budget, and the property that makes it one.
/// </summary>
/// <remarks>
/// A list of per-stage allowances is only a budget if the allowances add up to something — otherwise
/// every stage can pass its own line while the frame misses its own. This is the arithmetic nobody did
/// for five stages, which is how <c>r_perflog</c> came to be written after the fact.
/// </remarks>
public sealed class RenderBudgetTests
{
    [Fact]
    public void TheBudgetedStagesLeaveRoomForTheRestOfTheFrame()
    {
        double allocated = 0.0;

        foreach (RenderStage stage in Enum.GetValues<RenderStage>())
        {
            allocated += RenderBudget.For(stage) ?? 0.0;
        }

        // The G-buffer fill, the forward pass and the temporal resolve are unbudgeted and still have to
        // happen. Measured on mission 1 at 1080p on ultra they come to about 4 ms together, so a budget
        // spending more than about two thirds of the frame on the effects alone could not be met even
        // if every stage hit its number exactly.
        Assert.True(
            allocated < RenderBudget.FrameMilliseconds * 0.67,
            $"the budgeted stages claim {allocated:0.0} ms of a {RenderBudget.FrameMilliseconds:0.0} ms frame, "
                + "leaving too little for the G-buffer, the forward pass and the present");
    }

    [Fact]
    public void EveryBudgetIsAPositiveNumberOfMilliseconds()
    {
        foreach (RenderStage stage in Enum.GetValues<RenderStage>())
        {
            if (RenderBudget.For(stage) is { } budget)
            {
                Assert.True(budget > 0.0, $"{stage} was given a budget of {budget} ms");
            }
        }
    }

    [Fact]
    public void TheStagesWithoutABudgetAreTheOnesThatCannotBeTurnedOff()
    {
        // Not a taste judgement: a budget is a decision about what an OPTIONAL stage may cost, and
        // these three are the frame itself. Giving them a line to beat would make the budget a
        // statement about the hardware rather than about the renderer.
        Assert.Null(RenderBudget.For(RenderStage.Geometry));
        Assert.Null(RenderBudget.For(RenderStage.Forward));
        Assert.Null(RenderBudget.For(RenderStage.TemporalResolve));

        Assert.NotNull(RenderBudget.For(RenderStage.Shadows));
        Assert.NotNull(RenderBudget.For(RenderStage.AmbientOcclusionSearch));
        Assert.NotNull(RenderBudget.For(RenderStage.Lighting));
        Assert.NotNull(RenderBudget.For(RenderStage.Reflections));
        Assert.NotNull(RenderBudget.For(RenderStage.FogIntegrate));
        Assert.NotNull(RenderBudget.For(RenderStage.ToneMapGrade));
    }
}

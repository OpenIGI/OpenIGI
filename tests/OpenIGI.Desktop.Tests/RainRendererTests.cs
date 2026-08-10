using OpenIGI.Engine.Tasks;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class RainRendererTests
{
    [Fact]
    public void FindRainEffectLocatesTaskInTaskTree()
    {
        TaskTypeRegistry registry = new();
        TaskType rainType = registry.Declare("RainEffect", [
            new TaskParameter("Traceline start", ScriptParamType.Real32),
            new TaskParameter("Traceline end", ScriptParamType.Real32),
            new TaskParameter("Rain Alpha", ScriptParamType.Real32)
        ]);

        TaskTree tree = new(registry);
        GameTask rainTask = tree.New(tree.Root, rainType);

        DeclaredTask? found = RainRenderer.FindRainEffect(tree.Root);
        Assert.NotNull(found);
        Assert.Same(rainTask, found);
    }

    [Fact]
    public void IndoorsFlagSuppressesActiveRendering()
    {
        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);

        renderer.SetParams(active: true, startMeters: 10f, endMeters: -2f, alpha: 0.5f);
        Assert.True(renderer.IsActive);

        renderer.Indoors = true;
        Assert.False(renderer.IsActive);

        renderer.Indoors = false;
        Assert.True(renderer.IsActive);
    }

    [Fact]
    public void HeightBoundsCalculateCorrectlyAroundCameraZ()
    {
        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);

        renderer.SetParams(active: true, startMeters: 12f, endMeters: -4f, alpha: 0.5f);
        (float start, float end) = renderer.CalculateHeightBounds(100.0);

        Assert.Equal(112.0f, start);
        Assert.Equal(96.0f, end);
    }
}

using OpenIGI.Engine.Tasks;
using Xunit;
using OpenIGI.Game.Menus;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// The turning green overlay, from <c>BackgroundFX</c> (<c>0x419760</c>).
/// </summary>
public sealed class BackgroundFxTests
{
    [Fact]
    public void TheWholeEffectIsAFunctionOfOneCounter()
    {
        BackgroundFxTask fx = new();

        Assert.Equal(0f, fx.Time);
        fx.Receive(TaskMessage.Update);
        fx.Receive(TaskMessage.Update);

        // BackgroundFX_Update does exactly one thing: add one.
        Assert.Equal(2f, fx.Time);
    }

    [Fact]
    public void OneLayerTurnsTheOtherWay()
    {
        // Three layers all turning the same way would read as one rigid object.
        Assert.Equal(3, BackgroundFxTask.Layers.Count);
        Assert.True(BackgroundFxTask.Layers[1].RotationRate < 0f);
        Assert.True(BackgroundFxTask.Layers[0].RotationRate > 0f);
        Assert.True(BackgroundFxTask.Layers[2].RotationRate > 0f);
    }

    [Fact]
    public void EveryLayerTurnsAndPulsesAsTimePasses()
    {
        foreach (BackgroundFxLayer layer in BackgroundFxTask.Layers)
        {
            BackgroundFxTransform a = BackgroundFxTask.GetTransform(layer, 0f);
            BackgroundFxTransform b = BackgroundFxTask.GetTransform(layer, 300f);

            Assert.NotEqual(a.M0, b.M0);
            Assert.NotEqual(a.Scale, b.Scale);
        }
    }

    [Fact]
    public void ALayersTranslationIsItsOwnCentre()
    {
        // The transform's translation slots are the layer's width and height fields, which is how the
        // centre reaches the maths at all.
        BackgroundFxTransform m = BackgroundFxTask.GetTransform(BackgroundFxTask.Layers[1], 0f);

        Assert.Equal(400f, m.TranslateX);
        Assert.Equal(256f, m.TranslateY);
        Assert.Equal(m.TranslateX, m.Apply(0f, 0f).X);
    }

    [Fact]
    public void TheTransformIsARotationNotAShear()
    {
        BackgroundFxTransform m = BackgroundFxTask.GetTransform(BackgroundFxTask.Layers[0], 137f);

        // [cos*s, sin*s; -sin*s, cos*s]: the rows stay perpendicular and equal in length.
        (float x, float y) = m.Apply(1f, 0f);
        (float px, float py) = m.Apply(0f, 1f);

        float dot = ((x - m.TranslateX) * (px - m.TranslateX)) + ((y - m.TranslateY) * (py - m.TranslateY));
        Assert.Equal(0f, dot, 3);
    }

    [Fact]
    public void TheGeometryIsTheOnesTheDataTablesHold()
    {
        Assert.NotNull(BackgroundFxTask.Layers[0].Grid);
        Assert.Equal(20, BackgroundFxTask.Layers[0].Grid!.Value.Columns);
        Assert.Empty(BackgroundFxTask.Layers[0].Circles);

        Assert.Equal(2, BackgroundFxTask.Layers[1].Circles.Count);
        Assert.Equal(new BackgroundFxCircle(0f, 0f, 100f), BackgroundFxTask.Layers[1].Circles[0]);
        Assert.Equal(5, BackgroundFxTask.Layers[1].Lines.Count);

        Assert.Single(BackgroundFxTask.Layers[2].Circles);
        Assert.Equal(new BackgroundFxCircle(0f, -100f, 200f), BackgroundFxTask.Layers[2].Circles[0]);
        Assert.Equal(5, BackgroundFxTask.Layers[2].Lines.Count);
    }
}

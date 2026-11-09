using Xunit;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Presentation.Tests;

/// <summary>The bomb blast's camera shake: it arrives late with the sound, and dies away.</summary>
public sealed class TraumaShakeTests
{
    /// <summary>Trauma sent with a delay is not felt until the delay is up.</summary>
    [Fact]
    public void TraumaArrivesAfterItsDelay()
    {
        var shake = new TraumaShake();
        shake.Add(0.8f, delaySeconds: 0.5f);
        for (int i = 0; i < 14; i++)
        {
            shake.Tick();
            Assert.Equal(0f, shake.Trauma);
        }

        shake.Tick();
        shake.Tick();
        Assert.True(shake.Trauma > 0.7f);
    }

    /// <summary>Trauma decays to nothing, and then nothing shakes.</summary>
    [Fact]
    public void TraumaDecaysToStillness()
    {
        var shake = new TraumaShake();
        shake.Add(1f, 0f, kickPitch: 0.07f);
        for (int i = 0; i < 30 * 6; i++)
        {
            shake.Tick();
        }

        Assert.Equal(0f, shake.Trauma);
        Assert.True(shake.Sample(0.5f, 1f).IsNone);
    }

    /// <summary>A shake scaled to zero, the player's setting off, does not move the view.</summary>
    [Fact]
    public void ZeroScaleDoesNotShake()
    {
        var shake = new TraumaShake();
        shake.Add(1f, 0f);
        shake.Tick();
        Assert.True(shake.Sample(0.5f, 0f).IsNone);
    }

    /// <summary>Clearing, as a new round does, stops it at once.</summary>
    [Fact]
    public void ClearStopsEverything()
    {
        var shake = new TraumaShake();
        shake.Add(1f, 0.2f);
        shake.Tick();
        shake.Clear();
        for (int i = 0; i < 20; i++)
        {
            shake.Tick();
        }

        Assert.False(shake.IsActive);
    }
}

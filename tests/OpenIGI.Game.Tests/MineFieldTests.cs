using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Minefields: the wait between blasts both games share, and Project I.G.I.'s as its levels set them.</summary>
[Collection(RetailMissionWorld.Name)]
public sealed class MineFieldTests
{
    [Fact]
    public void AMineFieldGoesOffOnlyOnceItHasWaited()
    {
        MineFieldPause pause = new();
        Assert.True(pause.Due(static () => true));

        pause.Ticks = 2;
        int asked = 0;
        Assert.False(pause.Due(() => ++asked > 0));
        Assert.False(pause.Due(() => ++asked > 0));
        Assert.Equal(0, asked);
        Assert.True(pause.Due(() => ++asked > 0));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void ExplodingOnceWaitsForEver()
    {
        // "-1: explode once" is a wait of -30 ticks, which counts down past nought and never reaches it.
        MineFieldPause pause = new() { Ticks = (int)(-1f * 30f) };
        for (int tick = 0; tick < 1000; tick++)
        {
            Assert.False(pause.Due(static () => true));
        }

        Assert.False(new MineFieldPause { Ticks = -1 }.Due(static () => true));
    }

    [RequiresGameDataFact]
    public void MissionSevensMineFieldBlastsAtThePlayerEveryFiveSeconds()
    {
        OpenIGI.Engine.Tasks.GameTask field = Assert.Single(RetailMissionData.Get(7).Root.Descendants(), static t => t.Type.Name == MineFieldTask.TypeName);

        Assert.True(field.TryGetScriptValue("Explode close to task ID", out int target));
        Assert.Equal(0, target);
        Assert.True(field.TryGetScriptValue("Explosion radius (meter)", out float radius));
        Assert.Equal(3f, radius);
        Assert.True(field.TryGetScriptValue("Explosion falloff radius (meter)", out float falloff));
        Assert.Equal(1.5f, falloff);
        Assert.True(field.TryGetScriptValue("Explosion damage scale", out float damage));
        Assert.Equal(4.5f, damage);
        Assert.True(field.TryGetScriptValue("Explosion delay between explosions (seconds) -1: explode once", out float wait));
        Assert.Equal(5f, wait);
        Assert.True(field.TryGetScriptValue("Explosion fragments", out int fragments));
        Assert.Equal(3, fragments);
        Assert.True(field.TryGetScriptValue("Explosion fireballs", out int fireballs));
        Assert.Equal(3, fireballs);
        Assert.True(field.TryGetScriptValue("Snap explosion to ground", out bool snaps));
        Assert.True(snaps);
        Assert.True(field.TryGetScriptValue("Explode", out string? condition));
        Assert.Contains("AreaActivate_151.nActive", condition, StringComparison.Ordinal);
    }
}

using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Rendering.Blast;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The bomb's after-blast: on the front's own clock, placed in the eye's view, fading out over its last seconds.</summary>
public sealed class BombAfterBlastTests
{
    private static readonly Vector3d BombMetres = new(500.0, 700.0, 20.0);

    /// <summary>Nothing while the front is held; then the front where the server has it; then nothing once it has run out.</summary>
    [Fact]
    public void ItRunsOnTheFrontsClock()
    {
        BombBlastEffects effects = Started();
        Camera3D eye = LookingAtTheBombFrom(100.0);

        Run(effects, 15);
        Assert.False(effects.AfterBlast(eye, 0f).IsActive);

        Run(effects, 30);
        BombAfterBlastSettings early = effects.AfterBlast(eye, 0f);
        Assert.True(early.IsActive);
        Assert.Equal((float)BombWave.FrontMetres(1.5), early.FrontMetres, 3);
        Assert.Equal(1f, early.Strength, 3);

        Run(effects, 30 * 8);
        BombAfterBlastSettings late = effects.AfterBlast(eye, 0f);
        Assert.InRange(late.Strength, 0.001f, 0.5f);

        Run(effects, 30 * 2);
        Assert.False(effects.AfterBlast(eye, 0f).IsActive);
    }

    /// <summary>The bomb is placed in the view the depth is read in: ahead of an eye looking at it, as far as it stands.</summary>
    [Fact]
    public void TheBombIsPlacedAheadOfAnEyeLookingAtIt()
    {
        BombBlastEffects effects = Started();
        Run(effects, 45);
        BombAfterBlastSettings seen = effects.AfterBlast(LookingAtTheBombFrom(100.0), 0f);
        Assert.InRange(seen.BombView.Z, 99f, 101f);
        Assert.InRange(MathF.Abs(seen.BombView.X), 0f, 0.5f);
    }

    private static BombBlastEffects Started()
    {
        var effects = new BombBlastEffects();
        Assert.NotNull(effects.Start(BombMetres * WorldUnits.PerMetre, 40f * (float)WorldUnits.PerMetre, listenerMetres: null));
        return effects;
    }

    // An eye on the level of the air above the bomb, a distance south of it, looking north at it.
    private static Camera3D LookingAtTheBombFrom(double metres) =>
        new(BombMetres + new Vector3d(0.0, -metres, 0.4), Camera3D.LookRotation(0f, 0f));

    private static void Run(BombBlastEffects effects, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            effects.Tick();
        }
    }
}

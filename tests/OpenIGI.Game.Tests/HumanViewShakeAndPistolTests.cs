using OpenIGI.Game.Player;
using Xunit;
using Signal = OpenIGI.Game.Player.HumanSoldierBodyMachine.Signal;

namespace OpenIGI.Game.Tests;

/// <summary>The player's hit shake (<c>0x465970</c>, <c>0x464BD0</c>) and the pistol soldier (<c>0x459880</c>).</summary>
public sealed class HumanViewShakeAndPistolTests
{
    [Fact]
    public void AHitShakeFadesToNothingOverItsTicks()
    {
        HumanViewShake shake = new();
        shake.Install(HumanViewShake.HitPositionUnits, HumanViewShake.HitAngleRadians, HumanViewShake.HitTicks);

        int draws = 0;
        double Draw()
        {
            draws++;
            return 0.999;
        }

        for (int tick = 0; tick < HumanViewShake.HitTicks; tick++)
        {
            Assert.True(shake.IsActive, $"the shake stopped after {tick} ticks");
            float intensity = 1f - (tick / (float)HumanViewShake.HitTicks);
            shake.Advance(Draw);

            // Five draws a tick, each bounded by the amplitude at this tick's intensity.
            Assert.Equal((tick + 1) * 5, draws);
            Assert.InRange(Math.Abs(shake.Pitch), 0f, (HumanViewShake.HitAngleRadians * intensity) + 1e-6f);
            Assert.InRange(Math.Abs(shake.RightUnits), 0f, (HumanViewShake.HitPositionUnits * intensity) + 1e-3f);
        }

        // Fifteen float subtractions of 1/15 can leave a residue of a few ulps, which plays one more,
        // invisible, tick -- as the original's float arithmetic does -- and then it is over.
        if (shake.IsActive)
        {
            shake.Advance(Draw);
            Assert.InRange(Math.Abs(shake.Pitch), 0f, 1e-7f);
        }

        Assert.False(shake.IsActive);
        int drawn = draws;
        shake.Advance(Draw);
        Assert.Equal(drawn, draws);
        Assert.Equal(0f, shake.Pitch);
    }

    [Fact]
    public void ThePistolSoldierFlinchesWhereTheSoldierDoesNot()
    {
        // 0x458DD0 tests the flinch while kneeling down; the soldier's 0x45B900 does not.
        HumanSoldierBodyMachine soldier = new();
        HumanSoldierBodyMachine pistol = HumanSoldierBodyMachine.ForPistol();
        foreach (HumanSoldierBodyMachine body in new[] { soldier, pistol })
        {
            body.Tick(Signal.OnGround | Signal.Kneel);
            Assert.Equal(HumanSoldierStates.CrouchDownState, body.State);
        }

        Assert.Equal(HumanSoldierStates.CrouchDownState, soldier.Tick(Signal.OnGround | Signal.Kneel | Signal.Flinch));
        Assert.Equal(HumanSoldierStates.FlinchState, pistol.Tick(Signal.OnGround | Signal.Kneel | Signal.Flinch));
    }

    [Fact]
    public void ThePistolSoldierFlinchesProneWhileLyingDown()
    {
        HumanSoldierBodyMachine pistol = HumanSoldierBodyMachine.ForPistol();
        pistol.Tick(Signal.OnGround | Signal.LieDown);

        Assert.Equal(HumanSoldierStates.ProneFlinchState, pistol.Tick(Signal.OnGround | Signal.LieDown | Signal.Flinch));
    }

    [Fact]
    public void ThePistolSoldierDiesInItsOwnClips()
    {
        HumanSoldierClass pistol = HumanSoldierClass.PistolSoldier;

        Assert.Equal(27, pistol.ChooseDeathAnimation(SoldierDeathMode.Thrown, zone: 2, shotFromTheFront: true, coinFlip: 0));
        Assert.Equal(28, pistol.ChooseDeathAnimation(SoldierDeathMode.Thrown, zone: 2, shotFromTheFront: false, coinFlip: 0));
        Assert.Equal(26, pistol.ChooseDeathAnimation(SoldierDeathMode.Crumple, zone: 2, shotFromTheFront: false, coinFlip: 0));
        Assert.Equal(44, pistol.ChooseDeathAnimation(SoldierDeathMode.Crumple, zone: 15, shotFromTheFront: false, coinFlip: 1));

        HumanSoldierBodyMachine kneeling = HumanSoldierBodyMachine.ForPistol();
        kneeling.Tick(Signal.OnGround | Signal.Kneel);
        Assert.Equal(29, kneeling.PostureDeathAnimation);
    }

    [Fact]
    public void ThePistolSoldierKeepsItsOwnTableAndLandsNoBlow()
    {
        HumanSoldierClass pistol = HumanSoldierClass.PistolSoldier;

        Assert.Equal(PlayerTaskTypes.HumanSoldierPistol, pistol.TypeName);
        Assert.Equal(3, pistol.DefaultBoneHierarchy);
        Assert.Equal(39, pistol.States.AnimationOf(HumanSoldierStates.UseObjectState));
        Assert.False(pistol.SwapsClipsByAiType);
        Assert.False(pistol.LandsCloseCombatBlow);
        Assert.False(pistol.SoundsKnifeStab);
        Assert.False(pistol.TakesConfiguredRecoilThreshold);
        Assert.True(HumanSoldierClass.Soldier.SwapsClipsByAiType);
    }
}

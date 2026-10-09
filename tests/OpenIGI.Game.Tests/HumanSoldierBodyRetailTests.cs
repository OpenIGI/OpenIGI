using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using Xunit;
using Signal = OpenIGI.Game.Player.HumanSoldierBodyMachine.Signal;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The soldier body's hit reactions and deaths, as <c>off_53EC10</c>'s per-ticks and the hit handlers
/// define them.
/// </summary>
public sealed class HumanSoldierBodyRetailTests
{
    [Fact]
    public void ASoldierFlinchesOnlyOnceItsRecoilPassesTheThreshold()
    {
        // 0x45D3C0: accumulate, flinch strictly over the threshold, and start again from zero.
        HumanRecoil recoil = HumanSoldierClass.Soldier.CreateRecoil(configuredThreshold: 0.5f);

        Assert.Equal(default, recoil.Absorb(0.3f));
        Assert.Equal(new HumanRecoilOutcome(Reacted: true, Flinches: true), recoil.Absorb(0.3f));
        Assert.Equal(0f, recoil.Accumulated);
        Assert.Equal(default, recoil.Absorb(0.5f));
    }

    [Fact]
    public void OnlyTheOrdinarySoldierTakesTheConfiguredThreshold()
    {
        // 0x406F70 writes Human+3332 for a HumanSoldier only; the others keep the constructor's 0.33.
        Assert.Equal(0.5f, HumanSoldierClass.Soldier.CreateRecoil(0.5f).Threshold);
        Assert.Equal(HumanRecoil.DefaultThreshold, HumanSoldierClass.FemaleSoldier.CreateRecoil(0.5f).Threshold);
    }

    [Fact]
    public void AFemaleSoldierReactsButNeverFlinchesAndAnRpgSoldierAlwaysDoes()
    {
        HumanRecoil female = HumanSoldierClass.FemaleSoldier.CreateRecoil(null);
        Assert.Equal(new HumanRecoilOutcome(Reacted: true, Flinches: false), female.Absorb(0.4f));

        HumanRecoil rpg = HumanSoldierClass.RpgSoldier.CreateRecoil(null);
        Assert.Equal(new HumanRecoilOutcome(Reacted: true, Flinches: true), rpg.Absorb(0.01f));
    }

    [Theory]
    [InlineData(1, 44)]
    [InlineData(4, 47)]
    [InlineData(7, 45)]
    [InlineData(10, 46)]
    [InlineData(15, 27)]
    public void AFlinchPlaysTheStruckZonesLimbClipAndStands(int zone, int clip)
    {
        HumanSoldierBodyMachine body = new();

        Assert.Equal(HumanSoldierStates.FlinchState, body.Tick(Input(flinch: true, zone: zone)));
        Assert.True(body.IsFlinching);
        Assert.Equal(clip, body.CurrentAnimation);

        // 0x4592F0 plays the flinch out, and a kneeling or standing soldier returns to standing.
        Assert.Equal(HumanSoldierStates.FlinchState, body.Tick(Input()));
        Assert.Equal(HumanSoldierStates.StandState, body.Tick(Input(animationDone: true)));
    }

    [Fact]
    public void AProneSoldierFlinchesProneAndOnlyAFreshFlinchAfterTwentyTicksRestartsIt()
    {
        HumanSoldierBodyMachine body = new();
        body.Tick(Grounded(Signal.LieDown));
        body.Tick(Grounded(Signal.LieDown | Signal.AnimationDone));
        Assert.Equal(HumanSoldierStates.ProneState, body.State);

        Assert.Equal(HumanSoldierStates.ProneFlinchState, body.Tick(Grounded(Signal.LieDown | Signal.Flinch)));
        Assert.Equal(26, body.CurrentAnimation);

        for (int tick = 0; tick < 20; tick++)
        {
            body.Tick(Grounded(Signal.LieDown | Signal.Flinch));
            Assert.False(body.ChangedThisTick, $"the prone flinch restarted on its tick {tick + 1}");
        }

        body.Tick(Grounded(Signal.LieDown | Signal.Flinch));
        Assert.True(body.ChangedThisTick);
        Assert.Equal(HumanSoldierStates.ProneFlinchState, body.State);
    }

    [Fact]
    public void ADeadBodyStaysDeadWhateverElseItReads()
    {
        HumanSoldierBodyMachine body = new();
        Assert.Equal(HumanSoldierStates.DeathState, body.Tick(Grounded(Signal.Dead)));

        // 0x45C700 has no exits: a corpse in the air, or flinched, does not get up.
        Assert.Equal(HumanSoldierStates.DeathState, body.Tick(Signal.Flinch | Signal.Dead));
        Assert.Equal(HumanSoldierStates.DeathState, body.Tick(Grounded(Signal.Flinch | Signal.AnimationDone)));
    }

    [Fact]
    public void AKneelingSoldierDiesInTheKneelingDeathAndAProneOneInTheProne()
    {
        HumanSoldierBodyMachine kneeling = new();
        kneeling.Tick(Grounded(Signal.Kneel));
        kneeling.Tick(Grounded(Signal.Kneel | Signal.AnimationDone));
        Assert.Equal(HumanSoldierStates.KneelingState, kneeling.State);
        Assert.Equal(24, kneeling.PostureDeathAnimation);
        Assert.Equal(HumanSoldierStates.KneelDeathState, kneeling.Tick(Grounded(Signal.Kneel | Signal.Dead)));

        HumanSoldierBodyMachine prone = new();
        prone.Tick(Grounded(Signal.LieDown));
        prone.Tick(Grounded(Signal.LieDown | Signal.AnimationDone));
        Assert.Equal(25, prone.PostureDeathAnimation);
        Assert.Equal(HumanSoldierStates.ProneDeathState, prone.Tick(Grounded(Signal.LieDown | Signal.Dead)));

        Assert.Null(new HumanSoldierBodyMachine().PostureDeathAnimation);
    }

    [Fact]
    public void TheTypedClipTablesAreTheSoldiersWithTheirOwnCombatClips()
    {
        // 0x53EE40, 0x53EF70 and 0x53F0A0 against 0x53ED10, read byte for byte.
        Assert.Equal(63, HumanSoldierStates.SpetsnazTable.AnimationOf(HumanSoldierStates.FiringState));
        Assert.Equal(78, HumanSoldierStates.SpetsnazTable.AnimationOf(HumanSoldierStates.RackState));
        Assert.Equal(64, HumanSoldierStates.PriboiTable.AnimationOf(HumanSoldierStates.StandState));
        Assert.Equal(5, HumanSoldierStates.PriboiTable.AnimationOf(HumanSoldierStates.ReadyState));
        Assert.Equal(76, HumanSoldierStates.CivilianTable.AnimationOf(HumanSoldierStates.RunState));
        Assert.Equal(94, HumanSoldierStates.CivilianTable.AnimationOf(HumanSoldierStates.KneelingState));

        // Every other state, and every mode, is the base table's.
        Assert.Equal(
            HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.RunState),
            HumanSoldierStates.SpetsnazTable.AnimationOf(HumanSoldierStates.RunState));
        for (int state = 0; state < HumanSoldierStates.Table.StateCount; state++)
        {
            Assert.Equal(HumanSoldierStates.Table.ModeOf(state), HumanSoldierStates.CivilianTable.ModeOf(state));
        }
    }

    [Fact]
    public void AnExplosionKillIsThrownAndTurnedToTheBlast()
    {
        // 0x45A3D0 records death mode 0, which 0x45C440 handles as a throw.
        Assert.True(HumanSoldierClass.Soldier.TurnsToTheShot(SoldierDeathMode.Untouched));
        Assert.Equal(
            SoldierDeath.ThrownBackwardsAnimation,
            HumanSoldierClass.Soldier.ChooseDeathAnimation(SoldierDeathMode.Untouched, zone: 13, shotFromTheFront: true, coinFlip: 0));
    }

    [Fact]
    public void ACorpseStopsSlidingWhenItsDeathClipEnds()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Kill(new Vector3d(10, 0, 0));

        soldier.TickDeadBody(default, deathClipEnded: false);
        Assert.Equal(10, soldier.Position.X);

        // 0x45C700 still slides the tick the clip ends; 0x45C740, from the tick after, adds no more velocity.
        // Probed on mission 4: guard 410's corpse moved on the tick its clip reached its end, and then stopped.
        soldier.TickDeadBody(default, deathClipEnded: true);
        Assert.Equal(20, soldier.Position.X);
        soldier.TickDeadBody(default, deathClipEnded: false);
        Assert.Equal(20, soldier.Position.X);
    }

    private static Signal Grounded(Signal bits) => Signal.OnGround | bits;

    private static HumanBodyInput Input(bool flinch = false, int zone = -1, bool animationDone = false) =>
        new(default, animationDone, Flinch: flinch, HitZone: zone);
}

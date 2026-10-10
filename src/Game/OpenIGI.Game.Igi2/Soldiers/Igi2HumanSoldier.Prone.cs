using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>Lying down, firing and reloading prone, a prone flinch and death, and lying dead.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>The ticks a prone flinch must last before another hit flinches it again, more than <c>19</c>.</summary>
    private const int ProneHitDelay = 19;

    /// <summary>Gets or sets the ticks the prone flinch has run, <c>nHitDelay</c>.</summary>
    public int HitDelay { get; set; }

    /// <summary>
    /// A change of posture to or from lying: <c>HumanSoldier_InitStateStandToLieDown</c> (<c>igi2 0x4E6B00</c>),
    /// <c>LieDownToStand</c> (<c>0x4E6C30</c>) and <c>KneelToLieDown</c> (<c>0x4E74E0</c>): the posture it starts from, its row at
    /// once, still.
    /// </summary>
    /// <param name="pose">The posture it starts in.</param>
    /// <param name="run">Its tick.</param>
    private void EnterPostureChange(Igi2HumanPose pose, Action run)
    {
        Pose = pose;
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => run();
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>
    /// Lying down from standing's tick, <c>HumanSoldier_RunStateStandToLieDown</c> (<c>igi2 0x4E6B60</c>): it lies once the clip ends,
    /// dies lying if killed.
    /// </summary>
    private void RunStandToLieDown() => RunPostureChange(Igi2SoldierState.LieDownDie, Igi2SoldierState.LieDown);

    /// <summary>
    /// Getting up from lying's tick, <c>HumanSoldier_RunStateLieDownToStand</c> (<c>igi2 0x4E6C90</c>): the clip at twice the speed,
    /// standing once it ends.
    /// </summary>
    private void RunLieDownToStand()
    {
        Animation.MainTimeStep = Igi2AnimController.TicksPerFrame + Igi2AnimController.TicksPerFrame;
        RunPostureChange(Igi2SoldierState.LieDownDie, Igi2SoldierState.Stand);
    }

    /// <summary>
    /// Lying down from a kneel's tick, <c>HumanSoldier_RunStateKneelToLieDown</c> (<c>igi2 0x4E7540</c>): it lies once the clip ends,
    /// dies kneeling if killed.
    /// </summary>
    private void RunKneelToLieDown() => RunPostureChange(Igi2SoldierState.KneelDie, Igi2SoldierState.LieDown);

    /// <summary>A posture change's tests: off the ground it falls, killed it dies, its clip ended it settles.</summary>
    private void RunPostureChange(int death, int done)
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(death);
        }
        else if ((s & Igi2SoldierSignal.MainEnded) != 0)
        {
            Go(done);
        }
    }

    /// <summary>
    /// Lying, <c>HumanSoldier_InitStateLieDown</c> (<c>igi2 0x4E7610</c>): flat, the upper body cleared, its row at once.
    /// </summary>
    private void EnterLieDown()
    {
        Pose = Igi2HumanPose.LyingFlat;
        Animation.ClearUpperBody(0f);
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => RunLieDown();
    }

    /// <summary>
    /// Lying's tick, <c>HumanSoldier_RunStateLieDown</c> (<c>igi2 0x4E7650</c>): it fires or reloads prone while the lie channel holds,
    /// and gets up when it drops.
    /// </summary>
    private void RunLieDown()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Go(Igi2SoldierState.LieDownHit);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.LieDownDie);
        }
        else if ((s & Igi2SoldierSignal.LieDown) == 0)
        {
            Go(Igi2SoldierState.LieDownToStand);
        }
        else if ((s & Igi2SoldierSignal.Fire) != 0)
        {
            Go(Igi2SoldierState.LieDownFire);
        }
        else if ((s & Igi2SoldierSignal.Reload) != 0)
        {
            Go(Igi2SoldierState.LieDownReload);
        }
    }

    /// <summary>
    /// Firing or reloading prone, <c>HumanSoldier_InitStateLieDownFire</c> (<c>igi2 0x4E77E0</c>) and <c>LieDownReload</c>
    /// (<c>0x4E79C0</c>): its row at once, still; the posture is left as it was.
    /// </summary>
    /// <param name="run">Its tick.</param>
    private void EnterProneAction(Action run)
    {
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => run();
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>
    /// Firing prone's tick, <c>HumanSoldier_RunStateLieDownFire</c> (<c>igi2 0x4E7830</c>): the trigger held while the fire channel
    /// is, and lying again once the clip ends with the fire bit down. Leaving it any way lets the trigger go.
    /// </summary>
    /// <remarks>The death bit is tested before the ground, unlike every other state.</remarks>
    private void RunLieDownFire()
    {
        Igi2SoldierSignal s = Signal;
        int next;
        if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            next = Igi2SoldierState.LieDownDie;
        }
        else if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            next = Igi2SoldierState.Fall;
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            next = Igi2SoldierState.LieDownHit;
        }
        else
        {
            if (Channels[Igi2SoldierChannel.Fire] != 0f)
            {
                Weapon.Fire(0);
            }

            if ((s & Igi2SoldierSignal.MainEnded) == 0 || (s & Igi2SoldierSignal.Fire) != 0)
            {
                return;
            }

            next = Igi2SoldierState.LieDown;
        }

        StopWeapon(onlyWhenBusy: false);
        Go(next);
    }

    /// <summary>
    /// Reloading prone's tick, <c>HumanSoldier_RunStateLieDownReload</c> (<c>igi2 0x4E7A10</c>): once the clip ends the weapon reloads
    /// and the soldier lies again.
    /// </summary>
    private void RunLieDownReload()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Go(Igi2SoldierState.LieDownHit);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.LieDownDie);
        }
        else if ((s & Igi2SoldierSignal.MainEnded) != 0)
        {
            Weapon.Firing?.Reload();
            Go(Igi2SoldierState.LieDown);
        }
    }

    /// <summary>
    /// A prone flinch, <c>HumanSoldier_InitStateLieDownHit</c> (<c>igi2 0x4E81E0</c>): its row at once, the upper body not turned,
    /// still. Retail sets the posture to standing here, and so does the port.
    /// </summary>
    private void EnterLieDownHit()
    {
        Pose = Igi2HumanPose.Standing;
        HitDelay = 0;
        PlayCurrentState(0f);
        IsRotateUpperBody = false;
        Locomotion.Current = (_, _) => RunLieDownHit();
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>
    /// A prone flinch's tick, <c>HumanSoldier_RunStateLieDownHit</c> (<c>igi2 0x4E8240</c>): for twenty ticks it only counts; after,
    /// another hit flinches it again. Once the clip ends it lies again, the upper body turning.
    /// </summary>
    private void RunLieDownHit()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
            return;
        }

        if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.LieDownDie);
            return;
        }

        if (HitDelay <= ProneHitDelay)
        {
            HitDelay++;
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Go(Igi2SoldierState.LieDownHit);
            return;
        }

        if ((s & Igi2SoldierSignal.MainEnded) != 0)
        {
            IsRotateUpperBody = true;
            Go(Igi2SoldierState.LieDown);
        }
    }

    /// <summary>
    /// A prone death, <c>HumanSoldier_InitStateLieDownDie</c> (<c>igi2 0x4E8AB0</c>): the body becomes something detection can see, the
    /// trigger is let go and the weapons drop, and its row plays at once flat; the upper body is left as it was.
    /// </summary>
    private void EnterLieDownDie()
    {
        Surroundings.RegisterDetectable(this);
        IsDeathSoundPlayed = false;
        if (Weapon.Inventory is not null)
        {
            StopWeapon(onlyWhenBusy: false);
            CreateGunPickups();
        }

        Pose = Igi2HumanPose.LyingFlat;
        PlayCurrentState(0f);
        CameraMode = 3;
        View.IsLockOrientation = true;
        IsLockOrientation = true;
        IsRotateUpperBody = false;
        Locomotion.Current = (_, _) => RunDie();
        GroundTolerance = 0f;
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>
    /// Lying dead, <c>HumanSoldier_InitStateDead</c> (<c>igi2 0x4E8B80</c>): seen by detection, its row set at the clip's end, the camera
    /// and heading fixed; dying's tick runs on.
    /// </summary>
    private void EnterDead()
    {
        Surroundings.RegisterDetectable(this);
        Pose = Igi2HumanPose.Standing;
        Animation.PlayStateAtEnd(Locomotion.CurrentState, 0f);
        CameraMode = 3;
        View.IsLockOrientation = true;
        IsLockOrientation = true;
        IsRotateUpperBody = false;
        IsDisableDeltaTranslation = false;
        Locomotion.Current = (_, _) => RunDie();
    }
}

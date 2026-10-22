using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using Xunit;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The view sway a weapon swap runs: both angles together, the snap to target, and the lowered rig
/// that hides the exchange.
/// </summary>
public sealed class PlayerWeaponSwayTests
{
    [Fact]
    public void BothSwayAnglesArriveOnTheSameTick()
    {
        // 35 degrees at 0.0581776 and 20 degrees at 0.0332444 are both 10.5 ticks. The rates were
        // chosen to make the pair one motion; a port that picked its own would tip the rig crooked on
        // the way down.
        HumanViewSway sway = new();
        sway.Lower();

        int pitchArrived = -1;
        int yawArrived = -1;
        for (int tick = 1; tick <= 40; tick++)
        {
            sway.Advance();

            if (pitchArrived < 0 && sway.Pitch == HumanViewSway.LoweredPitch)
            {
                pitchArrived = tick;
            }

            if (yawArrived < 0 && sway.Yaw == HumanViewSway.LoweredYaw)
            {
                yawArrived = tick;
            }
        }

        Assert.Equal(HumanViewSway.TicksToTravel, pitchArrived);
        Assert.Equal(HumanViewSway.TicksToTravel, yawArrived);
        Assert.True(sway.Settled);
    }

    [Fact]
    public void TheSwaySnapsToItsTargetRatherThanApproachingIt()
    {
        // The exact equality in Settled is only safe because the step snaps on overshoot. A step that
        // merely got close would leave every weapon state waiting for ever.
        HumanViewSway sway = new();
        sway.MoveTo(-1f, 0f, 0.3f, 0f);

        for (int i = 0; i < 10; i++)
        {
            sway.Advance();
        }

        Assert.Equal(-1f, sway.Pitch);
        Assert.True(sway.Settled);
    }

    [Fact]
    public void AWeaponSwapHidesTheExchangeBehindTheLoweredRig()
    {
        // The whole point of the animation: the model is not exchanged until the rig is out of the
        // frame, and control does not come back until it has returned.
        PlayerWeapons weapons = new(Cycle);
        weapons.Carry(Knife);
        weapons.Carry(Glock, NineMillimetre);
        Equip(weapons);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        PlayerInputState input = new();
        Tick(weapons, input, Frame(PlayerInputSlot.NextWeapon));
        Assert.Equal(HumanWeaponState.ChangingWeapon, weapons.State);

        // The rig tips out of the frame, and all the way down the knife is still what you are holding.
        for (int i = 0; i < HumanViewSway.TicksToTravel - 1; i++)
        {
            Tick(weapons, input, Frame());
            Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);
        }

        Assert.True(weapons.Sway.Settled);
        Assert.Equal(HumanViewSway.LoweredPitch, weapons.Sway.Pitch);
        Assert.Equal(HumanViewSway.LoweredYaw, weapons.Sway.Yaw);

        // The tick it has arrived is the tick the model changes, and the rig starts back up.
        Tick(weapons, input, Frame());
        Assert.Equal("WEAPON_ID_GLOCK", weapons.InHand?.Weapon.ScriptId);
        Assert.Equal(0f, weapons.Sway.TargetPitch);
        Assert.Equal(HumanWeaponState.ChangingWeapon, weapons.State);

        // Control is not handed back until it is up again, and that takes the same eleven ticks.
        int raising = 0;
        while (weapons.State == HumanWeaponState.ChangingWeapon && raising < 4 * HumanViewSway.TicksToTravel)
        {
            Tick(weapons, input, Frame());
            raising++;
        }

        Assert.Equal(HumanWeaponState.Ready, weapons.State);
        Assert.Equal(0f, weapons.Sway.Pitch);
        Assert.Equal(HumanViewSway.TicksToTravel, raising);
    }

    [Fact]
    public void TheSwayIsTheIdentityWhenNothingIsBeingPutAway()
    {
        PlayerWeapons weapons = new([20]);
        weapons.Carry(Knife);
        Equip(weapons);

        Assert.True(weapons.Sway.Settled);
        Assert.Equal(Matrix3f.Identity, weapons.Sway.ToMatrix());
    }
}

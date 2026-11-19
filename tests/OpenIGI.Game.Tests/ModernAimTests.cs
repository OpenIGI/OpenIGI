using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Player.Aim;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Modern aim's shared core: the profile table, the aim's timing, the sights' alignment and the rig's moves.</summary>
public sealed class ModernAimTests
{
    private static readonly ModernAimOptions On = ModernAimOptions.Off with { Enabled = true };
    private static readonly float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;

    [Fact]
    public void ProjectIgisTableNamesEveryAimableWeaponAndLeavesTheRestAlone()
    {
        AdsProfileTable table = AdsProfileTable.Igi1;
        string[] aimable =
        [
            "WEAPON_ID_GLOCK", "WEAPON_ID_COLT", "WEAPON_ID_DESERTEAGLE", "WEAPON_ID_UZI", "WEAPON_ID_MP5SD", "WEAPON_ID_AK47",
            "WEAPON_ID_M16A2", "WEAPON_ID_MINIMI", "WEAPON_ID_SPAS12", "WEAPON_ID_JACKHAMMER", "WEAPON_ID_RPG18", "WEAPON_ID_UZIX2",
        ];
        foreach (string weapon in aimable)
        {
            Assert.True(table.TryGet(weapon, out _), $"{weapon} should be aimable.");
        }

        foreach (string weapon in (string[])["WEAPON_ID_KNIFE", "WEAPON_ID_DRAGUNOV", "WEAPON_ID_GRENADE", "WEAPON_ID_BINOCULARS",
                     "WEAPON_ID_MEDIPACK", "WEAPON_ID_PROXIMITYMINE", "WEAPON_ID_FLASHBANG", "WEAPON_ID_C4BOMB"])
        {
            Assert.False(table.TryGet(weapon, out _), $"{weapon} should keep its own use.");
        }

        Assert.True(table.TryGet("WEAPON_ID_UZIX2", out AdsProfile dual));
        Assert.Equal(AdsKind.Focus, dual.Kind);
        Assert.True(table.TryGet("WEAPON_ID_GLOCK", out AdsProfile glock));
        Assert.False(glock.CanMount);
        Assert.Equal(0.15f, glock.InSeconds);
    }

    [Fact]
    public void AModsTableReplacesOnlyTheWeaponsItNames()
    {
        AdsProfileTable table = AdsProfileTable.Igi1.Overlay("""
            { "classes": { "fast": { "in": 0.05 } }, "weapons": { "WEAPON_ID_AK47": { "class": "fast", "rear": [0, 0.2, 0.04] } } }
            """);
        Assert.True(table.TryGet("WEAPON_ID_AK47", out AdsProfile ak));
        Assert.Equal(0.05f, ak.InSeconds);
        Assert.True(table.TryGet("WEAPON_ID_M16A2", out AdsProfile m16));
        Assert.Equal(0.26f, m16.InSeconds);
        Assert.Throws<System.Text.Json.JsonException>(() => AdsProfileTable.Parse("""{ "weapons": { "X": { "speed": 1 } } }"""));
    }

    [Fact]
    public void TheSightsComeUpInTheProfilesTimeAndGoDownInTheirOwn()
    {
        AdsProfile profile = new() { ScriptId = "W", InSeconds = 0.2f, OutSeconds = 0.1f };
        AimDownSights ads = new();
        int ticks = 0;
        while (!ads.IsOnSights)
        {
            ads.Tick(On, profile, aimHeld: true, aimPressed: ticks == 0, canAim: true);
            ticks++;
        }

        Assert.Equal(6, ticks);
        ads.Tick(On, profile, false, false, true);
        ads.Tick(On, profile, false, false, true);
        ads.Tick(On, profile, false, false, true);
        Assert.Equal(0f, ads.Progress);
    }

    [Fact]
    public void AReloadTakesTheSightsDownAndAHeldButtonBringsThemBack()
    {
        AdsProfile profile = new() { ScriptId = "W", InSeconds = 0.1f, OutSeconds = 0.1f };
        AimDownSights ads = new();
        for (int i = 0; i < 5; i++)
        {
            ads.Tick(On, profile, true, i == 0, canAim: true);
        }

        Assert.True(ads.IsOnSights);
        for (int i = 0; i < 5; i++)
        {
            ads.Tick(On, profile, true, false, canAim: false);
        }

        Assert.Equal(0f, ads.Progress);
        Assert.True(ads.Wanted);
        ads.Tick(On, profile, true, false, canAim: true);
        Assert.True(ads.Progress > 0f);
    }

    [Fact]
    public void ToggleLatchesOnAPressAndANewWeaponForgetsIt()
    {
        AdsProfile first = new() { ScriptId = "A" };
        AdsProfile second = new() { ScriptId = "B" };
        AimDownSights ads = new();
        ModernAimOptions toggle = On with { Toggle = true };
        ads.Tick(toggle, first, aimHeld: true, aimPressed: true, canAim: true);
        ads.Tick(toggle, first, aimHeld: false, aimPressed: false, canAim: true);
        Assert.True(ads.Wanted);
        ads.Tick(toggle, second, aimHeld: false, aimPressed: false, canAim: true);
        Assert.False(ads.Wanted);
    }

    [Fact]
    public void WithModernAimOffNothingIsEverAimed()
    {
        AimDownSights ads = new();
        AdsProfile profile = new() { ScriptId = "W" };
        for (int i = 0; i < 20; i++)
        {
            ads.Tick(ModernAimOptions.Off, profile, true, i == 0, true);
        }

        Assert.Equal(0f, ads.Progress);
        Assert.False(ads.Wanted);
    }

    [Fact]
    public void TheEasingStartsAndEndsStillAndIsTheSameBothWays()
    {
        Assert.Equal(0f, AimDownSights.Shape(0f));
        Assert.Equal(1f, AimDownSights.Shape(1f));
        Assert.Equal(0.5f, AimDownSights.Shape(0.5f), 5);
        Assert.True(AimDownSights.Shape(0.05f) < 0.01f);
        Assert.True(AimDownSights.Shape(0.95f) > 0.99f);
    }

    [Fact]
    public void TheAlignmentPutsBothSightsOnTheLineOfSightAtTheEyeRelief()
    {
        // A weapon held right of, below and ahead of the eye, turned a little off the axis.
        AdsProfile profile = new()
        {
            ScriptId = "W",
            RearSight = new Vector3f(0.002f, -0.07f, 0.038f),
            FrontSight = new Vector3f(0f, 0.12f, 0.035f),
            EyeReliefMetres = 0.3f,
        };
        Vector3f hold = new Vector3f(0.035f, -0.048f, -0.033f) * UnitsPerMetre;
        Vector3f bone = new Vector3f(0.08f, 0.45f, -0.04f) * UnitsPerMetre;
        Matrix3f turned = Matrix3f.RotationZ(0.05f) * Matrix3f.RotationX(-0.03f);

        RigOffset move = SightAlignment.Solve(profile, hold, bone, turned);
        Vector3f rear = move.Apply(hold + bone + RigOffset.Multiply(turned, profile.RearSight * UnitsPerMetre));
        Vector3f front = move.Apply(hold + bone + RigOffset.Multiply(turned, profile.FrontSight * UnitsPerMetre));

        Assert.Equal(0f, rear.X, 1);
        Assert.Equal(0f, rear.Z, 1);
        Assert.Equal(0.3f * UnitsPerMetre, rear.Y, 1);
        Assert.Equal(0f, front.X, 1);
        Assert.Equal(0f, front.Z, 1);
        Assert.True(front.Y > rear.Y);
    }

    [Fact]
    public void AFocusedWeaponIsOnlyShifted()
    {
        AdsProfile profile = new() { ScriptId = "W", Kind = AdsKind.Focus, FocusOffsetMetres = new Vector3f(0f, 0.02f, 0.035f) };
        RigOffset move = SightAlignment.Solve(profile, default, default, Matrix3f.Identity);
        Assert.Equal(Matrix3f.Identity, move.Rotation);
        Assert.Equal(0.035f * UnitsPerMetre, move.Translation.Z, 2);
    }

    [Fact]
    public void MovesComposeInOrderAndBlendFromTheIdentity()
    {
        RigOffset turn = new(Matrix3f.RotationZ(MathF.PI / 2f), default);
        RigOffset shift = new(Matrix3f.Identity, new Vector3f(1f, 0f, 0f));
        Vector3f point = new(1f, 0f, 0f);

        Vector3f turnThenShift = turn.Then(shift).Apply(point);
        Assert.Equal(1f, turnThenShift.X, 4);
        Assert.Equal(1f, turnThenShift.Y, 4);
        Assert.Equal(RigOffset.Identity, RigOffset.Blend(RigOffset.Identity, shift, 0f));
        Assert.Equal(0.5f, RigOffset.Blend(RigOffset.Identity, shift, 0.5f).Translation.X, 5);
    }

    [Fact]
    public void TheMotionRestsAtTheIdentityAndAShotThrowsItAndItSettles()
    {
        ViewModelMotion motion = new();
        RigOffset rest = motion.Advance(new ViewModelMotionFrame(1f / 60f, 0f, 0f, 0f, 0, null, 0f));
        Assert.Equal(0f, rest.Translation.Length, 3);

        RigOffset kicked = default;
        for (int i = 0; i < 3; i++)
        {
            kicked = motion.Advance(new ViewModelMotionFrame(1f / 60f, 0f, 0f, 0f, 1, null, 0f));
        }

        Assert.True(kicked.Translation.Y < -10f, "A shot should drive the weapon back.");
        for (int i = 0; i < 240; i++)
        {
            kicked = motion.Advance(new ViewModelMotionFrame(1f / 60f, 0f, 0f, 0f, 1, null, 0f));
        }

        Assert.Equal(0f, kicked.Translation.Length, 0);
    }

    [Fact]
    public void TheSpreadScaleNeverReachesNoughtSoTheRandomDrawsAreTheOriginals()
    {
        Weapons.HeldGun gun = new(new Weapons.WeaponType()) { SpreadScale = 0f };
        Assert.True(gun.SpreadScale > 0f);
    }
}

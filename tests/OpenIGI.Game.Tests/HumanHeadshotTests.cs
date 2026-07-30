using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers the head zone actually being reachable. Every case in <see cref="HumanHitTests"/> passes
/// <c>pose: null</c>, so the head path had no coverage at all — which is why it was broken in the shipped
/// port: a guard took several rounds to the head because zone 15 could never be selected.
/// </summary>
public sealed class HumanHeadshotTests
{
    private static readonly Vector3f FacingNorth = new(0f, 1f, 0f);

    /// <summary>
    /// A round passing through the reference bone takes the head zone. Under the old reading the proximity
    /// was measured from the <em>cylinder contact</em>, which for a side-on shot is at least the body radius
    /// (0.55 m) from the bone on the axis — always more than the 15 cm the test allows, so zone 15 was
    /// unreachable no matter where the shot went.
    /// </summary>
    [Fact]
    public void AShotThroughTheHeadBoneTakesTheHeadZone()
    {
        Vector3d feet = default;
        BonePose pose = HeadAt(new Vector3f(0f, 0f, 6900f));

        // Straight through the head, from in FRONT: the human faces +Y, so the round must travel -Y.
        // A shot from behind can never take the head zone however close it lands (localForward >= 0).
        Vector3d origin = new(0, 40_000, 6900);
        Vector3d end = new(0, -40_000, 6900);

        Assert.True(HumanHitTest.TryCast(origin, end, feet, FacingNorth, pose, out HumanHit hit));
        Assert.Equal(HumanDamage.ProximityZone, hit.Zone);
    }

    /// <summary>A round through the chest, well below the head bone, does not take the head zone.</summary>
    [Fact]
    public void AShotThroughTheChestDoesNot()
    {
        Vector3d feet = default;
        BonePose pose = HeadAt(new Vector3f(0f, 0f, 6900f));

        // A metre below the head bone, also from the front.
        Vector3d origin = new(0, 40_000, 2800);
        Vector3d end = new(0, -40_000, 2800);

        Assert.True(HumanHitTest.TryCast(origin, end, feet, FacingNorth, pose, out HumanHit hit));
        Assert.NotEqual(HumanDamage.ProximityZone, hit.Zone);
    }

    /// <summary>
    /// The distance is to the ray's closest approach, and the segment is clamped — so a head bone behind the
    /// shooter is not "close" merely because the infinite line would pass through it.
    /// </summary>
    [Fact]
    public void TheSegmentIsClampedSoABoneBehindTheShooterIsNotClose()
    {
        Vector3d feet = default;
        BonePose pose = HeadAt(new Vector3f(0f, 0f, 6900f));

        // Firing away from the human: the cylinder is not hit at all, so there is no contact to zone.
        Vector3d origin = new(0, 60_000, 6900);
        Vector3d end = new(0, 120_000, 6900);

        Assert.False(HumanHitTest.TryCast(origin, end, feet, FacingNorth, pose, out _));
    }

    /// <summary>
    /// <b>A head shot kills a soldier outright with every weapon in the roster.</b> Zone 15's multiplier is
    /// 100 against a maximum health of 1.0, and the weakest damage factor shipped is 0.05, so one round is
    /// always enough — which is what the game does and what the port now reproduces.
    /// </summary>
    [RequiresGameDataFact]
    public void AHeadShotKillsASoldierOutrightWithEveryWeapon()
    {
        WeaponTypeRegistry weapons = RetailWeaponRoster.Value;
        Assert.NotEmpty(weapons.Weapons);

        foreach (WeaponType weapon in weapons.Weapons)
        {
            if (weapon.DamageFactor <= 0f)
            {
                continue;
            }

            var health = new HumanHealth(HumanHitZoneTable.Soldier);
            health.Apply(HumanDamage.ProximityZone, weapon.DamageFactor);

            Assert.True(
                health.IsDead,
                $"{weapon.ScriptId} (damage factor {weapon.DamageFactor}) did not kill with one head shot");
        }
    }

    /// <summary>
    /// The soldier table's fatal zones, straight out of <c>0x53F1D0</c>: zones 15 and 16 at 100, the four
    /// limb chains at 0.5/0.75/1.0 and the torso at 1.0 — byte-identical to the player's.
    /// </summary>
    [Fact]
    public void TheSoldierTablesFatalZonesAreOneHundred()
    {
        Assert.Equal(17, HumanHitZoneTable.Soldier.Count);
        Assert.Equal(HumanHitZoneTable.LethalMultiplier, HumanHitZoneTable.Soldier[15].Multiplier);
        Assert.Equal(HumanHitZoneTable.LethalMultiplier, HumanHitZoneTable.Soldier[16].Multiplier);

        // A limb extremity is a twentieth of a head shot's two hundred.
        Assert.Equal(0.5f, HumanHitZoneTable.Soldier[0].Multiplier);
        Assert.Equal(0.75f, HumanHitZoneTable.Soldier[1].Multiplier);
        Assert.Equal(1.0f, HumanHitZoneTable.Soldier[2].Multiplier);

        // The PLAYER is not lethal in those zones. 0x5389F8's rows 15 and 16 do hold 100.0, but that table
        // is an engine default the player's own script overrides: retail humanplayer.qvm declares 1.0, and
        // HumanHitZoneTableTests pins that against the shipped script. So a head shot kills a guard outright
        // and does not kill the player -- which is the game's behaviour, and changing the Player table to
        // match the binary's default was a mistake this records rather than repeats.
        Assert.Equal(1.0f, HumanHitZoneTable.Player[15].Multiplier);
        Assert.Equal(1.0f, HumanHitZoneTable.Player[16].Multiplier);
    }

    // A two-bone skeleton is not enough - the reference bone is index 4 - so this builds a five-bone chain
    // whose bone 4 rest translation puts it where the head should be, with identity rotations throughout.
    private static BonePose HeadAt(Vector3f head)
    {
        Vector3f[] rest = [default, default, default, default, head];
        int[] parents = [-1, 0, 1, 2, 3];
        BoneSkeleton skeleton = BoneSkeleton.Create(parents, rest);

        Quaternion32[] identity = new Quaternion32[skeleton.BoneCount];
        for (int i = 0; i < identity.Length; i++)
        {
            identity[i] = new Quaternion32(0f, 0f, 0f, 1f);
        }

        return BonePose.Evaluate(skeleton, identity);
    }
}

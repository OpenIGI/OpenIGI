using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Which body part a round goes through, and what that is worth.
/// </summary>
/// <remarks>
/// <para>
/// The rig here is the retail hierarchy — <c>RetailHumanRigTests</c> checks that every shipped human
/// skeleton has exactly these parents — with rest translations laid out as a plain upright figure so a
/// shot at a stated height lands on a stated limb. What is under test is the resolution, not the
/// artist's proportions.
/// </para>
/// </remarks>
public sealed class HumanBoneZoneTests
{
    private const float Metre = 4096f;

    private static readonly Vector3f FacingNorth = new(0f, 1f, 0f);

    /// <summary>The weapon chain is not a body part and can never be what a round went through.</summary>
    /// <remarks>
    /// Bones 20 to 22 run out in front of the chest, so without this a round to the chest would resolve
    /// to whichever of them happened to be nearest and score as a limb.
    /// </remarks>
    [Fact]
    public void TheWeaponChainIsNeverTheStruckPart()
    {
        Assert.Equal(-1, HumanDamage.BodyPartOfBone(20));
        Assert.Equal(-1, HumanDamage.BodyPartOfBone(21));
        Assert.Equal(-1, HumanDamage.BodyPartOfBone(22));

        // And the compaction is exactly "drop three above the chain".
        Assert.Equal(19, HumanDamage.BodyPartOfBone(19));
        Assert.Equal(20, HumanDamage.BodyPartOfBone(23));
        Assert.Equal(29, HumanDamage.BodyPartOfBone(32));
    }

    /// <summary>
    /// The two legs are mirror images through the table, which is the whole argument for the
    /// compaction: read raw, one leg's shin would score as torso.
    /// </summary>
    [Fact]
    public void TheTwoLegsMirrorEachOtherThroughTheTable()
    {
        // hip, thigh, shin, foot, toe -- root to extremity, both sides.
        Assert.Equal([8, 7, 6, 6, 6], Zones(23, 5));
        Assert.Equal([2, 1, 0, 0, 0], Zones(28, 5));

        // And the arms likewise: clavicle, upper arm, forearm, hand, two fingers.
        Assert.Equal([11, 10, 9, 9, 9, 9], Zones(8, 6));
        Assert.Equal([5, 4, 3, 3, 3, 3], Zones(14, 6));

        // Their multipliers run 1.0, 0.75, 0.5 outward on all four limbs.
        Assert.Equal(1.0f, HumanHitZoneTable.Soldier[8].Multiplier);
        Assert.Equal(0.75f, HumanHitZoneTable.Soldier[7].Multiplier);
        Assert.Equal(0.5f, HumanHitZoneTable.Soldier[6].Multiplier);
    }

    /// <summary>
    /// Reading the index as a raw rig bone gives an incoherent map — the check that rules the
    /// alternative out rather than merely preferring the one that was chosen.
    /// </summary>
    [Fact]
    public void TheUncompactedReadingIsAsymmetric()
    {
        // One leg would read hip 6, thigh 6, shin 2, foot 1 ...
        Assert.Equal([6, 6, 2, 1, 0], Raw(23, 5));

        // ... while the other read hip 0, thigh 0, and then its shin, foot and toe fell past the table
        // and scored as TORSO.
        Assert.Equal(
            [0, 0, HumanDamage.DefaultZone, HumanDamage.DefaultZone, HumanDamage.DefaultZone],
            Raw(28, 5));
    }

    /// <summary>A round through the chest resolves to the trunk and is refined by direction.</summary>
    [Fact]
    public void AShotThroughTheChestTakesTheTrunk()
    {
        BonePose pose = Figure();

        HumanHit hit = Shoot(pose, height: 1.35f, from: 1);
        Assert.InRange(hit.Bone, 0, 3);
        Assert.Equal(HumanDamage.FrontZone, hit.Zone);

        HumanHit behind = Shoot(pose, height: 1.35f, from: -1);
        Assert.InRange(behind.Bone, 0, 3);
        Assert.Equal(HumanDamage.RearZone, behind.Zone);
    }

    /// <summary>A round through a thigh resolves to a thigh, not to the trunk.</summary>
    /// <remarks>
    /// This is the behaviour the port did not have: before the part was resolved at all, every body hit
    /// scored as zone 12 and a leg was worth as much as a chest.
    /// </remarks>
    [Fact]
    public void AShotThroughAThighTakesAThighZone()
    {
        BonePose pose = Figure();

        HumanHit left = Shoot(pose, height: 0.6f, from: 1, side: -0.15f);
        Assert.Equal(24, left.Bone);
        Assert.Equal(7, left.Zone);

        HumanHit right = Shoot(pose, height: 0.6f, from: 1, side: 0.15f);
        Assert.Equal(29, right.Bone);
        Assert.Equal(1, right.Zone);

        Assert.Equal(0.75f, HumanHitZoneTable.Soldier[left.Zone].Multiplier);
        Assert.Equal(0.75f, HumanHitZoneTable.Soldier[right.Zone].Multiplier);
    }

    /// <summary>A round through a shin resolves to the cheapest zone there is.</summary>
    [Fact]
    public void AShotThroughAShinTakesTheExtremityZone()
    {
        BonePose pose = Figure();

        HumanHit hit = Shoot(pose, height: 0.25f, from: 1, side: -0.15f);
        Assert.InRange(hit.Bone, 25, 27);
        Assert.Equal(6, hit.Zone);
        Assert.Equal(0.5f, HumanHitZoneTable.Soldier[hit.Zone].Multiplier);
    }

    /// <summary>A round through the neck takes the head zone, and kills a soldier outright.</summary>
    [Fact]
    public void AShotThroughTheNeckTakesTheHeadZone()
    {
        BonePose pose = Figure();

        HumanHit hit = Shoot(pose, height: 1.6f, from: 1);
        Assert.Equal(HumanDamage.ProximityZone, hit.Zone);

        HumanHealth health = new(HumanHitZoneTable.Soldier);
        health.Apply(hit.Zone, damageFactor: 0.05f);
        Assert.True(health.IsDead);
    }

    /// <summary>
    /// <b>A head shot kills a guard in one round at every difficulty</b>, including the hard row where
    /// a guard is worth nearly three times what it is worth on easy.
    /// </summary>
    /// <remarks>
    /// Zone 15's multiplier is 100 and the weakest damage factor in the game is 0.05, so even 0.8 of a
    /// person goes down to one round. The point of the test is that the configured maximum is what is
    /// being compared against, not the constructor's 1.0.
    /// </remarks>
    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.5f)]
    [InlineData(0.8f)]
    public void AHeadShotKillsAGuardAtEveryDifficulty(float maximumHealth)
    {
        BonePose pose = Figure();
        HumanHit hit = Shoot(pose, height: 1.6f, from: 1);
        Assert.Equal(HumanDamage.ProximityZone, hit.Zone);

        HumanHealth health = new(HumanHitZoneTable.Soldier, maximumHealth);
        health.Apply(hit.Zone, damageFactor: 0.05f);
        Assert.True(health.IsDead);
    }

    /// <summary>
    /// A guard on hard soaks rounds a guard on easy does not — the configured maximum reaching the
    /// place it decides something.
    /// </summary>
    [Fact]
    public void TheConfiguredMaximumChangesHowManyBodyShotsAGuardTakes()
    {
        // A trunk hit is worth exactly the weapon's damage factor, so this counts rounds directly.
        HumanHealth easy = new(HumanHitZoneTable.Soldier, maximumHealth: 0.3f);
        HumanHealth hard = new(HumanHitZoneTable.Soldier, maximumHealth: 0.8f);

        easy.Apply(HumanDamage.FrontZone, damageFactor: 0.25f);
        hard.Apply(HumanDamage.FrontZone, damageFactor: 0.25f);
        Assert.False(easy.IsDead);
        Assert.False(hard.IsDead);

        easy.Apply(HumanDamage.FrontZone, damageFactor: 0.25f);
        hard.Apply(HumanDamage.FrontZone, damageFactor: 0.25f);
        Assert.True(easy.IsDead);
        Assert.False(hard.IsDead);

        // Four rounds on hard, two on easy.
        hard.Apply(HumanDamage.FrontZone, damageFactor: 0.25f);
        Assert.False(hard.IsDead);
        hard.Apply(HumanDamage.FrontZone, damageFactor: 0.25f);
        Assert.True(hard.IsDead);
    }

    /// <summary>The retail rows really do give guards those three maxima.</summary>
    [Fact]
    public void TheDefaultConfigurationsMaximumIsTheConstructorsOne()
    {
        HumanAiConfigRegistry config = new();
        Assert.Equal(
            HumanHitZoneTable.MaximumHealth,
            config.Parameters(AiType.GuardAk, AiDifficulty.Normal).MaximumHealth,
            5);
    }

    private static int[] Zones(int firstBone, int count)
    {
        int[] result = new int[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = HumanDamage.ZoneOfBone(firstBone + i);
        }

        return result;
    }

    private static int[] Raw(int firstBone, int count)
    {
        int[] result = new int[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = HumanDamage.ZoneOfBodyPart(firstBone + i);
        }

        return result;
    }

    // Fires horizontally through the figure at a stated height, from the front (+1) or the back (-1),
    // offset sideways by `side` metres. The figure faces +Y, so a shot from the front travels -Y.
    private static HumanHit Shoot(BonePose pose, float height, int from, float side = 0f)
    {
        double x = side * Metre;
        double z = height * Metre;
        Vector3d origin = new(x, from * 10.0 * Metre, z);
        Vector3d end = new(x, -from * 10.0 * Metre, z);

        Assert.True(
            HumanHitTest.TryCast(origin, end, default, FacingNorth, pose, out HumanHit hit),
            $"the round missed at {height} m");

        return hit;
    }

    // The retail hierarchy, with rest translations that stand it up: pelvis at 0.95 m, head at 1.7 m,
    // arms hanging at the sides, legs down to the ground, and the weapon chain out in front.
    private static BonePose Figure()
    {
        int[] parents =
        [
            -1, 0, 1, 2, 3, 4, 4, 6, 3, 8, 9, 10, 11, 12, 3, 14, 15, 16, 17, 18,
            1, 20, 21, 0, 23, 24, 25, 26, 0, 28, 29, 30, 31,
        ];

        Vector3f[] rest = new Vector3f[parents.Length];

        // Pelvis and spine, straight up.
        rest[0] = new Vector3f(0f, 0f, 0f);
        rest[1] = Up(0.15f);
        rest[2] = Up(0.20f);
        rest[3] = Up(0.20f);

        // Neck, then the head above it and a second branch.
        rest[4] = Up(0.15f);
        rest[5] = Up(0.20f);
        rest[6] = Up(0.05f);
        rest[7] = Up(0.05f);

        // Left arm off the spine top: out, then straight down.
        rest[8] = Side(-0.20f);
        rest[9] = new Vector3f(-0.05f * Metre, 0f, -0.28f * Metre);
        rest[10] = Up(-0.26f);
        rest[11] = Up(-0.10f);
        rest[12] = Up(-0.04f);
        rest[13] = Up(-0.03f);

        // Right arm, mirrored.
        rest[14] = Side(0.20f);
        rest[15] = new Vector3f(0.05f * Metre, 0f, -0.28f * Metre);
        rest[16] = Up(-0.26f);
        rest[17] = Up(-0.10f);
        rest[18] = Up(-0.04f);
        rest[19] = Up(-0.03f);

        // The weapon chain, out in front of the chest and well clear of any body part.
        rest[20] = new Vector3f(0f, 0.18f * Metre, -0.05f * Metre);
        rest[21] = new Vector3f(0f, 0.50f * Metre, 0.10f * Metre);
        rest[22] = new Vector3f(0f, 0.40f * Metre, 0.30f * Metre);

        // Left leg off the root.
        rest[23] = Side(-0.10f);
        rest[24] = Up(-0.05f);
        rest[25] = Up(-0.45f);
        rest[26] = Up(-0.42f);
        rest[27] = new Vector3f(0f, 0.10f * Metre, -0.03f * Metre);

        // Right leg, mirrored.
        rest[28] = Side(0.10f);
        rest[29] = Up(-0.05f);
        rest[30] = Up(-0.45f);
        rest[31] = Up(-0.42f);
        rest[32] = new Vector3f(0f, 0.10f * Metre, -0.03f * Metre);

        BoneSkeleton skeleton = BoneSkeleton.Create(parents, rest);
        Quaternion32[] identity = new Quaternion32[skeleton.BoneCount];
        for (int i = 0; i < identity.Length; i++)
        {
            identity[i] = new Quaternion32(0f, 0f, 0f, 1f);
        }

        // The pelvis stands 0.95 m up, which is where a standing clip's root track puts it.
        return BonePose.Evaluate(skeleton, identity, new Vector3f(0f, 0f, 0.95f * Metre));
    }

    private static Vector3f Up(float metres) => new(0f, 0f, metres * Metre);

    private static Vector3f Side(float metres) => new(metres * Metre, 0f, 0f);
}

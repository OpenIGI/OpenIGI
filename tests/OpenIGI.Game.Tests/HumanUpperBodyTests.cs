using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The torso half of a human rig: the weapon clip that plays on bone 1, and the bend that makes a body
/// drawn from outside look where its owner is looking.
/// </summary>
public sealed class HumanUpperBodyTests
{
    private const float Metre = 4096f;

    /// <summary>
    /// The upper-body clip claims the torso, the arms, the head and the weapon — and not the legs.
    /// </summary>
    /// <remarks>
    /// The whole reason <c>0x489ED0</c> can name a single bone and get a clean split: bones 20–22 (the
    /// weapon chain) hang off bone 1, the head and both arms hang off the spine top above it, and the two
    /// legs hang off the root beside it. Nothing is masked; the hierarchy does it.
    /// </remarks>
    [Fact]
    public void TheUpperBodyClipTakesTheTorsoAndLeavesTheLegs()
    {
        SoldierView view = View();

        Assert.True(view.PlayStateAnimation(WalkAnimationId, loops: true));
        Assert.True(view.PlayUpperBodyAnimation(AimAnimationId, loops: true));

        BoneObject bones = view.Bones;
        BoneClip? legs = bones.EffectiveSourceOf(0);
        BoneClip? torso = bones.EffectiveSourceOf(HumanUpperBody.Bone);

        Assert.NotNull(legs);
        Assert.NotNull(torso);
        Assert.NotSame(legs, torso);

        // Everything bone 1 owns: the spine above it, the neck and head, both arms, the weapon chain.
        foreach (int bone in new[] { 1, 2, 3, 4, 5, 8, 10, 14, 16, 20, 21, 22 })
        {
            Assert.Same(torso, bones.EffectiveSourceOf(bone));
        }

        // Everything it does not: the pelvis and the two legs.
        foreach (int bone in new[] { 0, 23, 25, 27, 28, 30, 32 })
        {
            Assert.Same(legs, bones.EffectiveSourceOf(bone));
        }
    }

    /// <summary>Standing the torso down hands it back to the clip driving the legs.</summary>
    [Fact]
    public void StoppingTheUpperBodyReturnsTheTorsoToTheLegsClip()
    {
        SoldierView view = View();
        view.PlayStateAnimation(WalkAnimationId, loops: true);
        view.PlayUpperBodyAnimation(AimAnimationId, loops: true);
        Assert.Equal(AimAnimationId, view.UpperBodyAnimation);

        view.StopUpperBodyAnimation();

        Assert.Equal(-1, view.UpperBodyAnimation);
        Assert.Same(
            view.Bones.EffectiveSourceOf(0),
            view.Bones.EffectiveSourceOf(HumanUpperBody.Bone));
    }

    /// <summary>
    /// A ladder, a keypad or a wire takes the WHOLE body back — torso clip and aim bend both.
    /// </summary>
    /// <remarks>
    /// The regression this exists for. Those states are single authored clips that animate the character
    /// from the boots up and play on bone 0; a weapon stance left on bone 1, or a spine still bent to an
    /// aim, replaces everything above the waist and puts a man on a ladder in a rifle pose. The
    /// third-person view introduced both overrides, so it is the third-person view's job to get out of
    /// the way — and the release is on the rig's own entry point so no call ordering can skip it.
    /// </remarks>
    [Fact]
    public void AnAuthoredFullBodyClipTakesTheTorsoAndTheBendBack()
    {
        SoldierView view = View();
        view.PlayStateAnimation(StandAnimationId, loops: true);
        view.PlayUpperBodyAnimation(AimAnimationId, loops: true);
        view.AimAlong(0.6f);

        Assert.NotSame(view.Bones.EffectiveSourceOf(0), view.Bones.EffectiveSourceOf(HumanUpperBody.Bone));
        Assert.NotNull(view.Bones.BendOf(HumanUpperBody.Bone));

        Assert.True(view.StartLadderAnimation(LadderAnimationId, loops: true));

        Assert.Equal(-1, view.UpperBodyAnimation);
        Assert.Same(
            view.Bones.EffectiveSourceOf(0),
            view.Bones.EffectiveSourceOf(HumanUpperBody.Bone));
        Assert.Null(view.Bones.BendOf(HumanUpperBody.Bone));
        Assert.Null(view.Bones.BendOf(HumanUpperBody.NeckBone));
    }

    /// <summary>A fall is a full-body clip too: a corpse does not go down holding a rifle stance.</summary>
    [Fact]
    public void DyingTakesTheTorsoAndTheBendBack()
    {
        SoldierView view = View();
        view.PlayStateAnimation(StandAnimationId, loops: true);
        view.PlayUpperBodyAnimation(AimAnimationId, loops: true);
        view.AimAlong(-0.5f);

        view.Kill(DeathAnimationId);

        Assert.Equal(-1, view.UpperBodyAnimation);
        Assert.Same(
            view.Bones.EffectiveSourceOf(0),
            view.Bones.EffectiveSourceOf(HumanUpperBody.Bone));
        Assert.Null(view.Bones.BendOf(HumanUpperBody.NeckBone));
        Assert.Equal(DeathAnimationId, view.CurrentAnimation);
    }

    /// <summary>Looking up raises the weapon, tips the head back, and leaves the feet on the ground.</summary>
    /// <remarks>
    /// The head is checked by where it FACES rather than by where it is. It sits directly above the neck
    /// joint, so tipping it back swings it backwards and very slightly down — which is what a head does,
    /// and asserting that it rises would be asserting something no neck can do.
    /// </remarks>
    [Fact]
    public void AimingUpRaisesTheWeaponAndTipsTheHeadBack()
    {
        SoldierView view = View();
        view.PlayStateAnimation(StandAnimationId, loops: true);

        BonePose level = view.UnrotatedPose();
        float weaponZ = level.Positions[22].Z;
        float headY = level.Positions[5].Y;
        float footZ = level.Positions[27].Z;

        view.AimAlong(0.6f);
        BonePose up = view.UnrotatedPose();

        Assert.True(up.Positions[22].Z > weaponZ, "aiming up did not raise the weapon.");
        Assert.True(PitchOf(up.Rotations[5]) > 0.1f, "aiming up did not tip the head back.");
        Assert.True(up.Positions[5].Y < headY, "a head tipped back should move behind the neck.");
        Assert.Equal(footZ, up.Positions[27].Z, 3);

        view.AimAlong(-0.6f);
        BonePose down = view.UnrotatedPose();

        Assert.True(down.Positions[22].Z < weaponZ, "aiming down did not lower the weapon.");
        Assert.True(PitchOf(down.Rotations[5]) < -0.1f, "aiming down did not tip the head forward.");
        Assert.Equal(footZ, down.Positions[27].Z, 3);
    }

    /// <summary>The head moves further than the chest does, which is what reads as looking rather than bowing.</summary>
    [Fact]
    public void TheHeadTurnsFurtherThanTheChestCarriesIt()
    {
        SoldierView view = View();
        view.PlayStateAnimation(StandAnimationId, loops: true);
        view.AimAlong(0.6f);

        Matrix3f chest = view.UnrotatedPose().Rotations[HumanUpperBody.Bone];
        Matrix3f head = view.UnrotatedPose().Rotations[HumanUpperBody.NeckBone];

        Assert.True(
            PitchOf(head) > PitchOf(chest),
            $"the head should end up above the chest's own pitch: {PitchOf(head)} against {PitchOf(chest)}.");
    }

    /// <summary>
    /// A player looking almost straight up does not fold their character in half.
    /// </summary>
    /// <remarks>
    /// <c>PlayerBody.PitchLimit</c> is 89°, which no spine reaches. Both bends cap, and what is left over
    /// is simply not expressed rather than being handed to a joint that cannot take it.
    /// </remarks>
    [Fact]
    public void AnExtremeAimIsCappedRatherThanApplied()
    {
        SoldierView capped = View();
        capped.PlayStateAnimation(StandAnimationId, loops: true);
        capped.AimAlong(PlayerBody.PitchLimit);

        SoldierView atLimit = View();
        atLimit.PlayStateAnimation(StandAnimationId, loops: true);
        atLimit.AimAlong(
            HumanUpperBody.ChestLimitRadians / HumanUpperBody.ChestShare);

        float chest = PitchOf(capped.UnrotatedPose().Rotations[HumanUpperBody.Bone]);

        Assert.Equal(HumanUpperBody.ChestLimitRadians, chest, 3);
        Assert.Equal(
            PitchOf(atLimit.UnrotatedPose().Rotations[HumanUpperBody.Bone]),
            chest,
            3);
    }

    /// <summary>Aiming level is the pose the rig would have had with no bend at all.</summary>
    [Fact]
    public void AimingLevelLeavesTheRigExactlyAsItWas()
    {
        SoldierView view = View();
        view.PlayStateAnimation(StandAnimationId, loops: true);
        Vector3f before = view.UnrotatedPose().Positions[22];

        view.AimAlong(0.5f);
        view.AimAlong(0f);
        Vector3f after = view.UnrotatedPose().Positions[22];

        Assert.Equal(before.X, after.X, 4);
        Assert.Equal(before.Y, after.Y, 4);
        Assert.Equal(before.Z, after.Z, 4);
        Assert.Null(view.Bones.BendOf(HumanUpperBody.Bone));
    }

    // The pose's 3x3 is applied as M * v, so its third ROW is where the bone's own +Z ended up and its
    // second is where +Y did; the pitch is how far the forward axis has been raised.
    private static float PitchOf(in Matrix3f rotation) =>
        MathF.Asin(Math.Clamp(rotation.M32, -1f, 1f));

    private const int StandAnimationId = 2;
    private const int WalkAnimationId = 3;
    private const int AimAnimationId = 4;
    private const int LadderAnimationId = 5;
    private const int DeathAnimationId = 6;

    private static SoldierView View()
    {
        BoneAnimationSet set = Set();
        HumanSoldierTask task = new()
        {
            Model = "test",
            BoneHierarchy = set.Skeleton.Id,
            StandAnimation = StandAnimationId,
            Position = default,
            Gamma = 0f,
        };

        return new SoldierView(task, set);
    }

    private static BoneAnimationSet Set()
    {
        BoneSkeleton skeleton = Rig();
        return BoneAnimationSet.Create(
            skeleton,
            Still(StandAnimationId, skeleton.BoneCount),
            Still(WalkAnimationId, skeleton.BoneCount),
            Still(AimAnimationId, skeleton.BoneCount),
            Still(LadderAnimationId, skeleton.BoneCount),
            Still(DeathAnimationId, skeleton.BoneCount));
    }

    // A clip that holds every bone at its rest orientation, so the pose under test is the rig's own
    // shape plus whatever bend is applied -- and nothing else.
    private static BoneAnimation Still(int id, int bones)
    {
        Quaternion32 identity = new(0f, 0f, 0f, 1f);
        BoneRotationKey[][] tracks = new BoneRotationKey[bones][];
        for (int bone = 0; bone < bones; bone++)
        {
            tracks[bone] = [new BoneRotationKey(identity, 0f, identity, identity)];
        }

        return BoneAnimation.Create(id, duration: 320f, tracks);
    }

    // The retail 33-bone hierarchy, standing up: pelvis at 0.95 m, head above it, arms at the sides,
    // legs to the ground, and the weapon chain out in front of the chest. The same figure
    // HumanBoneZoneTests builds, because it is the same rig.
    private static BoneSkeleton Rig()
    {
        int[] parents =
        [
            -1, 0, 1, 2, 3, 4, 4, 6, 3, 8, 9, 10, 11, 12, 3, 14, 15, 16, 17, 18,
            1, 20, 21, 0, 23, 24, 25, 26, 0, 28, 29, 30, 31,
        ];

        Vector3f[] rest = new Vector3f[parents.Length];
        rest[0] = new Vector3f(0f, 0f, 0.95f * Metre);
        rest[1] = Up(0.15f);
        rest[2] = Up(0.20f);
        rest[3] = Up(0.20f);
        rest[4] = Up(0.15f);
        rest[5] = Up(0.20f);
        rest[6] = Up(0.05f);
        rest[7] = Up(0.05f);
        rest[8] = Side(-0.20f);
        rest[9] = new Vector3f(-0.05f * Metre, 0f, -0.28f * Metre);
        rest[10] = Up(-0.26f);
        rest[11] = Up(-0.10f);
        rest[12] = Up(-0.04f);
        rest[13] = Up(-0.03f);
        rest[14] = Side(0.20f);
        rest[15] = new Vector3f(0.05f * Metre, 0f, -0.28f * Metre);
        rest[16] = Up(-0.26f);
        rest[17] = Up(-0.10f);
        rest[18] = Up(-0.04f);
        rest[19] = Up(-0.03f);
        rest[20] = new Vector3f(0f, 0.18f * Metre, -0.05f * Metre);
        rest[21] = new Vector3f(0f, 0.50f * Metre, 0.10f * Metre);
        rest[22] = new Vector3f(0f, 0.40f * Metre, 0.30f * Metre);
        rest[23] = Side(-0.10f);
        rest[24] = Up(-0.05f);
        rest[25] = Up(-0.45f);
        rest[26] = Up(-0.42f);
        rest[27] = new Vector3f(0f, 0.10f * Metre, -0.03f * Metre);
        rest[28] = Side(0.10f);
        rest[29] = Up(-0.05f);
        rest[30] = Up(-0.45f);
        rest[31] = Up(-0.42f);
        rest[32] = new Vector3f(0f, 0.10f * Metre, -0.03f * Metre);

        return BoneSkeleton.Create(parents, rest);
    }

    private static Vector3f Up(float metres) => new(0f, 0f, metres * Metre);

    private static Vector3f Side(float metres) => new(metres * Metre, 0f, 0f);
}

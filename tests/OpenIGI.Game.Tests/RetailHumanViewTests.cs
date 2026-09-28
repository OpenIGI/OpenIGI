using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Drives the first-person rig off retail data and checks that the held weapon's transform comes out of
/// it — the acceptance for the bone-attachment runtime.
/// </summary>
/// <remarks>
/// The chain under test is the one the original walks every frame: <c>005.iff</c> gives skeleton 5, the
/// weapon's own record gives an animation id, the clip poses the rig, and bone 3 says where the model
/// goes. Nothing here supplies a position or an offset of its own, which is the point.
/// </remarks>
public sealed class RetailHumanViewTests
{
    /// <summary>The knife's <c>Gun Model</c>, as its roster entry names it.</summary>
    private const string KnifeModel = "133_01_1";

    [RequiresGameDataFact]
    public void TheViewRigIsSkeletonFiveWithFiftySixBones()
    {
        BoneAnimationSet set = ViewSet();

        Assert.Equal(HumanView.SkeletonId, set.Skeleton.Id);
        Assert.Equal(56, set.Skeleton.BoneCount);

        // The four attachment bones all exist in it, and the one the body uses does too -- the offsets
        // would be meaningless against a rig that did not have them.
        Assert.InRange(HumanAttachPoints.FirstPersonWeapon, 0, set.Skeleton.BoneCount - 1);
        Assert.InRange(HumanAttachPoints.FirstPersonSecondary, 0, set.Skeleton.BoneCount - 1);
        Assert.InRange(HumanAttachPoints.FirstPersonTertiary, 0, set.Skeleton.BoneCount - 1);
    }

    [RequiresGameDataFact]
    public void TheBodyRigDoesNotHaveTheViewRigsAttachmentBones()
    {
        // 000.iff is the player's own skeleton, and it stops at 33 bones -- so bones 52 and 55 could
        // only ever have been read out of the view's palette.
        BoneAnimationSet body = Load("000");

        Assert.Equal(33, body.Skeleton.BoneCount);
        Assert.True(HumanAttachPoints.FirstPersonSecondary >= body.Skeleton.BoneCount);
        Assert.True(HumanAttachPoints.FirstPersonTertiary >= body.Skeleton.BoneCount);
        Assert.InRange(HumanAttachPoints.ThirdPersonWeapon, 0, body.Skeleton.BoneCount - 1);
    }

    [RequiresGameDataFact]
    public void TheKnifesStandAnimationResolvesInTheViewRigsSet()
    {
        WeaponType knife = Knife();
        BoneAnimationSet view = ViewSet();

        Assert.Equal(KnifeModel, knife.GunModel);

        // The weapon record's full-body ids are VIEW-rig ids: 0x489F20 plays them on bone 0 of
        // human->view->boneObj, which 0x464660 built from skeleton 5. Its UpperBody* ids go elsewhere.
        Assert.True(
            view.TryGet(knife.StandAnimation, out _),
            $"The knife's stand animation {knife.StandAnimation} is not in 005.iff.");
    }

    [RequiresGameDataFact]
    public void TheRigHasNoPoseToDrawUntilAWeaponClipIsPlayed()
    {
        // A fresh rig is what the player spawns with: empty-handed, lowering for the first weapon's
        // change, with nothing played on bone 0. Retail cannot evaluate that rig at all, so it must not
        // be drawn -- and one weapon clip is all it takes to give it a pose.
        HumanView view = new(ViewSet());

        Assert.False(view.IsPosed);

        view.Bones.Tick();
        Assert.False(view.IsPosed);

        view.PlayWeaponAnimation(Knife().StandAnimation, loops: true);
        Assert.True(view.IsPosed);

        view.Bones.Tick();
        Assert.True(view.IsPosed);
    }

    [RequiresGameDataFact]
    public void TheViewExposesTheCurrentWholeRigClipProgress()
    {
        HumanView view = new(ViewSet());
        view.PlayWeaponAnimation(Knife().StandAnimation, loops: true);

        Assert.Equal(0f, view.ClipProgress);

        view.Bones.Tick();

        Assert.InRange(view.ClipProgress, 0.0f, 1.0f);
        Assert.True(view.ClipProgress > 0f);
    }

    [RequiresGameDataFact]
    public void TheMedipackLoopActionUsesTheRawRetailSecondFireSlot()
    {
        WeaponType medipack = RetailWeaponRoster.Value.Weapons.First(
            weapon => weapon.Category == WeaponCategory.Medipack);
        WeaponAnimations action = WeaponAnimationTable.Select(medipack, WeaponAction.MedipackLoop);

        // The retail medipack record in IGI.EXE has no second full-body fire clip. The native
        // callback forwards -1 to the animation dispatcher; it does not replay FireAnimation1.
        Assert.Equal(medipack.FireAnimation2, action.FullBody);
        if (action.FullBody < 0)
        {
            return;
        }

        BoneAnimationSet viewSet = ViewSet();
        Assert.True(viewSet.TryGet(action.FullBody, out BoneAnimation injection));
        Assert.True(
            injection.Rotations(HumanAttachPoints.FirstPersonWeapon).Length > 1,
            "The medipack fire clip does not animate the first-person arm attachment.");

        HumanView view = new(viewSet);
        Assert.True(action.LoopFullBody);
        Assert.NotNull(view.PlayWeaponAnimation(action.FullBody, loops: action.LoopFullBody));
        Assert.Equal(0f, view.ClipProgress);

        view.Bones.Tick();

        Assert.InRange(view.ClipProgress, 0f, 1f);
        Assert.True(view.ClipProgress > 0f);
    }

    [RequiresGameDataFact]
    public void M16A2ReloadUsesTheRetailViewClipAndImmediateOneShotDispatch()
    {
        WeaponType m16 = RetailWeaponRoster.Value.Weapons.First(
            weapon => weapon.ScriptId == "WEAPON_ID_M16A2");
        WeaponAnimations reload = WeaponAnimationTable.Select(m16, WeaponAction.Reload);

        // 0x4137E0 case 6 reads +684/+6A8 and gives both tracks zero blend. The shipped M16A2 record
        // is full-body view clip 5 and player upper-body clip 136; substituting a generic rifle
        // animation is what makes the reload visibly wrong even though ammo still transfers.
        Assert.Equal(5, m16.ReloadAnimation);
        Assert.Equal(136, m16.UpperBodyReloadAnimation);
        Assert.Equal(5, reload.FullBody);
        Assert.Equal(136, reload.UpperBody);
        Assert.False(reload.LoopFullBody);
        Assert.False(reload.LoopUpperBody);
        Assert.Equal(0f, reload.FullBodyBlendSeconds);
        Assert.Equal(0f, reload.UpperBodyBlendSeconds);

        BoneAnimationSet view = ViewSet();
        Assert.True(view.TryGet(reload.FullBody, out _));
        HumanView humanView = new(view);
        Assert.NotNull(humanView.PlayWeaponAnimation(reload.FullBody, loops: reload.LoopFullBody));
    }

    [RequiresGameDataFact]
    public void EveryFullBodyWeaponAnimationOfEveryWeaponResolvesInTheViewRigsSet()
    {
        BoneAnimationSet view = ViewSet();
        int checkedIds = 0;

        foreach (WeaponType weapon in RetailWeaponRoster.Value.Weapons)
        {
            foreach (int id in FullBodyIds(weapon))
            {
                if (id <= 0)
                {
                    // Nothing to play. The dispatch's own -1 guard covers it.
                    continue;
                }

                Assert.True(
                    view.TryGet(id, out _),
                    $"{weapon.ScriptId}: full-body animation {id} is not in 005.iff.");
                checkedIds++;
            }
        }

        // A count, so a roster that silently stopped loading would fail rather than pass vacuously.
        Assert.True(checkedIds > 50, $"Only {checkedIds} full-body ids were checked.");
    }

    [RequiresGameDataFact]
    public void TheKnifeIsHeldOutInFrontOfTheView()
    {
        HumanView view = new(ViewSet());
        Assert.NotNull(view.PlayWeaponAnimation(Knife().StandAnimation, loops: true));

        Vector3d at = view.HeldWeapon.Position;

        // This is the whole acceptance in one assertion, so it is pinned to the measured value rather
        // than to a range. Two terms make it up, and both come out of 005.iff:
        //
        //   the clip's root motion   (233.26, -837.40, -751.50)   the knife's own hold offset
        //   bone 3 of the posed rig  (155.65, 2425.62,  205.52)   where the hand is
        //   -------------------------------------------------
        //   from the eye             (388.92, 1588.23, -545.99)
        //
        // The game's axes are +Y forward and +Z up, and one metre is 4096 engine units, so that is
        // 9.5 cm right, 39 cm forward and 13 cm DOWN of the eye -- low and to the right, which is where
        // the original holds it. Getting only the second term gives a knife in the middle of the screen.
        // Pinned to the whole engine unit, which is a quarter of a millimetre.
        Assert.Equal(388.92d, at.X, 0);
        Assert.Equal(1588.23d, at.Y, 0);
        Assert.Equal(-545.99d, at.Z, 0);

        Assert.True(at.Z < 0d, $"The knife is {at.Z / 4096d:0.00} m above the eye; it should hang below.");
        Assert.True(at.X > 0d, $"The knife is {at.X / 4096d:0.00} m right of the eye; it is a right hand.");
    }

    [RequiresGameDataFact]
    public void EveryWeaponsStandingClipCarriesItsOwnHoldOffset()
    {
        // The root-motion track of a first-person clip is not locomotion -- it is where that weapon
        // sits relative to the eye, and it is the reason a knife and a rifle do not occupy the same
        // patch of screen. Measured across the roster it is distinct per weapon and almost always
        // below the eye.
        BoneAnimationSet view = ViewSet();
        HashSet<string> distinct = new(StringComparer.Ordinal);
        int belowTheEye = 0;
        int total = 0;

        foreach (WeaponType weapon in RetailWeaponRoster.Value.Weapons)
        {
            if (weapon.StandAnimation <= 0 || !view.TryGet(weapon.StandAnimation, out BoneAnimation clip))
            {
                continue;
            }

            Assert.True(clip.Translation.Length > 0);
            Vector3f hold = clip.Translation[0].Position;
            distinct.Add($"{hold.X:0.###},{hold.Y:0.###},{hold.Z:0.###}");
            total++;
            if (hold.Z < 0f)
            {
                belowTheEye++;
            }
        }

        Assert.Equal(19, total);

        // The Glock and the Desert Eagle share a clip, and so do the grenade and the flashbang, which
        // is why this is 17 and not 19.
        Assert.Equal(17, distinct.Count);

        // The two held above it are the medipack and the proximity mine -- things you look at rather
        // than aim, which is a good sign the track really is a hold offset.
        Assert.Equal(17, belowTheEye);
    }

    [RequiresGameDataFact]
    public void TheKnifesHoldOffsetIsWhatPutsItBelowTheEye()
    {
        HumanView view = new(ViewSet());
        view.PlayWeaponAnimation(Knife().StandAnimation, loops: true);

        Vector3f hold = view.RootMotion;
        Assert.Equal(233.26f, hold.X, 1);
        Assert.Equal(-837.40f, hold.Y, 1);
        Assert.Equal(-751.50f, hold.Z, 1);

        // With the object's rotation the identity, the offset goes through untouched -- so the object
        // the rig hangs from sits 20 cm behind and 18 cm below the eye, and only then does the bone
        // reach forward.
        Assert.Equal(hold.X, view.Position.X, 3);
        Assert.Equal(hold.Y, view.Position.Y, 3);
        Assert.Equal(hold.Z, view.Position.Z, 3);
    }

    [RequiresGameDataFact]
    public void TheWallProbePullsBackTheRigWithoutMovingTheEye()
    {
        HumanView view = new(ViewSet())
        {
            Eye = new Vector3d(1000d, 2000d, 3000d),
            EyeOrientation = Matrix3f.Identity,
        };

        WorldRayCast wall = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            Assert.Equal(view.Eye, origin);
            Assert.Equal(view.Eye.Y + HumanViewWallPullback.ProbeDistanceInUnits, end.Y, 3);
            hit = new WorldRayHit(
                new Vector3d(origin.X, origin.Y + 1024d, origin.Z),
                new Vector3f(0f, -1f, 0f),
                1024d);
            return true;
        };

        view.TickWallPullback(wall);

        Assert.Equal(-HumanViewWallPullback.PullInStepInUnits, view.WallPullbackInUnits, 5);
        Assert.Equal(2000d - HumanViewWallPullback.PullInStepInUnits, view.Position.Y, 5);
        Assert.Equal(new Vector3d(1000d, 2000d, 3000d), view.Eye);
    }

    [RequiresGameDataFact]
    public void TheKnifesIdleIsASinglePoseAndItsAttackIsNot()
    {
        WeaponType knife = Knife();
        BoneAnimationSet view = ViewSet();

        Assert.True(view.TryGet(knife.StandAnimation, out BoneAnimation idle));
        Assert.True(view.TryGet(knife.FireAnimation1, out BoneAnimation attack));

        // Worth stating because it looks like a broken runtime otherwise: the knife's idle carries one
        // rotation key per bone, so the held pose really is motionless. Its attack carries 22.
        for (int bone = 0; bone < view.Skeleton.BoneCount; bone++)
        {
            Assert.Equal(1, idle.Rotations(bone).Length);
        }

        Assert.Equal(22, attack.Rotations(HumanAttachPoints.FirstPersonWeapon).Length);
    }

    [RequiresGameDataFact]
    public void AdvancingAMultiKeyClipMovesTheWeaponBone()
    {
        HumanView view = new(ViewSet())
        {
            Eye = new Vector3d(1000d, 2000d, 3000d),
        };

        Assert.NotNull(view.PlayWeaponAnimation(Knife().FireAnimation1, loops: false));
        BoneAttachment first = view.HeldWeapon;

        // The bone sits away from the object's origin -- it is a hand on a rig, not the origin with a
        // rotation -- but within arm's reach of it. One metre is 4096 engine units.
        double reach = Distance(first.Position, view.Position);
        Assert.True(reach > 1d, "The weapon bone coincides with the object's origin.");
        Assert.True(reach < 4d * 4096d, $"The weapon bone is {reach / 4096d:0.00} m from the object.");

        // Advancing the clip moves it, which is what makes this the animation's transform rather than a
        // constant read once.
        for (int tick = 0; tick < 8; tick++)
        {
            view.Tick();
        }

        Assert.True(
            Distance(first.Position, view.HeldWeapon.Position) > 1d,
            "Eight ticks of the knife's attack moved the weapon bone not at all.");
    }

    [RequiresGameDataFact]
    public void TheEyeIsAddedToTheBoneRatherThanReplacingIt()
    {
        BoneAnimationSet set = ViewSet();
        HumanView atOrigin = new(set);
        HumanView moved = new(set) { Eye = new Vector3d(5000d, -7000d, 900d) };

        atOrigin.PlayWeaponAnimation(Knife().StandAnimation, loops: true);
        moved.PlayWeaponAnimation(Knife().StandAnimation, loops: true);

        BoneAttachment a = atOrigin.HeldWeapon;
        BoneAttachment b = moved.HeldWeapon;

        Assert.Equal(a.Position.X + 5000d, b.Position.X, 3);
        Assert.Equal(a.Position.Y - 7000d, b.Position.Y, 3);
        Assert.Equal(a.Position.Z + 900d, b.Position.Z, 3);

        // The rotation is copied whole and owes nothing to where the object stands.
        Assert.Equal(a.Rotation, b.Rotation);
    }

    [RequiresGameDataFact]
    public void TheObjectsOrientationTurnsTheAttachmentWithIt()
    {
        BoneAnimationSet set = ViewSet();
        HumanView facing = new(set);
        facing.PlayWeaponAnimation(Knife().StandAnimation, loops: true);
        BoneAttachment straight = facing.HeldWeapon;

        // A quarter turn about Z, applied above the root exactly as 0x4D38C0 applies the object's 3x3.
        facing.Orientation = new Matrix3f(0f, -1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f);
        BoneAttachment turned = facing.HeldWeapon;

        Assert.Equal(-straight.Position.Y, turned.Position.X, 2);
        Assert.Equal(straight.Position.X, turned.Position.Y, 2);
        Assert.Equal(straight.Position.Z, turned.Position.Z, 2);
    }

    [RequiresGameDataFact]
    public void TheWeaponStaysWeldedToTheEyeAtEveryYaw()
    {
        // The invariant a first-person weapon has to satisfy: expressed in the camera's own frame, the
        // attachment must not depend on which way the player is facing. Measured over five yaws it is
        // constant to the unit -- 9.5 cm right, 13 cm DOWN, 39 cm forward -- which is what proves the
        // rig's orientation is bound to the camera's the right way round rather than merely looking
        // plausible from one spawn. Both terms of the position turn together, the hold offset with the
        // bone, because 0x464BD0 applies the same 3x3 to each.
        BoneAnimationSet set = ViewSet();

        foreach (float yaw in new[] { 0f, 0.7f, MathF.PI / 2f, 3f, -2.2f })
        {
            HumanView view = new(set);
            view.PlayWeaponAnimation(Knife().StandAnimation, loops: true);
            view.Orientation = Matrix3f.FromEngineEulerAngles(0f, 0f, -yaw);

            BoneAttachment held = view.HeldWeapon;
            Vector3f offset = ToView(
                yaw,
                new Vector3f((float)held.Position.X, (float)held.Position.Y, (float)held.Position.Z));

            Assert.Equal(388.92f, offset.X, 0);
            Assert.Equal(-545.99f, offset.Y, 0);
            Assert.Equal(1588.23f, offset.Z, 0);

            // The blade runs along the model's own +Y -- 133_01_1 measures 39 cm along it against 3 cm
            // across -- so the middle column of the bone's 3x3 is where the blade points.
            Vector3f blade = ToView(yaw, new Vector3f(held.Rotation.M12, held.Rotation.M22, held.Rotation.M32));
            Assert.Equal(0.93f, blade.Z, 2);
            Assert.True(blade.Z > 0.9f, $"At yaw {yaw} the blade points {blade.Z:0.00} forward.");
        }
    }

    /// <summary>
    /// Maps a world vector into the camera's frame, reproducing <c>Camera3D.LookRotation</c>'s rows —
    /// right, up, forward.
    /// </summary>
    /// <remarks>
    /// The yaw is negated against the camera's on purpose. <c>0x4B38E0</c> builds the engine's yaw as a
    /// plain <c>Z(gamma)</c>, which takes <c>+Y</c> to <c>(-sin, cos, 0)</c>, while this codebase's
    /// camera puts its forward at <c>(sin, cos, 0)</c>. The two run in opposite senses, and this is
    /// where that shows.
    /// </remarks>
    private static Vector3f ToView(float yaw, Vector3f world)
    {
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        Vector3f right = new(cy, -sy, 0f);
        Vector3f up = new(0f, 0f, 1f);
        Vector3f forward = new(sy, cy, 0f);

        return new Vector3f(Dot(right, world), Dot(up, world), Dot(forward, world));
    }

    private static float Dot(Vector3f a, Vector3f b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    [RequiresGameDataFact]
    public void HandingTheViewTheBodysSkeletonIsRefused()
    {
        // The whole attachment reading rests on bone 3 of the 56-bone rig, so the wrong set has to fail
        // loudly rather than produce an attachment at a bone that means something else.
        Assert.Throws<ArgumentException>(() => new HumanView(Load("000")));
    }

    private static IEnumerable<int> FullBodyIds(WeaponType weapon)
    {
        yield return weapon.StandAnimation;
        yield return weapon.MoveAnimation;
        yield return weapon.FireAnimation1;
        yield return weapon.FireAnimation2;
        yield return weapon.FireAnimation3;
        yield return weapon.ReloadAnimation;
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static WeaponType Knife()
    {
        Assert.True(
            RetailWeaponRoster.Value.TryGetByGunModel(KnifeModel, out WeaponType knife),
            $"No roster entry names the gun model '{KnifeModel}'.");
        return knife;
    }

    private static BoneAnimationSet ViewSet() => Load("005");

    private static BoneAnimationSet Load(string name) =>
        BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", $"{name}.iff")));
}

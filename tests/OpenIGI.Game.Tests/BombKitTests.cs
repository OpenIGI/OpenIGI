using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Sound;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>The bomb built into the game reads with the game's own readers, every part of it.</summary>
public sealed class BombKitTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_model_reads_and_every_slot_it_draws_with_has_its_texture_built_in()
    {
        Assert.Contains(BombKit.HeldModel, BombKit.Models.Keys);
        Assert.Contains(BombKit.PlantedModel, BombKit.Models.Keys);
        foreach (string name in BombKit.Models.Keys)
        {
            byte[] bytes = BombKit.Model(name) ?? throw new Xunit.Sdk.XunitException($"{name}.mef is not built in");
            MefModel model = MefModel.Read(bytes, AssetFormatProfile.Igi2);
            Assert.NotEmpty(model.DrawRecords);
            foreach (MefDrawRecord record in model.DrawRecords)
            {
                if (record.MaterialSlot == MefDrawRecord.NoMaterialSlot)
                {
                    continue;
                }

                string texture = BombKit.TextureOf(name, record.MaterialSlot) ?? throw new Xunit.Sdk.XunitException($"{name} slot {record.MaterialSlot} has no texture");
                Assert.True(ImageSniffer.TryDecode(BombKit.Texture(texture), out TextureImage? image), $"{texture}.tex does not decode");
                Assert.NotNull(image);
            }

            output.WriteLine($"{name}: {model.DrawRecords.Count} draw records, {model.Triangles().Count()} triangles");
        }
    }

    [Theory]
    [InlineData(BombKit.PlantClip)]
    [InlineData(BombKit.DefuseClip)]
    [InlineData("fire_c4bomb")]
    [InlineData("tense_c4bomb")]
    [InlineData("weaponraise_c4bomb")]
    [InlineData("fire_c4_1st")]
    [InlineData("walk_c4_1st")]
    [InlineData("run_c4_1st")]
    [InlineData("weaponraise_c4_1st")]
    public void Every_clip_reads(string name)
    {
        Igi2AnimationClip clip = Igi2AnimationClip.Read(BombKit.Clip(name) ?? throw new Xunit.Sdk.XunitException($"{name}.iff is not built in"));
        Assert.True(clip.TotalTime > 0);
        output.WriteLine($"{name}: {clip.BoneCount} bones, {clip.TotalTime} time, loops {clip.Loops}");
    }

    [Theory]
    [InlineData(BombKit.PlantSound)]
    [InlineData("tba_placec4_2")]
    [InlineData(BombKit.TickSound)]
    [InlineData(BombKit.LastTickSound)]
    [InlineData(BombKit.DefuseSound)]
    [InlineData(BombKit.ExplosionSound)]
    [InlineData(BombKit.DistantExplosionSound)]
    public void Every_sound_reads(string name)
    {
        IlsfFile sound = IlsfFile.Read(BombKit.Sound(name) ?? throw new Xunit.Sdk.XunitException($"{name}.wav is not built in"));
        output.WriteLine($"{name}: {sound.Duration.TotalSeconds:0.00} s, {sound.SampleRate} Hz, {sound.Channels} ch, streamed {sound.IsStreamed}");
        Assert.NotNull(sound);
    }

    [Fact]
    public void The_icon_is_built_in() => Assert.NotNull(BombKit.Read(BombKit.Icon + ".spr"));

    // The plant and the defuse, played on the first game's human rig: every bone the sequel's clip moves lands where the
    // sequel's own rig puts it. Exactly -- under a centimetre -- for a bone every sequel ancestor of which the first game has
    // too; within six for the neck and the head, which the sequel hangs from a joint of its own between the chest and the neck
    // that turns them, and which the first game, with no joint there, can only hang from the chest.
    [RequiresGameDataTheory]
    [InlineData(BombKit.PlantClip)]
    [InlineData(BombKit.DefuseClip)]
    public void A_clip_retargeted_onto_the_first_games_rig_poses_every_joint_where_the_sequels_rig_does(string name)
    {
        BoneSkeleton igi1 = BoneAnimationSet.Read(File.ReadAllBytes(GameData.Resolve("common/anims/000.iff"))).Skeleton;
        Igi2AnimationClip clip = Igi2AnimationClip.Read(BombKit.Clip(name)!);
        int[] match = Igi2ClipRetarget.Match(clip, igi1);
        Assert.Equal(28, match.Count(b => b >= 0));

        // Whether every bone above a sequel bone is one the first game has.
        bool Whole(int bone)
        {
            for (int up = clip.Hierarchy.Parents[bone]; up >= 0; up = clip.Hierarchy.Parents[up])
            {
                if (Array.IndexOf(match, up) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        BoneAnimation animation = Igi2ClipRetarget.ToBoneAnimation(clip, igi1, 900);
        BoneSkeleton igi2 = clip.Hierarchy.CreateSkeleton();
        Quaternion32[] sequel = new Quaternion32[clip.BoneCount];
        Quaternion32[] first = new Quaternion32[igi1.BoneCount];
        double exact = 0;
        double hung = 0;
        for (int step = 0; step * BoneAnimation.FrameStep < clip.TotalTime; step++)
        {
            float time = step * BoneAnimation.FrameStep;
            Vector3f root = Igi2ClipSampler.Sample(clip, (int)time, squad: true, sequel);
            BonePose expected = BonePose.Evaluate(igi2, sequel, root);
            for (int bone = 0; bone < igi1.BoneCount; bone++)
            {
                first[bone] = BoneRotationSampler.Sample(animation.Rotations(bone), time, animation.Duration, animation.IsCyclic);
            }

            BonePose actual = BonePose.Evaluate(igi1, first, root);
            for (int bone = 0; bone < igi1.BoneCount; bone++)
            {
                if (match[bone] >= 0)
                {
                    Vector3f d = actual.Positions[bone] - expected.Positions[match[bone]];
                    double off = Math.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z)) / 4096.0;
                    if (Whole(match[bone]))
                    {
                        exact = Math.Max(exact, off);
                    }
                    else
                    {
                        hung = Math.Max(hung, off);
                    }
                }
            }
        }

        output.WriteLine($"{name}: worst whole-chain joint {exact * 1000:0.0} mm, worst neck/head {hung * 1000:0.0} mm");
        Assert.True(exact < 0.01, $"a joint is {exact * 1000:0} mm from where the sequel puts it");
        Assert.True(hung < 0.06, $"the neck or head is {hung * 1000:0} mm from where the sequel puts it");
    }
    [RequiresGameDataFact]
    public void The_first_person_plant_lands_on_the_arms_and_holds_the_charge_in_the_hand_until_it_is_set_down()
    {
        OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.Register();
        BoneAnimationSet arms = OpenIGI.Game.World.AnimationSetExtensions.Apply(
            AssetFormatProfile.Igi1, [BoneAnimationSet.Read(File.ReadAllBytes(GameData.Resolve("common/anims/005.iff")))])[0];
        Assert.True(arms.TryGet(OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonPlant, out BoneAnimation plant));

        BonePose PoseAt(float time)
        {
            Quaternion32[] rotations = new Quaternion32[arms.Skeleton.BoneCount];
            for (int bone = 0; bone < rotations.Length; bone++)
            {
                rotations[bone] = BoneRotationSampler.Sample(plant.Rotations(bone), time, plant.Duration, plant.IsCyclic);
            }

            return BonePose.Evaluate(arms.Skeleton, rotations, Vector3f.Zero);
        }

        // Mid-plant the charge is in the hand, where the sequel's own arms hold it.
        float mid = OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonTime(
            OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.Duration(OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.Plant) / 2);
        Igi2AnimationClip clip = Igi2AnimationClip.Read(BombKit.Clip(BombKit.FirstPersonPlantClip)!);
        Quaternion32[] sequel = new Quaternion32[clip.BoneCount];
        _ = Igi2ClipSampler.Sample(clip, (int)mid, squad: true, sequel);
        BonePose own = BonePose.Evaluate(clip.Hierarchy.CreateSkeleton(), sequel, Vector3f.Zero);
        Igi2AttachedPart expected = Assert.Single(Igi2AttachedParts.Of(clip, (int)mid, own, BombKit.HeldModel), p => p.Model == BombKit.HeldModel);
        Igi2AttachedPart charge = Assert.Single(OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.InHand(PoseAt(mid), OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonPlant, mid), p => p.Model == BombKit.HeldModel);
        Vector3f off = expected.Position - charge.Position;
        double metres = Math.Sqrt((off.X * off.X) + (off.Y * off.Y) + (off.Z * off.Z)) / 4096.0;
        output.WriteLine($"charge {metres * 1000:0.0} mm from where the sequel holds it mid-plant");
        Assert.True(metres < 0.01, $"the charge is {metres * 1000:0} mm from where the sequel holds it");

        // At the plant's end it has been set down.
        float end = OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonTime(float.MaxValue);
        Assert.DoesNotContain(OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.InHand(PoseAt(end), OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonPlant, end), p => p.Model == BombKit.HeldModel);
    }
    [RequiresGameDataTheory]
    [InlineData("common/anims/005.iff", OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonHold)]
    [InlineData("common/anims/005.iff", OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonWalk)]
    [InlineData("common/anims/000.iff", OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.Hold)]
    public void The_bomb_held_as_a_weapon_has_its_clips_on_the_rig_it_is_held_on_and_the_charge_in_its_hand(string set, int animation)
    {
        OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.Register();
        // Read as the host reads the player's own body and arms from the install, not through a pack.
        BoneAnimationSet rig = OpenIGI.Game.World.AnimationSetExtensions.Read(AssetFormatProfile.Igi1, File.ReadAllBytes(GameData.Resolve(set)));
        Assert.True(rig.TryGet(animation, out BoneAnimation clip));
        Quaternion32[] rotations = new Quaternion32[rig.Skeleton.BoneCount];
        for (int bone = 0; bone < rotations.Length; bone++)
        {
            rotations[bone] = BoneRotationSampler.Sample(clip.Rotations(bone), 0f, clip.Duration, clip.IsCyclic);
        }

        BonePose pose = BonePose.Evaluate(rig.Skeleton, rotations, Vector3f.Zero);
        Assert.Contains(OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.InHand(pose, animation, 0f), p => p.Model == BombKit.HeldModel);
    }

    [Fact]
    public void The_weapon_wheels_picture_of_the_bomb_decodes()
    {
        OpenIGI.Formats.Sprites.SpriteAsset icon = OpenIGI.Formats.Sprites.SpriteLoader.Load(BombKit.Read(BombKit.Icon + ".spr")!, BombKit.Icon + ".spr");
        Assert.NotEmpty(icon.Frames);
        output.WriteLine($"c4 icon {icon.Frames[0].Width}x{icon.Frames[0].Height}, {icon.Frames.Count} frame(s)");
        Assert.True(icon.Frames[0].Width > 0 && icon.Frames[0].Height > 0);
    }
    // Played between its keys -- where a plant's time almost always falls -- a retargeted clip keeps the arms where its keys
    // have them: the root's tangents are slopes, not positions, so nothing is thrown metres off between two keys.
    [RequiresGameDataTheory]
    [InlineData(OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonPlant)]
    [InlineData(OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonHold)]
    [InlineData(OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.FirstPersonWalk)]
    public void Between_its_keys_a_first_person_bomb_clip_keeps_the_arms_where_its_keys_hold_them(int animation)
    {
        OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.Register();
        BoneAnimationSet arms = OpenIGI.Game.World.AnimationSetExtensions.Read(AssetFormatProfile.Igi1, File.ReadAllBytes(GameData.Resolve("common/anims/005.iff")));
        Assert.True(arms.TryGet(animation, out BoneAnimation clip));
        double worst = 0;
        for (float time = 0f; time < clip.Duration; time += BoneAnimation.FrameStep * 0.37f)
        {
            OpenIGI.Game.Player.HumanView view = new(arms);
            Assert.NotNull(view.PlayWeaponAnimation(animation, clip.IsCyclic));
            view.SkipTo(time);
            Vector3f root = view.RootMotion;
            worst = Math.Max(worst, Math.Sqrt((root.X * root.X) + (root.Y * root.Y) + (root.Z * root.Z)) / 4096.0);
        }

        output.WriteLine($"{animation}: the root strays at most {worst:0.000} m from the eye");
        Assert.True(worst < 0.5, $"the root is thrown {worst:0.00} m between keys");
    }
}

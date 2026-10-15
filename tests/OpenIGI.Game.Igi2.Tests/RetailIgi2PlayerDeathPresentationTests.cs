using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The player's selected retail death clip is visible through the death camera until its final pose.</summary>
public sealed class RetailIgi2PlayerDeathPresentationTests
{
    /// <summary>A fatal hit exposes the animated body, hides the arms, and leaves a completed death pose.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Player_death_draws_the_selected_clip_through_a_body_camera()
    {
        RetailIgi2Mission1 mission = RetailIgi2Mission1.Load(collision: true);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(mission.Level);
        Igi2Session session = Igi2Session.Start();
        Igi2PlayerTuning tuning = session.Require<Igi2PlayerTuning>();
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, mission.Level.Root, tuning, clips)!;
        Assert.True(PlayerSpawn.TryFindGameplay(mission.Level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel model = mission.Level.ModelFor(point.Model)!;
        Igi2HumanPlayerTask task = (Igi2HumanPlayerTask)PlayerSpawn.FindGameplayTask(mission.Level.Root)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(mission.Collision!, tuning.General, point.Position, point.Gamma,
            Game.Ai.AiDifficulty.Normal, clips, model.BoneHierarchy, weapons, Igi2Gameplay.FirstPersonRig(mission.Level.ModelFor, task));
        Igi2HumanPlayer human = player.Human;
        human.Model = Igi2BodyModel.From(point.Model, model);
        human.ModelRadius = model.NativeMarkerBound;
        human.RootTranslation = model.BoneHierarchy!.Translations[0];
        human.View.ObjectModel = "arms1_1";
        for (int i = 0; i < 90; i++) { player.Tick(RetailIgi2Mission1.Idle); }
        Assert.NotNull(player.FirstPerson);

        human.HandleDamage(Igi2DamagePart.UpperBody, human.Damage.MaxDamage * 10f, new Vector3f(1f, 0f, 0f));
        for (int i = 0; i < 120 && human.CameraMode != 5; i++) { player.Tick(RetailIgi2Mission1.Idle); }
        Assert.True(human.IsDead);
        Assert.Equal(5, human.CameraMode);
        Assert.Null(player.FirstPerson);
        Assert.False(player.CanPreviewLook);
        Igi2HumanAnimation animation = Assert.IsType<Igi2HumanAnimation>(human.Animation);
        Assert.InRange(animation.LastState, 92, 99);
        string clip = animation.Main.Clip!.Name;
        int started = animation.MainTime;
        TitleWorldModel first = Assert.Single(player.WorldModels, m => ReferenceEquals(m.Instance, human));
        Vector3f[] firstPose = first.Pose!.Positions.ToArray();
        Assert.NotEqual(human.View.Position, player.EyePosition);
        Vector3d cameraToBody = human.BonePosition("center") - player.EyePosition;
        Assert.True(cameraToBody.Length > 4096.0, "The initial clearance search must place the camera outside the body.");
        Vector3f forward = player.ViewOrientation.Row3;
        Assert.InRange(((cameraToBody.X * forward.X) + (cameraToBody.Y * forward.Y) + (cameraToBody.Z * forward.Z)) / cameraToBody.Length, 0.999, 1.001);
        float heading = human.Gamma;
        bool poseChanged = false;
        for (int i = 0; i < 300; i++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { MouseX = 1f, MouseY = -0.25f, UnfilteredMouse = true });
            TitleWorldModel body = Assert.Single(player.WorldModels, m => ReferenceEquals(m.Instance, human));
            poseChanged |= !body.Pose!.Positions.SequenceEqual(firstPose);
            Assert.Equal(clip, animation.Main.Clip!.Name);
            Assert.Null(player.FirstPerson);
        }
        Assert.True(poseChanged);
        Assert.True(animation.MainTime > started);
        Assert.True(human.IsDeathAnimationDone);
        Assert.Equal(animation.MainTotalTime, animation.MainTime);
        Assert.Equal(heading, human.Gamma);
    }
}

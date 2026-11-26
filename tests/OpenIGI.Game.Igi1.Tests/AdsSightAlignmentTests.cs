using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Player.Aim;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Every aimable weapon of Project I.G.I.'s, at its own stand pose, brought onto the eye's line of sight.</summary>
public sealed class AdsSightAlignmentTests
{
    private static readonly Lazy<WeaponTypeRegistry> Roster = new(static () =>
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        OpenIGI.Game.Flow.Igi1LevelTaskTypes.Register(loop.Types);
        OpenIGI.Game.Igi1.Igi1TaskTypes.Register(loop.Types);
        WeaponTypeRegistry roster = new();
        roster.Register(loop.Natives);
        roster.Load(fileSystem, resources, loop.Natives);
        return roster;
    });

    [RequiresGameDataFact]
    public void EveryAimableWeaponIsInTheRosterAndItsSightsLandOnTheLineOfSight()
    {
        BoneAnimationSet set = BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", "005.iff")));
        float unitsPerMetre = (float)WorldScene.UnitsPerMetre;
        foreach (AdsProfile profile in AdsProfileTable.Igi1.Profiles.Where(static p => p.Kind == AdsKind.Sights))
        {
            WeaponType weapon = Assert.Single(Roster.Value.Weapons, w => w.ScriptId == profile.ScriptId);
            BoneObject bones = new(set);
            Assert.NotNull(bones.Play(0, weapon.StandAnimation, loops: true));
            BonePose pose = bones.Pose(Matrix3f.Identity);
            Vector3f hold = bones.SampleRootMotion();
            Vector3f bone = hold + pose.Positions[HumanAttachPoints.FirstPersonWeapon];
            Matrix3f rotation = pose.Rotations[HumanAttachPoints.FirstPersonWeapon];

            RigOffset move = SightAlignment.Reference(profile, set, weapon.StandAnimation);
            Vector3f rear = move.Apply(bone + RigOffset.Multiply(rotation, profile.RearSight * unitsPerMetre)) / unitsPerMetre;
            Vector3f front = move.Apply(bone + RigOffset.Multiply(rotation, profile.FrontSight * unitsPerMetre)) / unitsPerMetre;

            // On the axis to a tenth of a millimetre, the rear sight at the relief and the front sight beyond it.
            Assert.True(MathF.Abs(rear.X) < 1e-4f && MathF.Abs(rear.Z) < 1e-4f, $"{profile.ScriptId}'s rear sight sits at {rear}.");
            Assert.True(MathF.Abs(front.X) < 1e-4f && MathF.Abs(front.Z) < 1e-4f, $"{profile.ScriptId}'s front sight sits at {front}.");
            Assert.Equal(profile.EyeReliefMetres, rear.Y, 3);
            Assert.True(front.Y > rear.Y + 0.1f, $"{profile.ScriptId}'s front sight should stand well ahead of its rear.");

            // A sight laid on the axis must still be beyond the view model's near plane, or it would be clipped away.
            Assert.True(rear.Y > 0.05f, $"{profile.ScriptId}'s rear sight is inside the near plane.");
        }
    }
}

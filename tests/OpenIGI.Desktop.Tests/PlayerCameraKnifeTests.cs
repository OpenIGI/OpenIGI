using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A knife stab is <c>KnifeStab</c> (<c>0x46DFB0</c>), not a gun ray: it hands the struck object its hit
/// message and records no impact, so it leaves no mark, no impact sound and no dust.
/// </summary>
public sealed class PlayerCameraKnifeTests
{
    [RequiresGameDataFact]
    public void AStabHitsThroughTheHumanCallbackAndLeavesNoMark()
    {
        WeaponType knife = LoadRoster().Weapons.Single(weapon => weapon.Category == WeaponCategory.Knife);
        PlayerCamera camera = new();
        camera.UseWeaponCycle([knife.WeaponId]);
        camera.Carry(knife, new AmmoType { AmmoId = 1, CalibreId = knife.CalibreId });
        camera.UseViewRig(LoadViewSet());
        camera.UseWalls(ObjectCollisionIndex.Build([]));

        int callbackCount = 0;
        camera.Humans = (Vector3d origin, Vector3d end, float _, WeaponCategory category, out HumanHit hit) =>
        {
            callbackCount++;
            Assert.Equal(WeaponCategory.Knife, category);
            hit = new HumanHit(
                new Vector3d(origin.X, origin.Y + (PlayerCamera.UnitsPerMetre * 0.5), origin.Z),
                PlayerCamera.UnitsPerMetre * 0.5,
                Zone: 4,
                LocalForward: 0f,
                LocalSide: 0f);
            return true;
        };

        InputState input = new();
        for (int tick = 0; tick < 60
            && (camera.Weapons?.InHand is null || camera.Weapons.State != HumanWeaponState.Ready); tick++)
        {
            input.BeginFrame();
            camera.Tick(input, terrain: null);
        }

        bool stabbed = false;
        for (int tick = 0; tick < 60 && !stabbed; tick++)
        {
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: true);
            camera.Tick(input, terrain: null);
            stabbed = camera.Impacts.Count > 0;
        }

        Assert.True(stabbed, "the knife never struck");
        Assert.True(callbackCount > 0);
        GunImpact impact = Assert.Single(camera.Impacts);
        Assert.True(impact.Melee);
        Assert.True(impact.StruckHuman);
        Assert.Empty(camera.Marks.Marks);
    }

    private static BoneAnimationSet LoadViewSet() => BoneAnimationSet.Read(File.ReadAllBytes(
        Path.Combine(GameData.RequireRoot(), "common", "anims", "005.iff")));

    private static WeaponTypeRegistry LoadRoster()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        WeaponTypeRegistry roster = new();
        roster.Register(loop.Natives);
        roster.Load(fileSystem, resources, loop.Natives);
        return roster;
    }
}

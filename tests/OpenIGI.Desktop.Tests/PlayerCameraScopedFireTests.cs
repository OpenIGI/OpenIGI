using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers the player-facing seam from a held scope key and trigger to the world human-hit callback.
/// </summary>
public sealed class PlayerCameraScopedFireTests
{
    [RequiresGameDataFact]
    public void DragunovFiresThroughTheHumanCallbackWhileItsScopeIsOpeningWithDefaultHitscan()
    {
        WeaponType dragunov = LoadRoster().Weapons.Single(
            weapon => weapon.ScriptId == "WEAPON_ID_DRAGUNOV");
        PlayerCamera camera = new();
        camera.UseWeaponCycle([dragunov.WeaponId]);
        camera.Carry(dragunov, new AmmoType { AmmoId = 1, CalibreId = dragunov.CalibreId });
        camera.UseViewRig(LoadViewSet());
        camera.UseWalls(ObjectCollisionIndex.Build([]));

        int callbackCount = 0;
        Vector3d callbackOrigin = default;
        Vector3d callbackEnd = default;
        camera.Humans = (Vector3d origin, Vector3d end, float _, WeaponCategory _, double _, bool _, out HumanHit hit) =>
        {
            callbackCount++;
            callbackOrigin = origin;
            callbackEnd = end;
            hit = new HumanHit(
                new Vector3d(origin.X, origin.Y + PlayerCamera.UnitsPerMetre, origin.Z),
                PlayerCamera.UnitsPerMetre,
                Zone: 4,
                LocalForward: 0f,
                LocalSide: 0f);
            return true;
        };

        InputState input = new();
        TickUntilReady(camera, input);

        // The opening edge and the trigger arrive in the same camera tick, as they do when the
        // player scopes and shoots together. The player may fire immediately; neither the scope
        // transition nor its FOV adjustment is allowed to defer or discard this round.
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.Backspace, down: true);
        input.Mouse.SetButton(MouseButton.Left, down: true);
        camera.Tick(input, terrain: null);

        // The fire state's entry tick asks nothing of the gun (0x40EF60); the trigger still down the next tick fires.
        input.BeginFrame();
        camera.Tick(input, terrain: null);

        Assert.True(camera.IsSniperScoped);
        Assert.True(camera.FiredThisTick);
        Assert.Equal(1, callbackCount);
        Assert.Equal(camera.EyeUnits, callbackOrigin);
        Assert.True(callbackEnd.Y > callbackOrigin.Y);
        Assert.Equal(1, camera.HumanHits);
        Assert.Equal(4, camera.LastHumanZone);
        Assert.True(Assert.Single(camera.Impacts).HitSomething);
    }

    private static void TickUntilReady(PlayerCamera camera, InputState input)
    {
        for (int tick = 0; tick < 60
            && (camera.Weapons?.InHand is null || camera.Weapons.State != HumanWeaponState.Ready); tick++)
        {
            input.BeginFrame();
            camera.Tick(input, terrain: null);
        }

        Assert.NotNull(camera.Weapons?.InHand);
        Assert.Equal(HumanWeaponState.Ready, camera.Weapons!.State);
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

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The map key that closes the computer is still down on the tick after it closes, and must not open it again.
/// </summary>
/// <remarks>
/// The open computer holds the window's input, so while it is up the player's input frames are empty. The key that
/// closes it arrives with the mouse, and the first tick after it sees that same press, still held, against an empty frame
/// before it: a rising edge, which the ready state answers by opening the computer again.
/// </remarks>
public sealed class PlayerCameraMapKeyHeldTests
{
    [Fact]
    public void TheKeyThatClosesTheComputerDoesNotOpenItAgainWhileItIsHeld()
    {
        PlayerCamera camera = ReadyPlayer();
        InputState input = new();

        // The press that opens it.
        Tick(camera, input, mapKeyDown: true, computerOpen: false);
        Assert.Equal(HumanWeaponState.MapComputer, camera.Weapons!.State);
        camera.SetMapComputerOpen(true);

        // Let go, and use it for a while: its ticks see an empty, captured frame.
        for (int tick = 0; tick < 5; tick++)
        {
            Tick(camera, input, mapKeyDown: tick == 0, computerOpen: true);
        }

        // The press that closes it, read by the host, which then closes the computer.
        Tick(camera, input, mapKeyDown: true, computerOpen: true);
        camera.SetMapComputerOpen(false);

        // The key is held for a few more ticks, as a finger on a key is.
        for (int tick = 0; tick < 3; tick++)
        {
            Tick(camera, input, mapKeyDown: true, computerOpen: false);
            Assert.False(camera.IsMapComputerOpen);
            Assert.Equal(HumanWeaponState.Ready, camera.Weapons.State);
        }

        // Let go, and the next press opens it again as it should.
        Tick(camera, input, mapKeyDown: false, computerOpen: false);
        Tick(camera, input, mapKeyDown: true, computerOpen: false);
        Assert.Equal(HumanWeaponState.MapComputer, camera.Weapons.State);
    }

    [Fact]
    public void ALetGoKeyDoesNotHoldBackTheNextPress()
    {
        PlayerCamera camera = ReadyPlayer();
        InputState input = new();
        Tick(camera, input, mapKeyDown: true, computerOpen: false);
        camera.SetMapComputerOpen(true);
        Tick(camera, input, mapKeyDown: false, computerOpen: true);

        // The key was down and up again before the host's tick took it.
        camera.SetMapComputerOpen(false);
        Tick(camera, input, mapKeyDown: false, computerOpen: false);
        Tick(camera, input, mapKeyDown: true, computerOpen: false);

        Assert.Equal(HumanWeaponState.MapComputer, camera.Weapons!.State);
    }

    private static PlayerCamera ReadyPlayer()
    {
        WeaponType knife = new() { WeaponId = 10, CalibreId = -1 };
        PlayerCamera camera = new();
        camera.UseWeaponCycle([knife.WeaponId]);
        camera.Carry(knife);
        camera.UseWalls(ObjectCollisionIndex.Build([]));
        InputState input = new();
        for (int tick = 0; tick < 60; tick++)
        {
            input.BeginFrame();
            camera.Tick(input, new FlatGround());
        }

        Assert.Equal(HumanWeaponState.Ready, camera.Weapons!.State);
        return camera;
    }

    // One tick the way the host feeds it: an open computer has the window's input captured, so its keys read as up.
    private static void Tick(PlayerCamera camera, InputState input, bool mapKeyDown, bool computerOpen)
    {
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.C, mapKeyDown);
        input.Captured = computerOpen;
        if (computerOpen)
        {
            camera.TickMapComputer(input);
        }
        else
        {
            camera.Tick(input, new FlatGround());
        }
    }

    private sealed class FlatGround : IGroundHeightField
    {
        public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
        {
            ground = new TerrainGround(0.0, 0.0, 0.0, 1.0);
            return true;
        }

        public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
        {
            ground = default;
            return false;
        }
    }
}

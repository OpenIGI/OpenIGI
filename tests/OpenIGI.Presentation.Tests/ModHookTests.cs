using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>What a mod can do with the player and the view: hold the body, stand it, title a prompt, take the view.</summary>
public sealed class ModHookTests
{
    private static readonly Vector3d Start = new(1000.0, 2000.0, 300.0);

    /// <summary>A body whose control a mod has taken stays exactly where it stands, whatever the controls say.</summary>
    [Fact]
    public void ATakenBodyDoesNotMoveOrTurn()
    {
        PlayerCamera camera = Standing();
        camera.ControlTaken = true;
        PlayerInputFrame frame = Pushing();

        for (int i = 0; i < 30; i++)
        {
            camera.TickFrame(frame, null);
        }

        Assert.Equal(Start, camera.Body.Position);
        Assert.Equal(0.5f, camera.Body.Yaw);
    }

    /// <summary>The same controls move a body nobody has taken: the test above is not passing because nothing moves.</summary>
    [Fact]
    public void AFreeBodyMovesOnTheSameControls()
    {
        PlayerCamera camera = Standing();
        PlayerInputFrame frame = Pushing();

        for (int i = 0; i < 30; i++)
        {
            camera.TickFrame(frame, null);
        }

        Assert.NotEqual(Start, camera.Body.Position);
    }

    /// <summary>Standing the body puts its feet, facing and look exactly where they are told, with no ground search.</summary>
    [Fact]
    public void StandAtPutsTheBodyExactlyThere()
    {
        PlayerCamera camera = new();
        Vector3d feet = new(-5000.0, 7000.0, 12345.0);

        camera.StandAt(feet, 1.25f, -0.4f);

        Assert.Equal(feet, camera.Body.Position);
        Assert.Equal(1.25f, camera.Body.Yaw);
        Assert.Equal(-0.4f, camera.Body.Pitch);
    }

    /// <summary>A prompt with a title of its own says it; one without keeps the title its type gives it.</summary>
    [Fact]
    public void APromptSaysItsOwnTitleOnlyWhenItHasOne()
    {
        InteractionPromptTarget door = new(7, InteractionActivateType.Door, null, default, 0, false, false, null);

        Assert.Equal("Open Door", InteractionPromptPresentation.Title(door));
        Assert.Equal("Search", InteractionPromptPresentation.Title(door with { Title = "Search" }));
    }

    /// <summary>A taken view hides the weapon and the HUD unless the mod says otherwise.</summary>
    [Fact]
    public void ATakenViewHidesTheWeaponAndHudByDefault()
    {
        ViewTakeover taken = new(_ => default(Camera3D));

        Assert.True(taken.HidesViewModel);
        Assert.True(taken.HidesHud);
        Assert.False((taken with { HidesHud = false }).HidesHud);
    }

    /// <summary>A new mission offers nothing and runs nothing of a mod's: the hooks are empty until a mod fills them.</summary>
    [Fact]
    public void ANewMissionHasNoModHooks()
    {
        MissionWorld mission = new();

        Assert.Empty(mission.BeforePlayerUpdate);
        Assert.Empty(mission.TickEnded);
        Assert.Empty(mission.PromptOffers);
        Assert.Null(mission.OfferedPrompt);
        Assert.Null(mission.ViewTakeover);
    }

    private static PlayerCamera Standing()
    {
        PlayerCamera camera = new();
        camera.UseWeaponCycle([]);
        camera.StandAt(Start, 0.5f);
        return camera;
    }

    private static PlayerInputFrame Pushing()
    {
        PlayerInputFrame frame = default;
        frame[(int)PlayerInputSlot.MoveUp] = 1f;
        frame.LookX = 0.3f;
        return frame;
    }
}

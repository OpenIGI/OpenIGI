using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Events;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.AiScenarioSupport;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Desktop.Tests;

/// <summary>Mission 11's first objective, retrieve the sniper rifle, completes once the Dragunov is taken (issue #44).</summary>
/// <remarks>
/// EditVariable_101 adds <c>GunPickup_202.isPickedUp</c>. It sits before the pickup and the player in the level walk, so it reads
/// the flag on the tick after the touch, while the pickup's task is still alive. Retail deletes the task in the pickup's own
/// update (<c>0x46CEA0</c>), not in the touch (<c>0x46D230</c>). The port used to delete it on the touch, so the expression
/// compiled the dead task's word to <c>0</c>. The objective never completed, and the cable car never ended the level.
/// </remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class MissionElevenSniperObjectiveTests : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    [RequiresGameDataFact]
    public void Taking_the_dragunov_completes_the_first_objective() => WithSaveGames(false, () => TakeTheDragunov(deleted: true));

    /// <summary>
    /// The same in a level that keeps save games, as the game is played: the rifle's task is withdrawn rather than deleted, so
    /// a save read later can put the rifle back, and the level's scripts read exactly what they read of a deleted one -- the
    /// counter counts it once, and its <c>isPickedUp</c> is gone.
    /// </summary>
    [RequiresGameDataFact]
    public void Taking_the_dragunov_completes_the_first_objective_in_a_level_that_keeps_saves() =>
        WithSaveGames(true, () => TakeTheDragunov(deleted: false));

    // Runs a check with Project I.G.I.'s save games on or off, and puts the setting back.
    private static void WithSaveGames(bool on, Action check)
    {
        bool saves = OpenIGI.Game.GameplayCVars.Igi1Saves.Value;
        OpenIGI.Game.GameplayCVars.Igi1Saves.Value = on;
        try
        {
            check();
        }
        finally
        {
            OpenIGI.Game.GameplayCVars.Igi1Saves.Value = saves;
        }
    }

    private void TakeTheDragunov(bool deleted)
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, new HeadlessGameWindow(), "-mission", "11", "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
        PlayerCamera? player = null;
        for (int i = 0; i < 20_000 && player is null; i++)
        {
            Assert.True(host.Step());
            player = host.Player();
        }

        Assert.NotNull(player);
        OpenIGI.Engine.Flow.GameLoop loop = host.Loop();
        GunPickup sniper = MissionOf(host).GunPickups!.Items.Single(pickup => pickup.Task?.Name == "GunPickup_202");

        // The player's update lands it on the rifle once the intro lets go of it.
        for (int i = 0; i < 900 && !sniper.IsCollected; i++)
        {
            player.Body.Position = sniper.Position;
            Assert.True(host.Step());
        }

        Assert.True(sniper.IsCollected, "The player never reached the rifle.");
        Assert.False(sniper.Task!.IsDeleted);
        Assert.Equal(0, Read(loop, "EditVariable_101.nValue"));

        // The next walk: the counter reads the taken rifle, then the rifle's own update deletes its task.
        Assert.True(host.Step());
        host.Stop();

        Assert.Equal(deleted, sniper.Task.IsDeleted);
        Assert.Equal(1, Read(loop, "EditVariable_101.nValue"));

        // Its delete handler (0x46D760) removes the symbol, as the level's deactivate handler does (0x46D1D0).
        Assert.False(loop.Variables.TryRead("GunPickup_202.isPickedUp", out _));
    }

    /// <summary>
    /// The level the issue could not finish: with the rifle taken, objectives 2 and 3 done and the player in the cable car
    /// (AreaActivate_150), the outro (ConditionalContainer_1113) starts and the level completes.
    /// </summary>
    /// <remarks>
    /// Only the rifle is taken for real. The other two counters are set directly: the reporter's consoles worked, and the
    /// outro's condition reads only the counters and the area.
    /// </remarks>
    [RequiresGameDataFact]
    public void With_the_rifle_taken_the_cable_car_ends_the_level()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, new HeadlessGameWindow(), "-mission", "11", "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
        PlayerCamera? player = null;
        for (int i = 0; i < 20_000 && player is null; i++)
        {
            Assert.True(host.Step());
            player = host.Player();
        }

        Assert.NotNull(player);
        OpenIGI.Engine.Flow.GameLoop loop = host.Loop();
        MissionWorld mission = MissionOf(host);
        GunPickup sniper = mission.GunPickups!.Items.Single(pickup => pickup.Task?.Name == "GunPickup_202");
        for (int i = 0; i < 900 && !sniper.IsCollected; i++)
        {
            player.Body.Position = sniper.Position;
            Assert.True(host.Step());
        }

        Assert.True(host.Step());
        Assert.Equal(1, Read(loop, "EditVariable_101.nValue"));
        MissionObjective first = mission.Objectives!.Objectives.Single(objective => objective.TextResource == "M11_OBJ1");
        Assert.Equal(MissionObjectiveState.Complete, first.State);

        Assert.True(loop.Variables.TryWrite("EditVariable_102.nValue", QvmValue.FromInt32(1)));
        Assert.True(loop.Variables.TryWrite("EditVariable_103.nValue", QvmValue.FromInt32(1)));
        OpenIGI.Game.Flow.AreaActivateTask cableCar = Assert.IsType<OpenIGI.Game.Flow.AreaActivateTask>(sniper.Task!.Tree.FromId(150));
        OpenIGI.Core.Numerics.Vector3d inside = cableCar.Position;

        bool outro = false;
        for (int i = 0; i < 300 && !outro; i++)
        {
            player.Body.Position = inside;
            Assert.True(host.Step());
            outro = Read(loop, "ConditionalContainer_1113.isRun") == 1;
        }

        Assert.True(outro, "The outro never started.");

        // The outro's end completes the level (LevelFlow_10: CutScene_114.isFinished), and the host moves on to mission 12.
        bool completed = false;
        host.EventsForTesting!.Subscribe((in MissionEnded ended) => completed |= ended is { MissionId: 11, Completed: true });
        for (int i = 0; i < 3000 && !completed; i++)
        {
            Assert.True(host.Step());
        }

        host.Stop();
        Assert.True(completed, "The level never completed.");
    }

    /// <summary>An AmmoPickup is deleted by its own update once taken (<c>0x46D740</c>), not by the touch (<c>0x46D990</c>).</summary>
    /// <remarks>
    /// With the save games off, as the original had none: a level that keeps save games withdraws a taken pickup rather than
    /// deleting it, so a save read later can put it back (see <c>TakenPickupTask</c>).
    /// </remarks>
    [RequiresGameDataFact]
    public void A_taken_ammo_box_deletes_itself_on_its_next_update() => WithSaveGames(false, TakeTheAmmoBox);

    private void TakeTheAmmoBox()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, new HeadlessGameWindow(), "-mission", "11", "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
        PlayerCamera? player = null;
        for (int i = 0; i < 20_000 && player is null; i++)
        {
            Assert.True(host.Step());
            player = host.Player();
        }

        Assert.NotNull(player);
        OpenIGI.Engine.Flow.GameLoop loop = host.Loop();
        // The SPAS shells beside the Dragunov. Like every box in the level, the task has no id (-1), so the level never
        // registers its isPickedUp (0x4F0780 sends the activate event only to a task with an id).
        GenericPickup box = MissionOf(host).GenericPickups!.Items
            .Where(pickup => pickup.Task.Type.Name == "AmmoPickup")
            .MinBy(pickup => Math.Abs(pickup.Position.X - 114483650) + Math.Abs(pickup.Position.Y + 56890100))!;
        Assert.True(box.Task.Id < 0);
        Assert.False(loop.Variables.TryRead($"{box.Task.Name}.isPickedUp", out _));

        for (int i = 0; i < 900 && !box.IsCollected; i++)
        {
            player.Body.Position = box.Position;
            Assert.True(host.Step());
        }

        Assert.True(box.IsCollected, "The player never reached the box.");
        Assert.False(box.Task.IsDeleted);

        Assert.True(host.Step());
        host.Stop();

        Assert.True(box.Task.IsDeleted);
    }

    private static int Read(OpenIGI.Engine.Flow.GameLoop loop, string name)
    {
        Assert.True(loop.Variables.TryRead(name, out QvmValue value), name);
        return value.ToInt32();
    }
}

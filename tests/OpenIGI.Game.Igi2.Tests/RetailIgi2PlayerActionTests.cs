using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The player's action machine: idle, the weapon change, and the selection keys that ask for one.</summary>
public sealed class RetailIgi2PlayerActionTests
{
    private const int TickLimit = 600;

    private static readonly Lazy<Mission1Setup> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2HumanPlayerTask task = (Igi2HumanPlayerTask)PlayerSpawn.FindGameplayTask(level.Root)!;
        Igi2Session session = Igi2Session.Start();
        return new Mission1Setup(
            level,
            Igi2AnimationLibrary.Load(packs, resources),
            Igi2Gameplay.FirstPersonRig(name => level.ModelFor(name), task)!,
            session);
    });

    /// <summary>
    /// The next-weapon key with the MP5SD in hand steps the cycle to the next weapon carried, the Glock; the change puts the
    /// MP5SD away with a change sound, waits for the first-person put-away, puts the Glock in hand, draws it, and the
    /// player is idle again with the weapon ready.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_next_weapon_key_changes_to_the_next_weapon_carried()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings, Igi2Inventory inventory) = ArmedPlayer();
        Settle(player);
        int glock = inventory.FindWeapon("WEAPON_ID_GLOCK")!.Number;

        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.NextWeapon);
        player.Update();

        Assert.Equal(Igi2PlayerActionState.ChangeWeapon, player.ActionState);
        Assert.Equal(glock, player.ChangeWeaponId);
        Assert.False(player.IsWeaponReady);
        Assert.Matches("^changeweapon_0[1-4]$", Assert.Single(surroundings.Sounds));
        Assert.Equal(11, player.CurrentUpperBody);
        Assert.Equal("WEAPON_ID_MP5SD", inventory.Current!.WeaponType.WeaponId);

        int ticks = RunUntilIdle(player);
        Assert.InRange(ticks, 2, TickLimit - 1);
        Assert.Equal("WEAPON_ID_GLOCK", inventory.Current!.WeaponType.WeaponId);
        Assert.Same(inventory.Current, inventory.StatusDisplay);
        Assert.Equal(-1, player.ChangeWeaponId);
        Assert.True(player.IsWeaponReady);
        Assert.Equal(Igi2PlayerWeaponStatus.None, player.WeaponStatus);
    }

    /// <summary>The sidearm key picks the Glock out of the sidearm category, the previous-weapon key steps the cycle back.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Category_and_previous_keys_pick_their_weapon()
    {
        (Igi2HumanPlayer player, _, Igi2Inventory inventory) = ArmedPlayer();
        Settle(player);

        FeedPressed(player, Igi2PlayerAction.WeaponCategory2);
        player.Update();
        Assert.Equal(inventory.FindWeapon("WEAPON_ID_GLOCK")!.Number, player.ChangeWeaponId);
        RunUntilIdle(player);

        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.PrevWeapon);
        player.Update();
        int expected = PreviousSelectable(player, inventory, inventory.FindWeapon("WEAPON_ID_GLOCK")!.Number);
        Assert.Equal(expected, player.ChangeWeaponId);
    }

    /// <summary>
    /// The upper-body modes: none clears the upper body and the request; standing, the stand mode plays the stand state's
    /// clip on the upper body; lying, the whole body plays the lying state's clip.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_upper_body_modes_play_their_states()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Igi2PlayerFakes.Player();

        player.SetUpperBody(1);
        Assert.Equal((0, 0, 0f), Assert.Single(animation.UpperBodyPlayed));
        Assert.True(player.IsForceNetUpdate);

        player.Pose = Igi2HumanPose.LyingFlat;
        player.SetUpperBody(2);
        Assert.Contains((75, 0, 0f), animation.Played);
        Assert.Contains(0f, animation.UpperBodyClears);

        player.SetUpperBody(0);
        Assert.Equal(0, player.CurrentUpperBody);
        Assert.Equal(0, player.RequestedUpperBody);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeSurroundings Surroundings, Igi2Inventory Inventory) ArmedPlayer()
    {
        Mission1Setup m = Mission1.Value;
        Igi2PlayerTuning tuning = m.Session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(m.Session.Session.Services, m.Level.Root, tuning, m.Library)!;
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.OnFloor(weapons);
        player.View.ObjectRig = m.Arms;
        player.WeaponCycle = tuning.WeaponCycle;
        return (player, surroundings, weapons.Inventory);
    }

    /// <summary>Runs the first ticks, which enter idle and play its first-person stand clip.</summary>
    private static void Settle(Igi2HumanPlayer player)
    {
        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.None);
        player.Update();
        Assert.Equal(Igi2PlayerActionState.Idle, player.ActionState);
    }

    /// <summary>Feeds one frame with actions pressed this tick, as the category keys are read.</summary>
    private static void FeedPressed(Igi2HumanPlayer player, Igi2PlayerAction pressed)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        new Igi2PlayerInputReader().Read(new Igi2PlayerInputState(0f, 0f, Igi2PlayerAction.None, pressed), frame);
        player.Channels.Accept(frame);
    }

    private static int RunUntilIdle(Igi2HumanPlayer player)
    {
        for (int tick = 1; tick < TickLimit; tick++)
        {
            Igi2PlayerFakes.Feed(player, Igi2PlayerAction.None);
            player.Update();
            if (player.ActionState == Igi2PlayerActionState.Idle)
            {
                return tick;
            }
        }

        return TickLimit;
    }

    /// <summary>The weapon the cycle reaches stepping back from a weapon, trying each entry once.</summary>
    private static int PreviousSelectable(Igi2HumanPlayer player, Igi2Inventory inventory, int from)
    {
        IReadOnlyList<int> cycle = player.WeaponCycle;
        int index = Math.Max(0, cycle.ToList().IndexOf(from));
        for (int left = cycle.Count; left > 0; left--)
        {
            index = index - 1 < 0 ? cycle.Count - 1 : index - 1;
            if (inventory.WeaponByNumber(cycle[index]) is { } item && inventory.IsSelectable(item))
            {
                return cycle[index];
            }
        }

        return -1;
    }

    private sealed record Mission1Setup(Igi2Level Level, Igi2AnimationLibrary Library, Igi2BoneHierarchy Arms, Igi2Session Session);
}

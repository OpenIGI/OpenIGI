using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Throwing mission 1's grenade: the anim triggers its first-person clip fires (<c>AnimController_TriggerEvents</c>,
/// <c>igi2 0x553D50</c>) and the throw state they drive (<c>HumanPlayer_RunActionThrowGrenade</c>, <c>0x4B4880</c>).
/// </summary>
public sealed class RetailIgi2GrenadeThrowTests
{
    private const string Grenade = "WEAPON_ID_GRENADE";

    private static readonly Lazy<Mission1Setup> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2HumanPlayerTask task = (Igi2HumanPlayerTask)PlayerSpawn.FindGameplayTask(level.Root)!;
        return new Mission1Setup(level, task, Igi2AnimationLibrary.Load(packs, resources), Igi2Session.Start());
    });

    /// <summary>
    /// <c>animtrigger.qvm</c> gives the player its drawers, the throw and the hold point, the human its feet and its dead
    /// body, the soldier its throw release and its blow, and every reload sound its own <c>AnimSound</c>.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_trigger_table_names_who_answers_each_trigger()
    {
        Igi2AnimTriggers triggers = Mission1.Value.Session.Require<Igi2AnimTriggers>();

        foreach (int id in new[] { Igi2HumanAnimTrigger.OpenDrawer, Igi2HumanAnimTrigger.CloseDrawer, Igi2HumanAnimTrigger.ThrowGrenade, Igi2HumanAnimTrigger.StopAnimation })
        {
            Assert.True(triggers.TryGet(id, out Igi2AnimTriggerType type));
            Assert.True(triggers.IsAnsweredBy(type, "HumanPlayer"));
        }

        Assert.True(triggers.TryGet(Igi2HumanAnimTrigger.FeetSound, out Igi2AnimTriggerType feet));
        Assert.True(triggers.IsAnsweredBy(feet, "Human"));
        Assert.True(triggers.TryGet(Igi2HumanAnimTrigger.ThrowGrenadeRelease, out Igi2AnimTriggerType release));
        Assert.True(triggers.IsAnsweredBy(release, "HumanSoldier"));
        Assert.False(triggers.TryGet(Igi2HumanAnimTrigger.TimeGrenade, out _));
        Assert.True(triggers.TryGet(1000, out Igi2AnimTriggerType sound));
        Assert.True(triggers.IsAnsweredBy(sound, "AnimSound"));
        Assert.Equal("glock_reload_1", sound.Parameters[0].ToString().Trim('"'));
    }

    /// <summary>
    /// Played forward a frame at a time, the first-person grenade clip fires its time trigger, its hold point and its throw
    /// in that order; stepped back over two of them it fires them again, still in time order.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_clip_fires_its_triggers_as_it_plays_through_them()
    {
        Igi2AnimationClip clip = FirstPersonClip("fire_grenade_1st");
        Recording fired = new();
        Igi2AnimController controller = new() { Target = fired };
        controller.SetAnimationAndTime(clip, 0);
        while (!controller.IsEnded)
        {
            controller.Step(controller.Time + Igi2AnimController.TicksPerFrame);
        }

        Assert.Equal(
            [(3520, Igi2HumanAnimTrigger.TimeGrenade), (4480, Igi2HumanAnimTrigger.StopAnimation), (5760, Igi2HumanAnimTrigger.ThrowGrenade)],
            fired.Triggers);

        fired.Triggers.Clear();
        controller.SetAnimationAndTime(clip, 6000);
        controller.Step(4000);
        Assert.Equal([(4480, Igi2HumanAnimTrigger.StopAnimation), (5760, Igi2HumanAnimTrigger.ThrowGrenade)], fired.Triggers);
    }

    /// <summary>
    /// Fire with a grenade in hand throws: the pin comes out on the throw's eighth tick with a five-second fuse; the clip
    /// holds at its hold point while fire is held, to the sound of the grenade handled; let go, it plays on to the sound of
    /// the throw, and its throw trigger fires the grenade with what is left of the fuse; the clip played out, the player is
    /// idle again.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Fire_throws_a_grenade()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, Igi2PlayerFakes.FakeSurroundings surroundings, RecordingGrenades grenades) = Thrower();
        Igi2Inventory inventory = weapons.Inventory;

        Tick(player, Igi2PlayerAction.Fire);
        Assert.Equal(Igi2PlayerActionState.ThrowGrenade, player.ActionState);

        int fuse = -1;
        for (int i = 0; i < 40 && player.GrenadeHeld < 5; i++)
        {
            Tick(player, Igi2PlayerAction.Fire);
            if (inventory.IsPinPulled && fuse < 0)
            {
                fuse = inventory.Timer;
            }
        }

        Assert.InRange(fuse, 145, 150);
        Assert.Equal(5, player.GrenadeHeld);
        int held = player.View.ObjectAnimation.Time;
        Tick(player, Igi2PlayerAction.Fire);
        Assert.Equal(held, player.View.ObjectAnimation.Time);
        Assert.Single(surroundings.Sounds, static s => s == Igi2HumanPlayer.GrenadeHandleSound);
        Assert.Empty(grenades.Thrown);

        for (int i = 0; i < 60 && grenades.Thrown.Count == 0; i++)
        {
            Tick(player, Igi2PlayerAction.None);
        }

        Igi2GrenadeThrow thrown = Assert.Single(grenades.Thrown);
        Assert.Contains(Igi2HumanPlayer.GrenadeThrowSound, surroundings.Sounds);
        Assert.Equal(Grenade, thrown.WeaponType.WeaponId);
        Assert.InRange(thrown.ExplodeTime, 1, fuse - 1);
        Assert.False(inventory.IsPinPulled);
        Assert.True(thrown.Speed.Z > 0f || thrown.Speed.X != 0f || thrown.Speed.Y != 0f);

        for (int i = 0; i < 120 && player.ActionState == Igi2PlayerActionState.ThrowGrenade; i++)
        {
            Tick(player, Igi2PlayerAction.None);
        }

        Assert.NotEqual(Igi2PlayerActionState.ThrowGrenade, player.ActionState);
    }

    private static Igi2AnimationClip FirstPersonClip(string name)
    {
        Mission1Setup m = Mission1.Value;
        Igi2BoneHierarchy arms = m.Level.ModelFor(Igi2Gameplay.FirstPersonModel(m.Level.ModelFor, m.Task)!)!.BoneHierarchy!;
        return m.Library.Find(arms, name)!;
    }

    private static (Igi2HumanPlayer, Igi2PlayerWeapons, Igi2PlayerFakes.FakeSurroundings, RecordingGrenades) Thrower()
    {
        Mission1Setup m = Mission1.Value;
        Igi2PlayerTuning tuning = m.Session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(m.Session.Session.Services, m.Level.Root, tuning, m.Library)!;
        RecordingGrenades grenades = new();
        weapons.Grenades = grenades;
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.OnFloor(weapons);
        string model = Igi2Gameplay.FirstPersonModel(m.Level.ModelFor, m.Task)!;
        player.View.ObjectModel = model;
        player.View.ObjectRig = m.Level.ModelFor(model)!.BoneHierarchy;
        player.WeaponCycle = tuning.WeaponCycle;
        player.AnimTriggers = m.Session.Require<Igi2AnimTriggers>();
        Igi2Inventory inventory = weapons.Inventory;
        inventory.EnableWeaponType(Grenade, addAmmo: true);
        Assert.True(weapons.Activate(inventory.FindWeapon(Grenade)!));
        player.IsWeaponReady = true;
        for (int i = 0; i < 3; i++)
        {
            Tick(player, Igi2PlayerAction.None);
        }

        surroundings.Sounds.Clear();
        return (player, weapons, surroundings, grenades);
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held)
    {
        Igi2PlayerFakes.Feed(player, held);
        player.Update();
        player.RunDelayed();
        player.Ticks++;
    }

    private sealed class Recording : IIgi2AnimTriggerTarget
    {
        public List<(int Time, int Id)> Triggers { get; } = [];

        public void Trigger(in Igi2AnimationTrigger trigger) => Triggers.Add((trigger.Time, trigger.TriggerId));
    }

    private sealed class RecordingGrenades : IIgi2GrenadeWorld
    {
        public List<Igi2GrenadeThrow> Thrown { get; } = [];

        public void Launch(in Igi2GrenadeThrow grenade) => Thrown.Add(grenade);
    }

    private sealed record Mission1Setup(Igi2Level Level, Igi2HumanPlayerTask Task, Igi2AnimationLibrary Library, Igi2Session Session);
}

using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The player firing mission 1's MP5SD: a full-automatic gun of 30 rounds a pull and a 30-round clip, 800 rounds a
/// minute, a looped <c>mp5_loop</c> with the tail <c>mp5_loop_e</c>, a casing, and an AI detection range of 204800.
/// </summary>
public sealed class RetailIgi2FiringTests
{
    private static readonly Lazy<Mission1Setup> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2HumanPlayerTask task = (Igi2HumanPlayerTask)PlayerSpawn.FindGameplayTask(level.Root)!;
        return new Mission1Setup(level, task, Igi2AnimationLibrary.Load(packs, resources), Igi2Session.Start());
    });

    /// <summary>
    /// A pull fires one round on its first tick: one bullet into the weapon's shot, carrying the gun's damage factor and
    /// penetration over its range in world units along the view's forward axis, a round off the clip, the delay of 800 rounds a minute (<c>(i32)(60 / 800 × 30 + 0.5)</c> = 2), the looped fire sound,
    /// the three AI events of a loud gun, its holder kept visible for fifteen ticks, and a muzzle flash and a casing from
    /// the weapon the view holds.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_pull_fires_one_round_of_the_mp5sd()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, RecordingEffects effects) = ArmedPlayer();

        Tick(player, Igi2PlayerAction.Fire);

        Igi2Inventory inventory = weapons.Inventory;
        Assert.Equal("WEAPON_ID_MP5SD", inventory.Current!.WeaponType.WeaponId);
        Igi2GunShotInstant bullet = Assert.Single(weapons.Firing.GunShot.Instants);
        Assert.Empty(effects.Projectiles);
        Assert.Equal((new Fpu(260f) * 4096.0).F, bullet.Range);
        Assert.Equal(0.29f, bullet.DamageFactor);
        Assert.Equal(5f, bullet.Penetration);
        Assert.Same(inventory.Current.WeaponType, bullet.WeaponType);
        Assert.Equal(29, inventory.Current.Loaded(0));
        Assert.Equal(1, inventory.TotalBulletsFired);
        Assert.Equal(["0:mp5_loop:loop"], effects.Sounds);
        Assert.Equal([39, 15, 23], effects.Events.Select(static e => e.Event));
        Assert.Equal(15, player.IgnoreLightEffectOnVisibilityTimer);
        Assert.Equal(1, player.MissionShots);
        Assert.Single(effects.Flashes);
        Igi2CasingEjection casing = Assert.Single(effects.Casings);
        Assert.Equal("1123_01_1", casing.Model);
        Assert.False(casing.IsGun);
        Assert.Equal(Igi2PlayerActionState.InitFire, player.ActionState);
    }

    /// <summary>
    /// Held, the gun fires on by itself: the second round the next tick, as the first came from the update before the
    /// weapon's own countdown, and then every second tick as its delay of two runs down.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Held_the_mp5sd_fires_every_second_tick_after_the_first_two()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, _) = ArmedPlayer();
        List<int> shotTicks = [];

        for (int tick = 0; tick < 10; tick++)
        {
            int before = weapons.Firing.GunShot.BulletsFired;
            Tick(player, Igi2PlayerAction.Fire);
            if (weapons.Firing.GunShot.BulletsFired > before)
            {
                shotTicks.Add(tick);
            }
        }

        Assert.Equal([0, 1, 3, 5, 7, 9], shotTicks);
    }

    /// <summary>
    /// The MP5SD's kick is its minimum whatever the draw: retail takes the draw across the range less the minimum's size,
    /// which for a positive minimum always falls below it and is held at it. Up 1.8° and sideways −0.9°, each scaled on the
    /// first round by the attack's floor of 0.8, and taken by the view the tick after the shot it came from fired.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_kick_is_the_guns_minimum_scaled_by_the_attack()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, _) = ArmedPlayer();
        Tick(player, Igi2PlayerAction.Fire);

        Igi2WeaponFiring firing = weapons.Firing;
        Fpu factor = (Fpu)Math.Pow(0f, 8f) + 0.8f;
        Assert.Equal(((Fpu)1.8f * factor).F, firing.AlphaKickback);
        Assert.Equal(((Fpu)(-0.9f) * factor).F, firing.GammaKickback);
        Assert.True(firing.IsKickback);
    }

    /// <summary>
    /// Letting go puts the trigger up: the release state, the weapon ready and no longer firing on, and the loop cut by
    /// its tail in the same slot.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Letting_go_stops_the_mp5sd_and_plays_its_tail()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, RecordingEffects effects) = ArmedPlayer();
        for (int tick = 0; tick < 4; tick++)
        {
            Tick(player, Igi2PlayerAction.Fire);
        }

        int fired = effects.Projectiles.Count;
        Tick(player, Igi2PlayerAction.None);

        Assert.Equal(fired, effects.Projectiles.Count);
        Assert.Equal(Igi2PlayerActionState.FireRelease, player.ActionState);
        Assert.False(weapons.Firing.IsAutoFire);
        Assert.Equal(Igi2WeaponStatus.Ready, weapons.Inventory.Status);
        Assert.Equal("0:mp5_loop_e:once", effects.Sounds[^1]);
    }

    /// <summary>An MP5SD with nothing loaded and nothing carried fires empty: the dry click, and back to idle once the key is up.</summary>
    [RequiresTitleDataFact("igi2")]
    public void An_empty_mp5sd_clicks()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, RecordingEffects effects, Igi2PlayerFakes.FakeSurroundings surroundings) = ArmedPlayerWithWorld();
        Igi2Inventory inventory = weapons.Inventory;
        inventory.Disable(inventory.Current!, removeCarriedAmmo: true);
        inventory.EnableWeaponType("WEAPON_ID_MP5SD", addAmmo: false);

        Tick(player, Igi2PlayerAction.Fire);

        Assert.Equal(Igi2PlayerActionState.FireEmpty, player.ActionState);
        Assert.Contains(Igi2HumanPlayer.DryFireSound, surroundings.Sounds);
        Assert.Empty(effects.Projectiles);
        Tick(player, Igi2PlayerAction.None);
        Assert.Equal(Igi2PlayerActionState.Idle, player.ActionState);
    }

    /// <summary>
    /// One pull of the trigger takes exactly its draws from the run stream: two for the player's inaccuracy, two for the
    /// kick, and the casing's one bounded and three raw draws.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_shot_takes_eight_draws_from_the_run_stream()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, _) = ArmedPlayer();
        player.Random = new IgiRandom(0x1234u);
        IgiRandom reference = new(0x1234u);

        player.Weapon.Fire(0);

        Assert.Single(weapons.Firing.GunShot.Instants);
        for (int i = 0; i < 8; i++)
        {
            reference.NextRaw();
        }

        Assert.Equal(reference.NextRaw(), player.Random.NextRaw());
    }

    /// <summary>
    /// The reload key with a round fired reloads: the start, then the rest of the clip taken from what is carried
    /// (<c>Weapon_SingleReloadHandler</c>), and back to idle once the clips have played.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Reloading_tops_the_clip_up_from_what_is_carried()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, _) = ArmedPlayer();
        Tick(player, Igi2PlayerAction.Fire);
        Tick(player, Igi2PlayerAction.None);
        for (int tick = 0; tick < 30 && player.ActionState != Igi2PlayerActionState.Idle; tick++)
        {
            Tick(player, Igi2PlayerAction.None);
        }

        Igi2Inventory inventory = weapons.Inventory;
        Igi2WeaponItem mp5 = inventory.Current!;
        int loaded = mp5.Loaded(0);
        int carried = inventory.FireTypeRounds(mp5, 0).Available;
        Assert.True(loaded < 30);

        Tick(player, Igi2PlayerAction.Reload);
        Assert.Equal(Igi2PlayerActionState.Reload, player.ActionState);
        for (int tick = 0; tick < 600 && player.ActionState != Igi2PlayerActionState.Idle; tick++)
        {
            Tick(player, Igi2PlayerAction.None);
        }

        Assert.Equal(Igi2PlayerActionState.Idle, player.ActionState);
        Assert.Equal(30, mp5.Loaded(0));
        Assert.Equal(carried - (30 - loaded), inventory.FireTypeRounds(mp5, 0).Available);
    }

    /// <summary>Fired with nothing loaded but more carried, the MP5SD reloads instead of clicking.</summary>
    [RequiresTitleDataFact("igi2")]
    public void An_mp5sd_fired_empty_with_more_carried_reloads()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, RecordingEffects effects) = ArmedPlayer();
        Igi2Inventory inventory = weapons.Inventory;
        inventory.Disable(inventory.Current!, removeCarriedAmmo: false);
        inventory.EnableWeaponType("WEAPON_ID_MP5SD", addAmmo: false);

        Tick(player, Igi2PlayerAction.Fire);

        Assert.Equal(Igi2PlayerActionState.Reload, player.ActionState);
        Assert.Empty(effects.Projectiles);
    }

    /// <summary>A reload takes no more than is carried, and a round at a time for a weapon reloaded so.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_reload_takes_no_more_than_is_carried()
    {
        (_, Igi2PlayerWeapons weapons, _) = ArmedPlayer();
        Igi2Inventory inventory = weapons.Inventory;
        Igi2WeaponItem mp5 = inventory.Current!;
        Igi2AmmoItem clips = inventory.FindAmmo(inventory.FireTypeRounds(mp5, 0).AmmoType!.AmmoId)!;
        inventory.Disable(mp5, removeCarriedAmmo: true);
        inventory.EnableWeaponType("WEAPON_ID_MP5SD", addAmmo: false);
        inventory.AddAmmo(clips.AmmoType.AmmoId, 12);

        weapons.Firing.Reload();

        Assert.Equal(12, mp5.Loaded(0));
        Assert.Equal(0, clips.Count);
    }

    /// <summary>
    /// The MP5SD's own clips come from its gun (<c>Gun_GetAnimHandler</c>, asked by <c>WeaponType_GetAnimHandler</c> for
    /// what the weapon type does not name): <c>fire_mp5_1st</c> at its own speed, and <c>reload_mp5_1st</c> at the rate
    /// that makes it last the gun's reload time, the gun naming no reload loop or end.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_mp5sd_gives_its_guns_fire_and_reload_clips()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, _) = ArmedPlayer();
        Igi2GunTask gun = (Igi2GunTask)weapons.Inventory.Current!.WeaponType.FireType(0)!;

        Igi2WeaponAnim fire = weapons.GetAnim(Igi2WeaponAnimRequest.FirstFire, player.View.ObjectRig, 0)!.Value;
        Igi2WeaponAnim reload = weapons.GetAnim(Igi2WeaponAnimRequest.FirstReloadStart, player.View.ObjectRig, 0)!.Value;

        Assert.Equal("fire_mp5_1st", fire.Clip.Name);
        Assert.Equal(1f, fire.TimeScale);
        Assert.Equal("reload_mp5_1st", reload.Clip.Name);
        Assert.Equal((((Fpu)0.0 + ((new Fpu(reload.Clip.TotalTime) * 0.00020833334f) * 1.0)) / gun.ReloadTimeSeconds).F, reload.TimeScale);
        Assert.Null(weapons.GetAnim(Igi2WeaponAnimRequest.FirstReloadLoop, player.View.ObjectRig, 0));
        Assert.Null(weapons.GetAnim(Igi2WeaponAnimRequest.FirstFire, player.View.ObjectRig, -1));
    }

    /// <summary>
    /// The alternate key on the MP5SD fires its second slot, a fire mode (<c>FireMode_WeaponFireHandler</c>): the pull's
    /// first tick steps the weapon to its next of three fire modes and fires nothing, and each later pull steps it on,
    /// round to the first again.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_alternate_key_steps_the_mp5sd_through_its_fire_modes()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, RecordingEffects effects) = ArmedPlayer();
        Igi2WeaponItem mp5sd = weapons.Inventory.Current!;
        Assert.Equal(3, mp5sd.WeaponType.AvailableFireModes);
        List<int> modes = [];

        for (int pull = 0; pull < 3; pull++)
        {
            for (int tick = 0; tick < 4; tick++)
            {
                Tick(player, Igi2PlayerAction.AlternateFire);
            }

            modes.Add(mp5sd.ActiveFireMode);
            for (int tick = 0; tick < 4; tick++)
            {
                Tick(player, Igi2PlayerAction.None);
            }
        }

        Assert.Equal([1, 2, 0], modes);
        Assert.Equal(30, mp5sd.Loaded(0));
        Assert.Equal(0, weapons.Firing.GunShot.BulletsFired);
        Assert.Empty(effects.Sounds);
    }

    /// <summary>
    /// Fired, the binoculars' zoom (<c>Zoom_WeaponFireHandler</c>) puts the view into zoom mode with its overlay and
    /// steers it to the zoom's widest scale a third of the gap a tick; the zoom keys step the aim by the zoom's factor,
    /// within its scales, sounding at the view; fired again, the zoom comes off and the view is back to its default.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_binoculars_zoom_the_view_in_and_out()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, RecordingEffects effects) = ArmedPlayer();
        Igi2WeaponItem binoculars = weapons.Inventory.Weapons.Single(static w => w.WeaponType.WeaponId == "WEAPON_ID_BINOCULARS");
        Assert.True(weapons.Inventory.Activate(binoculars));
        Igi2ZoomTask zoom = (Igi2ZoomTask)binoculars.WeaponType.FireType(0)!;
        Igi2HumanView view = player.View;

        weapons.Firing.Fire(0);

        Assert.True(view.IsZoomMode);
        Assert.Equal(Igi2WeaponOverlayStatus.WithOverlay, weapons.Firing.OverlayStatus);
        Assert.Equal([$"overlay:{zoom.OverlayQTaskType}"], effects.Sounds);
        Assert.Equal(zoom.MaximumFieldOfViewScale, view.IdealFieldOfView);
        float speed = ((new Fpu(Igi2HumanView.DefaultFieldOfView) - zoom.MaximumFieldOfViewScale) * 0.33333334f).F;
        Assert.Equal(Math.Abs(speed), view.FieldOfViewSpeed);
        for (int tick = 0; tick < 4; tick++)
        {
            view.RunZoom();
        }

        Assert.Equal(zoom.MaximumFieldOfViewScale, view.FieldOfView);

        weapons.Firing.Zoom(zoomIn: true);
        Assert.Equal((new Fpu(zoom.MaximumFieldOfViewScale) / zoom.ZoomStepFactor).F, view.IdealFieldOfView);
        Assert.Equal($"view:{zoom.Sound}", effects.Sounds[^1]);
        weapons.Firing.Zoom(zoomIn: false);
        weapons.Firing.Zoom(zoomIn: false);
        Assert.Equal(zoom.MaximumFieldOfViewScale, view.IdealFieldOfView);

        weapons.Firing.UpdateStatus();
        weapons.Firing.Fire(0);

        Assert.False(view.IsZoomMode);
        Assert.Equal(Igi2WeaponOverlayStatus.None, weapons.Firing.OverlayStatus);
        Assert.Equal(Igi2HumanView.DefaultFieldOfView, view.FieldOfView);
        Assert.Equal("overlay:off", effects.Sounds[^1]);
    }

    /// <summary>The three angles of an orientation give it back (<c>Real32x9_GetAngles</c> against <c>Real32x9_Create</c>).</summary>
    [Fact]
    public void The_angles_of_an_orientation_rebuild_it()
    {
        float[] m = Igi2Pose.Rotation(0.3f, -0.2f, 2.5f);

        (float alpha, float beta, float gamma) = Igi2Pose.Angles(m);

        Assert.InRange(alpha, 0.3f - 1e-5f, 0.3f + 1e-5f);
        Assert.InRange(beta, (float)((2 * Math.PI) - 0.2) - 1e-5f, (float)((2 * Math.PI) - 0.2) + 1e-5f);
        Assert.InRange(gamma, 2.5f - 1e-5f, 2.5f + 1e-5f);
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held)
    {
        Igi2PlayerFakes.Feed(player, held);
        player.Update();
        player.RunDelayed();
        player.Ticks++;
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerWeapons Weapons, RecordingEffects Effects) ArmedPlayer()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, RecordingEffects effects, _) = ArmedPlayerWithWorld();
        return (player, weapons, effects);
    }

    private static (Igi2HumanPlayer, Igi2PlayerWeapons, RecordingEffects, Igi2PlayerFakes.FakeSurroundings) ArmedPlayerWithWorld()
    {
        Mission1Setup m = Mission1.Value;
        Igi2PlayerTuning tuning = m.Session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(m.Session.Session.Services, m.Level.Root, tuning, m.Library)!;
        RecordingEffects effects = new();
        weapons.Effects = effects;
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.OnFloor(weapons);
        string model = Igi2Gameplay.FirstPersonModel(m.Level.ModelFor, m.Task)!;
        player.View.ObjectModel = model;
        player.View.ObjectRig = m.Level.ModelFor(model)!.BoneHierarchy;
        player.WeaponCycle = tuning.WeaponCycle;
        player.IsWeaponReady = true;
        for (int i = 0; i < 3; i++)
        {
            Tick(player, Igi2PlayerAction.None);
        }

        effects.Clear();
        return (player, weapons, effects, surroundings);
    }

    private sealed class RecordingEffects : IIgi2WeaponEffects
    {
        public List<Igi2ProjectileLaunch> Projectiles { get; } = [];

        public List<Igi2WeaponAiEvent> Events { get; } = [];

        public List<int> Flashes { get; } = [];

        public List<Igi2CasingEjection> Casings { get; } = [];

        public List<string> Sounds { get; } = [];

        public void Clear()
        {
            Projectiles.Clear();
            Events.Clear();
            Flashes.Clear();
            Casings.Clear();
            Sounds.Clear();
        }

        public void CreateProjectile(in Igi2ProjectileLaunch launch) => Projectiles.Add(launch);

        public void SendAiEvent(in Igi2WeaponAiEvent aiEvent) => Events.Add(aiEvent);

        public void MuzzleFlash(int weaponObject, string? sprite, float size) => Flashes.Add(weaponObject);

        public void EjectCasing(in Igi2CasingEjection casing) => Casings.Add(casing);

        public void PlaySound(int slot, string sound, bool looped) => Sounds.Add($"{slot}:{sound}:{(looped ? "loop" : "once")}");

        public void ClearSoundSequence() => Sounds.Add("clear");

        public void AddSoundToSequence(string sound, float timeToPlay) => Sounds.Add($"seq:{sound}");

        public void MaintainSoundSequence()
        {
        }

        public void ShowOverlay(Igi2ZoomTask zoom) => Sounds.Add($"overlay:{zoom.OverlayQTaskType}");

        public void RemoveOverlay() => Sounds.Add("overlay:off");

        public void PlayViewSound(string sound) => Sounds.Add($"view:{sound}");
    }

    private sealed record Mission1Setup(Igi2Level Level, Igi2HumanPlayerTask Task, Igi2AnimationLibrary Library, Igi2Session Session);
}

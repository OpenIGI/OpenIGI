using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Systems;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Project I.G.I.'s player standing in another game's level takes the game's guns, and is drawn with its arms and HUD.</summary>
public sealed class Igi1TitlePresentationTests
{
    [RequiresGameDataFact]
    public void A_guest_player_takes_a_laid_gun_and_is_drawn_with_its_arms_and_hud()
    {
        Igi1GuestContent content = Igi1GuestContent.Load(GameData.RequireRoot(), []);
        Igi1GuestLevel level = Igi1GuestLevel.Open(content);
        GameLoop host = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(host.Types);
        GameTask root = host.Tasks.New(host.Tasks.Root, host.Types.Get(EngineTaskTypes.Level));
        HumanPlayerTask task = (HumanPlayerTask)host.Tasks.New(root, host.Types.Get(PlayerTaskTypes.HumanPlayer));
        Igi1GuestSoundStage stage = new(null, content.Arsenal.Materials, new IgiRandom(), static () => 0, static () => default);
        Igi1TitlePlayer player = level.AddPlayer(new Igi2CollisionWorld([], null), new Igi1GuestPlayerStart(
            task, ObjectCollisionIndex.Build([]), AiDifficulty.Normal, new Igi1GuestPlayerSurroundings(stage, NullLogger.Instance))
        {
            Loadout = PlayerLoadout.Of([new LoadoutWeapon("Gun", "WEAPON_ID_AK47", 0)], [new LoadoutAmmo("AMMO_ID_AK47", 90)]),
        });

        // A gun laid at the player's feet is taken as the player reaches it.
        Assert.Null(level.AddGunPickup("WEAPON_ID_NOT_A_GUN", player.Position, Matrix3f.Identity));
        Assert.NotNull(level.AddGunPickup("WEAPON_ID_UZI", player.Position, Matrix3f.Identity));
        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(new TitlePlayerInput(0f, 0f, default, default, 1f, false));
        }

        Assert.True(content.Arsenal.Weapons.TryGetByScriptId("WEAPON_ID_UZI", out Game.Weapons.WeaponType uzi));
        Assert.True(player.Human.Rig.Weapons!.HasWeapon(uzi.WeaponId));
        Assert.All(level.GunPickups.Items, static pickup => Assert.True(pickup.IsCollected));

        // Its arms and the weapon in hand, from the game's own mission's packs.
        RecordingGraphicsDevice device = new();
        Assert.True(content.Guest.Session.Services.TryGet(out MagicObjectRegistry? magic));
        using Igi1TitleViewModel arms = new(device, content.Guest.Resources, magic, () => player, NullLogger.Instance, content.Mission);
        Assert.True(arms.Load(content.Mission));
        TitleFirstPersonView view = Assert.IsType<TitleFirstPersonView>(player.FirstPerson);
        Camera3D eye = new(new Vector3d(player.EyePosition.X / 4096.0, player.EyePosition.Y / 4096.0, player.EyePosition.Z / 4096.0), player.ViewOrientation);
        arms.Draw(view, eye, ViewModelProjection.Create(view.ObjectFieldOfView, 1000f));
        Assert.NotEmpty(device.MeshDraws);

        // Its HUD, from the game's own files, wherever the host's files are.
        using Igi1TitleHud hud = new(device, NullLogger.Instance, content.Arsenal.Ammunition, own: content.Guest.FileSystem);
        hud.Load(new Engine.Vfs.VirtualFileSystem());
        SpriteBatch batch = new(device);
        int before = device.Draws.Count;
        batch.Begin();
        hud.Draw(batch, player, 1280, 720);
        batch.End();
        Assert.True(device.Draws.Count > before);
    }

    [RequiresGameDataFact]
    public void A_guest_player_struck_dead_watches_from_the_death_camera_pulled_back_off_its_eye()
    {
        Igi1GuestContent content = Igi1GuestContent.Load(GameData.RequireRoot(), []);
        Igi1GuestLevel level = Igi1GuestLevel.Open(content);
        GameLoop host = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(host.Types);
        GameTask root = host.Tasks.New(host.Tasks.Root, host.Types.Get(EngineTaskTypes.Level));
        HumanPlayerTask task = (HumanPlayerTask)host.Tasks.New(root, host.Types.Get(PlayerTaskTypes.HumanPlayer));
        Igi1GuestSoundStage stage = new(null, content.Arsenal.Materials, new IgiRandom(), static () => 0, static () => default);
        Igi1TitlePlayer player = level.AddPlayer(new Igi2CollisionWorld([], null), new Igi1GuestPlayerStart(
            task, ObjectCollisionIndex.Build([]), AiDifficulty.Normal, new Igi1GuestPlayerSurroundings(stage, NullLogger.Instance)));
        player.Tick(new TitlePlayerInput(0f, 0f, default, default, 1f, false));
        Vector3d eye = player.EyePosition;

        for (int round = 0; round < 20 && !player.Human.Life.IsDead; round++)
        {
            task.WeaponHit(new Game.Weapons.WeaponHit(eye, new Vector3f(1f, 0f, 0f), 1f, new Vector3f(-1f, 0f, 0f), null, null));
        }

        Assert.True(player.Human.Life.IsDead);
        for (int tick = 0; tick < 30; tick++)
        {
            player.Tick(new TitlePlayerInput(0f, 0f, default, default, 1f, false));
        }

        Assert.True(player.IsDeathViewActive);
        Assert.True(Vector3d.Distance(player.EyePosition, eye) > 1024.0, "the camera pulled back from where the eyes were");
    }
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Presentation.Igi2.Hud;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Retail status art, fading hit effects and interaction feedback reach the draw stream.</summary>
public sealed class Igi2StatusPresentationTests
{
    /// <summary>The installed archives supply every status badge, the font and camera glow.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Retail_status_assets_upload()
    {
        using RecordingGraphicsDevice device = new();
        using Igi2SpriteResources assets = new(device, Files(), NullLogger.Instance);
        string[] names = ["stance_standing.spr", "stance_crouching.spr", "stance_lying.spr", "stance_running.spr",
            "powerbar.spr", "powerbarbackground.spr", "visibilitybar.spr", "visibilitymeter.spr", "progressbar.spr",
            "action_picklock.spr", "a_codepad.spr", "alarmlight.spr"];
        foreach (string name in names) { Assert.NotNull(assets.Get(name)); }
        Assert.True(assets.Get("font3.fnt")?.IsFont);
        Assert.NotNull(assets.Get("visibilityalert.spr"));
    }

    /// <summary>Health, visibility and posture remain the same relative size at 720p and 1440p, including the visibility eye.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Status_art_scales_with_the_viewport()
    {
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
            Igi2HumanPlayerGeneral.Unset with { MaxDamageScale = 1f }, default, 0f, Game.Ai.AiDifficulty.Normal);
        player.Human.Damage.MaxDamage = 100f;
        player.Human.Lightmap = 0.5f;
        using RecordingGraphicsDevice device = new();
        using Igi2HudRenderer hud = new(device, NullLogger.Instance);
        hud.Load(Files());
        SpriteBatch batch = new(device);
        batch.Begin(); hud.Draw(batch, player, 1280, 720); batch.End();
        Vertex2D[] small = [.. device.Draws.SelectMany(d => d.Vertices)];
        Assert.Equal(24, small.Length); // background, health, visibility meter, eye, visibility fill, posture.
        device.Draws.Clear();
        batch.Begin(); hud.Draw(batch, player, 2560, 1440); batch.End();
        Vertex2D[] large = [.. device.Draws.SelectMany(d => d.Vertices)];
        Assert.Equal(small.Length, large.Length);
        for (int i = 0; i < small.Length; i++)
        {
            Assert.Equal(small[i].X * 2f, large[i].X);
            Assert.Equal(small[i].Y * 2f, large[i].Y);
            Assert.Equal(small[i].Color, large[i].Color);
        }
        Assert.All(large, v => { Assert.InRange(v.X, 0f, 2560f); Assert.InRange(v.Y, 0f, 1440f); });
    }

    /// <summary>The held weapon has its icon above a right-aligned name and fire mode, with ammo beneath the divider.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Weapon_status_uses_the_retail_stack()
    {
        Igi2Session session = Igi2Session.Start();
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, level.Root, session.Require<Igi2PlayerTuning>())!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
            session.Require<Igi2PlayerTuning>().General, default, 0f, Game.Ai.AiDifficulty.Normal, weapons: weapons);
        using RecordingGraphicsDevice device = new();
        using Igi2HudRenderer hud = new(device, NullLogger.Instance);
        hud.Load(Files());
        SpriteBatch batch = new(device);
        batch.Begin(); hud.Draw(batch, player, 1280, 720); batch.End();
        Vertex2D[] divider = Assert.Single(device.Draws, d => !d.Texture.IsValid && d.Vertices.All(v => v.X > 1000f && v.Color.A == 128)).Vertices;
        float baseline = divider.Min(v => v.Y), right = divider.Max(v => v.X);
        Assert.Equal(1260f, right);
        Assert.InRange(baseline, 689.9f, 690.1f);
        // The font is uploaded first; all of this font's pages precede the status sprites.
        using RecordingGraphicsDevice metricsDevice = new();
        using Igi2SpriteResources metrics = new(metricsDevice, Files(), NullLogger.Instance);
        SpriteResource font = metrics.Get("font3.fnt")!;
        BitmapTextRenderer text = new(font);
        Weapons.Igi2WeaponTypeTask weapon = weapons.Inventory.Current!.WeaponType;
        string label = weapon.WeaponName + " : " + weapon.FiremodeName;
        Vertex2D[] glyphs = [.. device.Draws.Where(d => d.Texture.IsValid && d.Texture.Id <= font.Textures.Count)
            .SelectMany(d => d.Vertices).Where(v => v.X > 1000f)];
        Assert.Contains(glyphs, v => v.Y < baseline);
        Assert.Contains(glyphs, v => v.Y > baseline);
        Assert.Equal(right - text.Measure(label).Width, glyphs.Where(v => v.Y < baseline).Min(v => v.X));
        Assert.Contains(device.Draws, d => d.Texture.Id > font.Textures.Count && d.Vertices.All(v => v.X > 1000f && v.Y < baseline - text.LineHeight - 2f));
        Assert.All(glyphs, v => Assert.InRange(v.Y, 0f, 720f));
    }

    /// <summary>A hit adds a red screen quad that fades away through simulation ticks; status remains without a crosshair.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Hud_draws_status_and_a_fading_directional_hit()
    {
        RetailIgi2Mission1 mission = RetailIgi2Mission1.Load(collision: true);
        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(mission.Collision!, mission.Level, null, default, 0f);
        using RecordingGraphicsDevice device = new();
        using Igi2HudRenderer hud = new(device, NullLogger.Instance);
        hud.Load(Files());
        SpriteBatch batch = new(device);
        Draw();
        Assert.Contains(device.Draws, static d => d.Texture.IsValid);
        player.Human.HandleDamage(Igi2DamagePart.UpperBody, 0.01f, new Vector3f(1f, 0f, 0f));
        float before = player.Human.DamageFlash.Max();
        device.Draws.Clear();
        Draw();
        Assert.Contains(device.Draws.SelectMany(static d => d.Vertices), static v => v.Color.R == 255 && v.Color.G == 0 && v.Color.A > 0);
        player.Tick(RetailIgi2Mission1.Idle);
        Assert.InRange(player.Human.DamageFlash.Max(), 0f, before - 0.02f);
        for (int i = 0; i < 45; i++) { player.Tick(RetailIgi2Mission1.Idle); }
        Assert.All(player.Human.DamageFlash, static f => Assert.Equal(0f, f));

        void Draw() { batch.Begin(); hud.Draw(batch, player, 1280, 720); batch.End(); }
    }

    /// <summary>A timed action uses its authored badge, including padlocks rather than a universal codepad.</summary>
    [Fact]
    public void Timed_actions_keep_their_authored_badge()
    {
        Igi2GenericTbaTask action = new() { Sprite = "STATUSSCREEN:action_picklock.spr" };
        Assert.Equal(action.Sprite, Igi2HudRenderer.InteractionIcon(action));
    }

    private static VirtualFileSystem Files()
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        return files;
    }
}

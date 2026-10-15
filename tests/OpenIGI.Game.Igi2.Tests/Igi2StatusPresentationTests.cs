using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Hud;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

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
        Assert.True(assets.Get("fontmp.fnt")?.IsFont);
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

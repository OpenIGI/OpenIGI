using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Overlay holders, computer plans and model LOD settings, as overlayholder.c, computerplan.c and lodsettings.c run them.</summary>
public sealed class Igi2ScreenTasksTests
{
    /// <summary>A holder whose visible expression holds puts its overlay up once, with the view infrared as its other expression says.</summary>
    [Fact]
    public void A_visible_holder_puts_its_overlay_up()
    {
        Igi2ScreenOverlay screen = new();
        Igi2OverlayHolderTask holder = new() { VisibleExpression = "1", InfraredExpression = "1", OverlayType = 3, ShowDistance = false, Screen = screen };
        holder.Run(Level());
        Assert.True(holder.IsVisible);
        Assert.Equal(Igi2OverlayType.LaserDesignator, screen.Overlay);
        Assert.True(screen.IsInfrared);
        Assert.False(screen.ShowsDistance);
    }

    /// <summary>A holder with no visible expression is never up, as its create handler leaves it.</summary>
    [Fact]
    public void A_holder_without_a_visible_expression_stays_down()
    {
        Igi2ScreenOverlay screen = new();
        Igi2OverlayHolderTask holder = new() { OverlayType = 0, Screen = screen };
        holder.Run(Level());
        Assert.False(holder.IsVisible);
        Assert.Null(screen.Overlay);
    }

    /// <summary>Plans are listed on the computer in the order they are made, and forgotten with the level.</summary>
    [Fact]
    public void Plans_are_listed_in_order()
    {
        Igi2Computer computer = new();
        Igi2ComputerPlanTask first = new() { PictureFile = "a.tga" }, second = new() { PictureFile = "b.tga" };
        first.Attach(computer);
        second.Attach(computer);
        Assert.Equal([first, second], computer.Plans);
        computer.BeginLevel();
        Assert.Empty(computer.Plans);
    }

    /// <summary>Each distance is made no less than the one before it, and the model takes them in world units.</summary>
    [Fact]
    public void A_setting_keeps_its_distances_in_order()
    {
        Igi2ModelLodTable table = new();
        Igi2ModelLodSettingsTask setting = new() { Model = "123_45_6  ", ToLod2 = 10f, ToLod3 = 5f, ToLod4 = 20f, ToLod5 = 1f, ToCutoff = 30f };
        setting.Apply(table);
        Assert.True(table.TryGet("123_45_6", out IReadOnlyList<float> distances));
        Assert.Equal([40960f, 40960f, 81920f, 81920f, 122880f], distances);
    }

    /// <summary>The game's own settings file loads at start-up, giving models distances of their own.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_games_settings_load_at_start_up()
    {
        Igi2Session session = Igi2Session.Start();
        Assert.True(session.Require<Igi2ModelLodTable>().Count > 0);
    }

    private static Igi2LevelRun Level()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        GameTask root = tree.New(null, types.Register<ContainerTask>("Container"));
        return new(root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
    }
}

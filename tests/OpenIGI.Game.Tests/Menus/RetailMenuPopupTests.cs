using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// A popup stands in for the screen while it is up (<c>0x418080</c>, <c>0x4188B0</c>, <c>0x418940</c>) and is drawn over it
/// (<c>0x418970</c>).
/// </summary>
public sealed class RetailMenuPopupTests
{
    private const int QuitPopup = 934;

    [RequiresGameDataFact]
    public void The_quit_popup_takes_the_selection_and_is_drawn_over_the_screen_as_it_fades_in()
    {
        Harness menu = Boot();
        MenuManagerTask manager = menu.Menu.Manager;
        MenuScreenTask screen = manager.CurrentScreen!;

        manager.RunAction($"MenuManager_ActivatePopupScreen({QuitPopup})");
        for (int i = 0; i < 32 && (manager.CurrentPopup is null || manager.Navigator.State != MenuManagerState.Active); i++)
        {
            Tick(menu);
            menu.Loop.Tasks.Render();
        }

        DialogWindowTask popup = Assert.IsType<DialogWindowTask>(manager.CurrentPopup);
        Assert.Equal(QuitPopup, popup.Id);
        Assert.Equal(MenuManagerState.Active, manager.Navigator.State);

        // Its controls are the selection, and none of the screen's is.
        Assert.NotEmpty(manager.Selection.Items);
        Assert.All(manager.Selection.Items, item => Assert.True(IsUnder(item, popup)));

        // Drawn over the screen at the fade, which rises to full; the screen is still the one beneath.
        Assert.Same(screen, manager.CurrentScreen);
        int[] alphas = [.. menu.Host.PopupsDrawn.Where(d => d.Popup == popup).Select(d => d.Alpha)];
        Assert.NotEmpty(alphas);
        Assert.Equal(MenuNavigator.FullAlpha, alphas[^1]);
        Assert.True(alphas[0] < MenuNavigator.FullAlpha);
        Assert.Equal(alphas.Order(), alphas);
    }

    [RequiresGameDataFact]
    public void Dismissing_the_popup_gives_the_screen_its_selection_back()
    {
        Harness menu = Boot();
        MenuManagerTask manager = menu.Menu.Manager;
        MenuScreenTask screen = manager.CurrentScreen!;

        manager.RunAction($"MenuManager_ActivatePopupScreen({QuitPopup})");
        for (int i = 0; i < 32 && (manager.Navigator.State != MenuManagerState.Active || manager.CurrentPopup is null); i++)
        {
            Tick(menu);
        }

        manager.RunAction("MenuManager_DeactivatePopuScreen()");
        for (int i = 0; i < 32 && manager.CurrentPopup is not null; i++)
        {
            Tick(menu);
        }

        Assert.Null(manager.CurrentPopup);
        Assert.NotEmpty(manager.Selection.Items);
        Assert.All(manager.Selection.Items, item => Assert.True(IsUnder(item, screen)));

        menu.Host.PopupsDrawn.Clear();
        menu.Loop.Tasks.Render();
        Assert.Empty(menu.Host.PopupsDrawn);
    }

    [Fact]
    public void A_popup_box_grows_out_of_its_centre_and_is_full_size_at_half_the_fade()
    {
        DialogWindowTask popup = new() { X = 200, Y = 180, Width = 240, Height = 120 };

        MenuDialogPanel closed = MenuDialogPanel.For(popup, 0);
        Assert.True(closed.IsEmpty);
        Assert.Equal((320, 240, 320, 240), (closed.X0, closed.Y0, closed.X1, closed.Y1));

        MenuDialogPanel half = MenuDialogPanel.For(popup, MenuNavigator.FullAlpha / 4);
        Assert.Equal((260, 210, 380, 270, 220), (half.X0, half.Y0, half.X1, half.Y1, half.RuleY));

        foreach (int alpha in new[] { MenuNavigator.FullAlpha / 2, MenuNavigator.FullAlpha })
        {
            MenuDialogPanel open = MenuDialogPanel.For(popup, alpha);
            Assert.Equal((200, 180, 440, 300, 200, 1f), (open.X0, open.Y0, open.X1, open.Y1, open.RuleY, open.Alpha));
        }
    }

    private static bool IsUnder(MenuWindowTask item, MenuWindowTask root)
    {
        for (Engine.Tasks.GameTask? task = item; task is not null; task = task.Parent)
        {
            if (ReferenceEquals(task, root))
            {
                return true;
            }
        }

        return false;
    }

    private static void Tick(Harness harness)
    {
        for (int i = 0; i < 8; i++)
        {
            harness.Time.Advance(40);
            if (harness.Loop.RunFrame() == FrameOutcome.Tick)
            {
                return;
            }
        }
    }

    private static Harness Boot()
    {
        ManualTimeSource time = new();
        PointerHost host = new();

        // Off every control, so the pointer chooses nothing.
        host.MoveTo(-100, -100, down: false);
        GameLoop loop = new(time: time, host: host);
        GameMissionState missions = new();
        missions.Register(loop.Natives);

        MenuSystem? menu = null;
        host.OnShowMainMenu = () =>
        {
            menu = MenuSystem.Load(File.ReadAllBytes(GameData.Resolve("menusystem/mainmenu.qvm")), loop);
            menu.Manager.Configuration = new ConfigNatives(new GameConfig(), new StubConfigHost());
            menu.Manager.ExternalNatives = missions.TryInvokeFromMenu;
            menu.Manager.Initialize(menu, host);
        };

        loop.Flow.Request(FlowEvent.MainMenu);
        for (int i = 0; i < 64 && menu is null; i++)
        {
            time.Advance(40);
            loop.RunFrame();
        }

        return new Harness(loop, time, host, menu!);
    }

    private sealed record Harness(GameLoop Loop, ManualTimeSource Time, PointerHost Host, MenuSystem Menu);
}

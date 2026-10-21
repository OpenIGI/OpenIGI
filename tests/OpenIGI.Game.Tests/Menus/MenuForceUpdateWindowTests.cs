using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

/// <summary><c>MenuManager_ForceUpdateWindow</c> (<c>0x417880</c>): the window named takes its data in again.</summary>
public sealed class MenuForceUpdateWindowTests
{
    [RequiresGameDataFact]
    public void The_window_named_reads_its_value_again()
    {
        (GameLoop loop, MenuSystem menu) = Boot();

        // The graphics screen refreshes window 950 after the render device changes.
        MenuWindowTask window = Assert.IsAssignableFrom<MenuWindowTask>(loop.Tasks.Root.Tree.FromId(950));
        List<MenuWindowTask> controls = [window, .. Descendants(window)];
        Dictionary<MenuWindowTask, int> before = controls.OfType<ListBoxTask>().ToDictionary(static c => (MenuWindowTask)c, static c => c.ActiveId);
        Assert.NotEmpty(before);
        foreach (ListBoxTask list in controls.OfType<ListBoxTask>())
        {
            list.ActiveId = -12345;
        }

        menu.Manager.RunAction("MenuManager_ForceUpdateWindow(950)");

        foreach (ListBoxTask list in controls.OfType<ListBoxTask>())
        {
            Assert.Equal(before[list], list.ActiveId);
        }
    }

    private static IEnumerable<MenuWindowTask> Descendants(MenuWindowTask root)
    {
        foreach (MenuWindowTask child in root.Children.OfType<MenuWindowTask>())
        {
            yield return child;
            foreach (MenuWindowTask grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    private static (GameLoop Loop, MenuSystem Menu) Boot()
    {
        ManualTimeSource time = new();
        PointerHost host = new();
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

        return (loop, menu!);
    }
}

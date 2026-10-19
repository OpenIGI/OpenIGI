using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Menus;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// A click must fire once, even though the fade it starts takes several ticks.
/// </summary>
/// <remarks>
/// The interaction flags are recomputed on every tick — in the original by each control calling
/// <c>MenuItem_HitTest</c> from its own update, here by the manager doing it before the active gate.
/// Skipping it during a fade latches <c>WasClicked</c>, and a latched click re-runs the entry's
/// <c>On Click Script</c> every tick: the push that started the fade fires again, restarts the fade,
/// and the menu never becomes active again. Every symptom of that is the same one — the active gate
/// also gates the keys, the pointer and the cursor — so it presents as a freeze.
/// </remarks>
public sealed class MenuClickLatchTests
{
    [RequiresGameDataFact]
    public void AHeldClickDoesNotPushTheSameScreenEveryTick()
    {
        Harness harness = Boot();
        MenuManagerTask manager = harness.Menu.Manager;

        MenuTextTask entry = manager.Selection.Items
            .OfType<MenuTextTask>()
            .First(e => e.OnClickScript.Contains("PushScreen(", StringComparison.Ordinal));

        // Click it, and then keep the button down over it for far longer than the fade lasts.
        harness.Host.PressOver(entry);
        Tick(harness);
        harness.Host.ReleaseOver(entry);
        Tick(harness);

        int depth = manager.Navigator.Depth;
        Assert.True(depth > 0, "the click never fired, so the rest of this proves nothing");

        for (int i = 0; i < 60; i++)
        {
            Tick(harness);
        }

        // One click, one push — and the fade completes rather than being restarted.
        Assert.Equal(depth, manager.Navigator.Depth);
        Assert.Equal(MenuManagerState.Active, manager.Navigator.State);
    }

    [RequiresGameDataFact]
    public void TheMenuBecomesActiveAgainAfterNavigating()
    {
        Harness harness = Boot();
        MenuManagerTask manager = harness.Menu.Manager;

        manager.Navigator.Push(904);

        for (int i = 0; i < 60; i++)
        {
            Tick(harness);
        }

        Assert.Equal(MenuManagerState.Active, manager.Navigator.State);
        Assert.Equal(904, manager.CurrentScreen?.Id);

        // The cursor is drawn again, which is the visible half of the same gate.
        int drawn = harness.Host.PointerDraws;
        harness.Loop.Tasks.Render();
        Assert.True(harness.Host.PointerDraws > drawn);
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
        RecordingHost host = new();
        GameLoop loop = new(time: time, host: host);

        MenuSystem? menu = null;
        host.OnShowMainMenu = () =>
        {
            menu = MenuSystem.Load(File.ReadAllBytes(GameData.Resolve("menusystem/mainmenu.qvm")), loop);
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

    private sealed record Harness(GameLoop Loop, ManualTimeSource Time, RecordingHost Host, MenuSystem Menu);

    private sealed class RecordingHost : IFlowHost, IMenuHost
    {
        private int _x;
        private int _y;
        private bool _down;

        public Action? OnShowMainMenu { get; set; }

        public int PointerDraws { get; private set; }

        public void PressOver(MenuWindowTask window)
        {
            _x = window.LayoutX + (window.LayoutWidth / 2);
            _y = window.LayoutY + (window.LayoutHeight / 2);
            _down = true;
        }

        public void ReleaseOver(MenuWindowTask window)
        {
            _x = window.LayoutX + (window.LayoutWidth / 2);
            _y = window.LayoutY + (window.LayoutHeight / 2);
            _down = false;
        }

        public void ShowMainMenu() => OnShowMainMenu?.Invoke();

        public void ShowIntroScreens()
        {
        }

        public void ShowOutroScreens()
        {
        }

        public void ShowAttractScreens()
        {
        }

        public void BeginGame(FlowTask flow)
        {
        }

        public void LoadLevel()
        {
        }

        public void EndGame()
        {
        }

        public bool TryGetPointer(out int x, out int y, out bool buttonDown)
        {
            x = _x;
            y = _y;
            buttonDown = _down;
            return true;
        }

        public IEnumerable<MenuInputEvent> DrainEvents() => [];

        public void PrepareScreen(MenuScreenTask screen)
        {
            foreach (MenuTextTask entry in Descend(screen).OfType<MenuTextTask>())
            {
                entry.LayoutWidth = 80;
                entry.LayoutHeight = 16;
            }
        }

        public void DrawScreen(MenuScreenTask screen)
        {
        }

        public void DrawPointer(int x, int y) => PointerDraws++;

        public void PlaySound(string name)
        {
        }

        public void LeaveMenus(MenuManagerState state)
        {
        }

        private static IEnumerable<MenuWindowTask> Descend(MenuWindowTask window)
        {
            foreach (MenuWindowTask child in window.Children.OfType<MenuWindowTask>())
            {
                yield return child;
                foreach (MenuWindowTask descendant in Descend(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}

using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Menus;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// Boots the retail menu the way the original does: through the flow state machine, as a task under
/// the flow, driven by the frame walk.
/// </summary>
public sealed class MenuFlowBootTests
{
    [RequiresGameDataFact]
    public void TheFlowLoadsTheMenuAndTheFrameWalkDrivesIt()
    {
        Harness harness = Boot();

        // The flow asked for the menu, and the menu's tasks live under the flow task.
        Assert.True(harness.Host.MainMenuShown);
        Assert.Contains(harness.Menu.Manager, harness.Loop.Tasks.Root.Children);

        // The walk reaches the manager: it entered the screen the script named and measured it.
        Assert.NotNull(harness.Menu.Manager.CurrentScreen);
        Assert.Equal(harness.Menu.Manager.CurrentMenuScreen, harness.Menu.Manager.CurrentScreen!.Id);
        Assert.True(harness.Host.Prepared > 0);
    }

    [RequiresGameDataFact]
    public void RenderingGoesThroughTheTaskTreeRatherThanTheShell()
    {
        Harness harness = Boot();

        int before = harness.Host.Drawn;
        harness.Loop.Tasks.Render();

        Assert.True(harness.Host.Drawn > before);
    }

    [RequiresGameDataFact]
    public void TheSelectionIsBuiltWhenTheScreenIsEntered()
    {
        Harness harness = Boot();

        Assert.NotEmpty(harness.Menu.Manager.Selection.Items);
        Assert.NotNull(harness.Menu.Manager.Selection.Selected);
        Assert.True(harness.Menu.Manager.Selection.Selected!.IsHighlighted);
    }

    [RequiresGameDataFact]
    public void AKeyPressMovesTheSelectionThroughTheTaskUpdate()
    {
        Harness harness = Boot();
        MenuWindowTask? first = harness.Menu.Manager.Selection.Selected;

        harness.Host.Queue(MenuInputEvent.Next);
        RunUntilTick(harness);

        Assert.NotSame(first, harness.Menu.Manager.Selection.Selected);
        Assert.Contains("menu_high", harness.Host.Sounds);
    }

    [RequiresGameDataFact]
    public void ChoosingAnEntryRunsItsActionAndTheNavigatorMoves()
    {
        Harness harness = Boot();

        // Select "Play Project IGI" and press enter.
        MenuTextTask entry = harness.Menu.Manager.Selection.Items
            .OfType<MenuTextTask>()
            .First(e => e.OnClickScript.Contains("PushScreen(914)", StringComparison.Ordinal));

        harness.Menu.Manager.Selection.SelectItem(entry);
        harness.Host.Queue(MenuInputEvent.Enter);
        RunUntilTick(harness);

        Assert.Equal(914, harness.Menu.Manager.Navigator.CurrentScreen);
    }

    [RequiresGameDataFact]
    public void LeavingTheMenusReachesTheHost()
    {
        Harness harness = Boot();

        harness.Menu.Manager.Navigator.Leave(0, MenuManagerState.Quit);

        // Leaving fades out first and enters the leave state at the bottom of the fade, which is what
        // the original's "state 6, with the requested state parked beside it" does (0x417790).
        for (int i = 0; i < 40 && harness.Host.Left is null; i++)
        {
            RunUntilTick(harness);
        }

        Assert.Equal(MenuManagerState.Quit, harness.Host.Left);
    }

    private static void RunUntilTick(Harness harness)
    {
        for (int i = 0; i < 64; i++)
        {
            harness.Time.Advance(40);
            if (harness.Loop.RunFrame() == FrameOutcome.Tick)
            {
                return;
            }
        }

        Assert.Fail("No tick ran.");
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

        Assert.NotNull(menu);
        return new Harness(loop, time, host, menu!);
    }

    private sealed record Harness(GameLoop Loop, ManualTimeSource Time, RecordingHost Host, MenuSystem Menu);

    private sealed class RecordingHost : IFlowHost, IMenuHost
    {
        private readonly Queue<MenuInputEvent> _events = new();

        public Action? OnShowMainMenu { get; set; }

        public bool MainMenuShown { get; private set; }

        public int Prepared { get; private set; }

        public int Drawn { get; private set; }

        public List<string> Sounds { get; } = [];

        public MenuManagerState? Left { get; private set; }

        public void Queue(MenuInputEvent input) => _events.Enqueue(input);

        public void ShowMainMenu()
        {
            MainMenuShown = true;
            OnShowMainMenu?.Invoke();
        }

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

        // No pointer in the headless harness, so the keys drive everything.
        public bool TryGetPointer(out int x, out int y, out bool buttonDown)
        {
            x = 0;
            y = 0;
            buttonDown = false;
            return false;
        }

        public IEnumerable<MenuInputEvent> DrainEvents()
        {
            List<MenuInputEvent> drained = [];
            while (_events.Count > 0)
            {
                drained.Add(_events.Dequeue());
            }

            return drained;
        }

        public void PrepareScreen(MenuScreenTask screen)
        {
            Prepared++;

            // Give every entry a size so the line-up has something to place.
            foreach (MenuTextTask entry in Descend(screen).OfType<MenuTextTask>())
            {
                entry.LayoutWidth = 80;
                entry.LayoutHeight = 16;
            }
        }

        public void DrawScreen(MenuScreenTask screen) => Drawn++;

        public void DrawPointer(int x, int y) => PointerDrawn++;

        public int PointerDrawn { get; private set; }

        public void PlaySound(string name) => Sounds.Add(name);

        public void LeaveMenus(MenuManagerState state) => Left = state;

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

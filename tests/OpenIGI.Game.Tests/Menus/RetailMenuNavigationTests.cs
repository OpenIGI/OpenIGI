using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Expressions;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Menus;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// Drives the retail main menu end to end: load the script, lay it out, click an entry, and check the
/// manager went where the entry said.
/// </summary>
/// <remarks>
/// This is M5's acceptance criterion made headless. Everything here comes from the shipped data —
/// the entries, their labels, their positions and the action each one runs.
/// </remarks>
public sealed class RetailMenuNavigationTests
{
    [RequiresGameDataFact]
    public void ClickingAnEntryRunsItsActionAndMovesTheManager()
    {
        Harness menu = Load();

        // "Play Project IGI" is the first entry; its action is MenuManager_PushScreen(914).
        MenuTextTask entry = FindEntry(menu, "Single Player");
        Assert.Contains("MenuManager_PushScreen(914)", entry.OnClickScript, StringComparison.Ordinal);

        Click(menu, entry);

        Assert.Equal(914, menu.Navigator.CurrentScreen);
        Assert.Equal([900], menu.Navigator.SavedScreens);
        Assert.Equal(MenuManagerState.FadingOut, menu.Navigator.State);
    }

    [RequiresGameDataFact]
    public void TheFadeCompletesAndTheScreenTheEntryNamedExists()
    {
        Harness menu = Load();
        Click(menu, FindEntry(menu, "Single Player"));

        for (int i = 0; i < 64 && menu.Navigator.State != MenuManagerState.Active; i++)
        {
            menu.Navigator.Tick();
        }

        Assert.Equal(MenuManagerState.Active, menu.Navigator.State);
        Assert.NotNull(menu.System.FindScreen(menu.Navigator.CurrentScreen));
    }

    [RequiresGameDataFact]
    public void QuitRaisesThePopupTheScriptNames()
    {
        Harness menu = Load();

        MenuTextTask quit = FindEntry(menu, "Quit");
        Assert.Contains("ActivatePopupScreen(934)", quit.OnClickScript, StringComparison.Ordinal);

        Click(menu, quit);

        Assert.True(menu.Navigator.PopupActive);
        Assert.Equal(934, menu.Navigator.PopupScreen);
        // A popup is a DialogWindow, not a MenuScreen: the script declares twelve screens numbered
        // 900 and 904-916, and the ids the popup calls name are its six DialogWindow tasks.
        Assert.NotNull(menu.System.FindPopup(934));
        Assert.DoesNotContain(934, menu.System.Screens.Select(x => x.Id));
    }

    [RequiresGameDataFact]
    public void TheMainMenuUsesNoFrameBorders()
    {
        // A finding, not an omission: every MenuFrame in mainmenu.qvm has an empty Sprite Resource,
        // so the main menu is text on a background with no window chrome. The border code is exercised
        // by MenuBorderLayoutTests instead, since no shipped main-menu data reaches it.
        Harness menu = Load();

        MenuFrameTask[] frames = [.. menu.System.Screens.SelectMany(s => Descend(s).OfType<MenuFrameTask>())];
        Assert.NotEmpty(frames);
        Assert.All(frames, f => Assert.Empty(f.SpriteResource));
    }

    [RequiresGameDataFact]
    public void TheMainScreenIsABackgroundAndTextAndNothingElse()
    {
        // Closing an assumption rather than an omission. The main screen's background is mainmenu.pic
        // and its children are frames and text only - no PictureBox, and "logo" appears nowhere in
        // either menu script. logo.pic ships in menusystem.res but the main menu does not draw it, so
        // a shell that draws one is adding something the data does not ask for.
        Harness menu = Load();

        Assert.Equal("mainmenu.pic", menu.Screen.Background);
        Assert.All(
            Descend(menu.Screen),
            child => Assert.True(
                child is MenuFrameTask or MenuTextTask,
                $"unexpected {child.GetType().Name} on the main screen"));
    }

    [RequiresGameDataFact]
    public void EveryEntryOnTheMainScreenHasALabelAndAnAction()
    {
        Harness menu = Load();

        MenuTextTask[] entries = [.. Descend(menu.Screen).OfType<MenuTextTask>()];
        Assert.NotEmpty(entries);

        foreach (MenuTextTask entry in entries.Where(e => e.OnClickScript.Length > 0))
        {
            // A label that still reads "RM: ..." means the key did not resolve, which is a data or
            // wiring fault rather than something to paper over.
            string label = menu.Strings.Get(entry.TextResource);
            Assert.False(
                label.StartsWith("RM:", StringComparison.Ordinal),
                $"'{entry.TextResource}' did not resolve.");

            // And every action must parse.
            Assert.NotNull(ScriptExpressionParser.Parse(entry.OnClickScript, Constants));
        }
    }

    [RequiresGameDataFact]
    public void EntriesAreLaidOutInsideTheAuthoredScreenAndDoNotOverlap()
    {
        Harness menu = Load();

        MenuTextTask[] entries =
        [
            .. Descend(menu.Screen)
                .OfType<MenuTextTask>()
                .Where(e => e.LayoutWidth > 0 && e.LayoutHeight > 0)
                .OrderBy(e => e.LayoutY),
        ];

        Assert.NotEmpty(entries);

        foreach (MenuTextTask entry in entries)
        {
            Assert.InRange(entry.LayoutX, 0, MenuLayout.AuthoredWidth);
            Assert.InRange(entry.LayoutY, 0, MenuLayout.AuthoredHeight);
        }

        // Consecutive entries in a column must not sit on top of one another.
        for (int i = 1; i < entries.Length; i++)
        {
            if (entries[i].Parent == entries[i - 1].Parent)
            {
                Assert.True(
                    entries[i].LayoutY >= entries[i - 1].LayoutY + entries[i - 1].LayoutHeight,
                    $"'{entries[i].TextResource}' overlaps '{entries[i - 1].TextResource}'.");
            }
        }
    }

    private static void Click(Harness menu, MenuTextTask entry)
    {
        int x = entry.LayoutX + (entry.LayoutWidth / 2);
        int y = entry.LayoutY + (entry.LayoutHeight / 2);

        MenuPointer pointer = new();
        pointer.Update(x, y, MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight, buttonDown: true);
        menu.Screen.HitTestSubtree(pointer);

        pointer.Update(x, y, MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight, buttonDown: false);
        MenuWindowTask? clicked = menu.Screen.HitTestSubtree(pointer);

        Assert.Same(entry, clicked);
        menu.Evaluator.Evaluate(ScriptExpressionParser.Parse(entry.OnClickScript, Constants));
        Assert.False(menu.Evaluator.HasError, menu.Evaluator.ErrorMessage);
    }

    private static MenuTextTask FindEntry(Harness menu, string textResource) =>
        Descend(menu.Screen)
            .OfType<MenuTextTask>()
            .First(e => e.TextResource.Equals(textResource, StringComparison.OrdinalIgnoreCase));

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

    private static int? Constants(string name) => name switch
    {
        "TRUE" => 1,
        "FALSE" => 0,
        "MENUMANAGER_IDLE" => 9,
        "MENUMANAGER_QUIT" => 10,
        "MENUMANAGER_QUITTOMAINMENU" => 11,
        "MENUMANAGER_RESTARTLEVEL" => 12,
        "MENUMANAGER_STARTGAME" => 13,
        _ => null,
    };

    private static Harness Load()
    {
        VirtualFileSystem fileSystem = new(NullLogger.Instance);
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));

        MenuSystem system = MenuSystem.Load(File.ReadAllBytes(GameData.Resolve("menusystem/mainmenu.qvm")));

        ResourceRegistry resources = new(fileSystem, NullLogger.Instance);
        resources.Register(
            MenuStringTable.GetArchiveName(),
            File.ReadAllBytes(GameData.Resolve("language/english/menusystem.res")));
        MenuStringTable strings = new(resources);

        MenuScreenTask screen = system.GetInitialScreen() ?? system.Screens[0];

        // Measure with a fixed nominal glyph size rather than a real font: the line-up only needs
        // sizes, and this keeps the test independent of a graphics device.
        foreach (MenuTextTask entry in Descend(screen).OfType<MenuTextTask>())
        {
            string label = strings.Get(entry.TextResource);
            entry.LayoutWidth = label.Length * 8;
            entry.LayoutHeight = 16;
        }

        screen.Arrange();

        MenuNavigator navigator = new();
        navigator.Start(system.Manager.CurrentMenuScreen);

        return new Harness(system, screen, strings, navigator, new ScriptExpressionEvaluator(new MenuScriptContext(navigator)));
    }

    private sealed record Harness(
        MenuSystem System,
        MenuScreenTask Screen,
        MenuStringTable Strings,
        MenuNavigator Navigator,
        ScriptExpressionEvaluator Evaluator);
}

using OpenIGI.Game.Igi2.Config;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Menus;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The in-game menu (menumanager.c and its windows), as <c>ingamemenu.qsc</c> builds it over mission 11.</summary>
public sealed class RetailIgi2InGameMenuTests
{
    /// <summary>
    /// Opened, a single-player game's menu shows its pause screen and pauses; "Sound" pushes the sound page, whose music slider reads the
    /// profile's volume; moved and accepted, the volume is the profile's; Escape on the pause screen leaves the menu and the game plays.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_menu_opens_pushes_a_page_and_keeps_what_is_accepted()
    {
        (Igi2InGameMenu menu, Igi2LevelRun level, Igi2GameConfig config) = Load();
        bool paused = false;
        menu.SetPaused = value => paused = value;
        Assert.Equal(Igi2MenuStatus.Idle, menu.Status);

        menu.Open(level);
        Settle(menu, level);
        Assert.True(paused);
        Assert.Equal(901, menu.CurrentScreen?.Id);
        Assert.Equal(Igi2MenuStatus.Running, menu.Status);

        Igi2MenuTextTask sound = Texts(menu.CurrentScreen!).Single(t => t.Text == "Sound");
        Assert.True(sound.Click(level));
        Settle(menu, level);
        Assert.Equal(902, menu.CurrentScreen?.Id);
        Assert.Equal([901], menu.Stack);

        Igi2SlideBarTask music = Windows<Igi2SlideBarTask>(menu.CurrentScreen!).First();
        Assert.Equal(config.Active!.Music.Volume, (float)music.Value, 3);
        music.Modify(level, 0.25);
        Igi2MenuTextTask ok = Texts(menu.CurrentScreen!).First(t => t.Text == "OK");
        Assert.True(ok.Click(level));
        Settle(menu, level);
        Assert.Equal(0.25f, config.Active!.Music.Volume, 3);
        Assert.Equal(901, menu.CurrentScreen?.Id);
        Assert.Empty(menu.Stack);

        menu.PressEscape(level);
        Settle(menu, level);
        Assert.Equal(Igi2MenuStatus.Idle, menu.Status);
        Assert.False(menu.IsOpen);
        Assert.False(paused);
    }

    /// <summary>
    /// "Restart Game" opens its popup, whose yes asks the game to restart the level (<c>MENUMANAGER_RESTARTLEVEL</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Restart_asks_the_game_to_start_the_level_again()
    {
        (Igi2InGameMenu menu, Igi2LevelRun level, _) = Load();
        menu.Open(level);
        Settle(menu, level);
        Assert.True(Texts(menu.CurrentScreen!).Single(t => t.Text == "Restart Game").Click(level));
        Settle(menu, level);
        Assert.Equal(932, menu.PopupScreen?.Id);

        Igi2MenuTextTask yes = Texts(menu.PopupScreen!).First(t => t.OnClick.Contains("MENUMANAGER_RESTARTLEVEL", StringComparison.Ordinal));
        Assert.True(yes.Click(level));
        Settle(menu, level);
        Assert.Equal(Igi2MenuRequest.RestartLevel, menu.TakeRequest());
        Assert.Equal(Igi2MenuRequest.None, menu.TakeRequest());
    }

    [RequiresTitleDataFact("igi2")]
    public void The_manager_reads_a_controls_expression_in_this_games_dialect()
    {
        (Igi2InGameMenu menu, Igi2LevelRun level, _) = Load();
        Igi2MenuManagerTask manager = menu.Manager!;

        Assert.Equal(0.0, manager.EvaluateReal("1 + 2"));
        manager.Run(level);
        Assert.Equal(3.0, manager.EvaluateReal("1 + 2"));
        Assert.Equal(0.0, manager.EvaluateReal(string.Empty));
    }

    [RequiresTitleDataFact("igi2")]
    public void Every_parameter_the_level_declares_for_a_shared_window_reaches_the_class()
    {
        (Igi2InGameMenu menu, _, _) = Load();
        int checkedWindows = 0;
        foreach (Engine.Tasks.GameTask window in RetailIgi2Mission1.All(menu.Manager!)
            .Where(static t => t is Igi2MenuScreenTask or Igi2MenuFrameTask or Igi2DialogWindowTask))
        {
            foreach (Engine.Tasks.TaskParameter parameter in window.Type.Parameters)
            {
                Assert.True(window.TrySetScriptValue(parameter.Name, Value(window, parameter.Name)), $"{window.Type.Name} has no '{parameter.Name}' to set.");
            }

            checkedWindows++;
        }

        Assert.True(checkedWindows > 10, $"only {checkedWindows} windows were checked.");
        Assert.Contains(RetailIgi2Mission1.All(menu.Manager!).OfType<Igi2MenuScreenTask>(), static s => s.Background.Length > 0);

        static object? Value(Engine.Tasks.GameTask window, string name) =>
            window.TryGetScriptValue(name, out object? value) ? value : null;
    }

    private static (Igi2InGameMenu Menu, Igi2LevelRun Level, Igi2GameConfig Config) Load()
    {
        Igi2Level mission = Igi2Level.Find("location1", "level1");
        RetailIgi2LevelLoadTests.Harness harness = new(Path.Combine(mission.Directory, "objects.qvm"), loadTables: true);
        harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);
        Igi2InGameMenu menu = harness.Session.Services.Require<Igi2InGameMenu>();
        Assert.True(menu.Load(harness.LevelRoot, harness.Resources, harness.Loop.Natives));
        Igi2LevelRun level = new(harness.LevelRoot, harness.Loop.Natives, harness.Session.Services.Require<Scripting.Igi2ScriptFunctions>());
        return (menu, level, harness.Session.Services.Require<Igi2GameConfig>());
    }

    // Long enough for any fade.
    private static void Settle(Igi2InGameMenu menu, Igi2LevelRun level)
    {
        for (int tick = 0; tick < 40; tick++)
        {
            menu.Run(level);
        }
    }

    private static IEnumerable<Igi2MenuTextTask> Texts(Engine.Tasks.GameTask root) => Windows<Igi2MenuTextTask>(root);

    private static IEnumerable<T> Windows<T>(Engine.Tasks.GameTask root)
        where T : Engine.Tasks.GameTask => RetailIgi2Mission1.All(root).OfType<T>();
}

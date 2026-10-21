using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Expressions;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>The controls list (<c>0x4230A0</c>): a row clicked takes the next key held, and the screen's commit saves it.</summary>
public sealed class ControlsMenuTests
{
    private const int ControlsScreen = 907;
    private const int ControlsList = 954;
    private const int KeyG = 0x22;
    private const int Escape = 0x01;

    [Fact]
    public void The_slider_shows_the_offset_as_a_fraction_of_the_rows_it_scrolls_by_and_back()
    {
        MenuScriptContext context = new(new MenuNavigator());
        ScriptExpressionEvaluator evaluator = new(context);

        Assert.Equal(1.0, evaluator.EvaluateReal(Parse("ControlsMenu_ConvertFloatFromOffset(17)")), 6);
        Assert.Equal(1.0 / 17, evaluator.EvaluateReal(Parse("ControlsMenu_ConvertFloatFromOffset(1)")), 6);
        Assert.Equal(9, evaluator.EvaluateInt32(Parse("ControlsMenu_ConvertOffsetFromFloat(0.5)")));
        Assert.Equal(17, evaluator.EvaluateInt32(Parse("ControlsMenu_ConvertOffsetFromFloat(1.0)")));
    }

    [RequiresGameDataFact]
    public void A_clicked_row_takes_the_key_held_and_the_row_that_had_it_takes_its_old_one()
    {
        Harness menu = Open();
        ControlsMenuTask list = menu.List;

        // MoveLeft is A and Activate is E in a fresh profile; give MoveLeft E.
        Assert.Equal("KEY_A", list.KeyOf(0)?.Name);
        int activate = Row("Activate");
        Assert.Equal("KEY_E", list.KeyOf(activate)?.Name);

        Click(menu, 0);
        Assert.Equal(0, list.CapturingRow);
        Assert.Equal(MenuManagerState.Inactive, menu.Manager.Navigator.State);
        Assert.Equal(ControlsMenuTask.Prompt, list.KeyTextOf(0));
        Assert.Contains(ControlsMenuTask.ChooseSoundName, menu.Host.Sounds);

        menu.Host.HeldKeys.Add(0x12);
        Tick(menu);
        menu.Host.HeldKeys.Clear();

        Assert.False(list.IsCapturing);
        Assert.Equal(MenuManagerState.Active, menu.Manager.Navigator.State);
        Assert.Equal("KEY_E", list.KeyOf(0)?.Name);
        Assert.Equal("KEY_A", list.KeyOf(activate)?.Name);
        Assert.Matches("^menu_k0[1-7]$", menu.Host.Sounds[^1]);

        // Committing the screen writes the rows to the profile.
        list.SaveData();
        PlayerProfile profile = menu.Config.Active!;
        Assert.Contains(new InputBinding("MoveLeft", InputBindingKeyNames.KeyboardDevice, "KEY_E"), profile.Bindings);
        Assert.Contains(new InputBinding("Activate", InputBindingKeyNames.KeyboardDevice, "KEY_A"), profile.Bindings);
    }

    [RequiresGameDataFact]
    public void Escape_cancels_a_row_waiting_for_a_key_and_a_mouse_button_is_taken_as_held()
    {
        Harness menu = Open();
        ControlsMenuTask list = menu.List;

        Click(menu, 3);
        menu.Host.HeldKeys.Add(Escape);
        menu.Host.HeldKeys.Add(KeyG);
        Tick(menu);
        menu.Host.HeldKeys.Clear();

        Assert.False(list.IsCapturing);
        Assert.Equal("KEY_S", list.KeyOf(3)?.Name);
        Assert.Equal(ControlsMenuTask.CancelSoundName, menu.Host.Sounds[^1]);

        Click(menu, 3);
        menu.Host.MouseButtons = 0x4;
        Tick(menu);
        menu.Host.MouseButtons = 0;
        Assert.Equal("MOUSE_BUTTON_3", list.KeyOf(3)?.Name);
    }

    [RequiresGameDataFact]
    public void Resetting_puts_every_row_back_to_its_default()
    {
        Harness menu = Open();
        ControlsMenuTask list = menu.List;

        Click(menu, 0);
        menu.Host.HeldKeys.Add(KeyG);
        Tick(menu);
        menu.Host.HeldKeys.Clear();
        Assert.Equal("KEY_G", list.KeyOf(0)?.Name);

        // The reset popup's Yes: ControlsMenu_954.isReset = TRUE.
        menu.Manager.RunAction($"ControlsMenu_{ControlsList}.isReset = TRUE");
        Tick(menu);

        Assert.False(list.IsReset);
        Assert.Equal("KEY_A", list.KeyOf(0)?.Name);
    }

    [RequiresGameDataFact]
    public void A_key_posted_to_a_selected_options_row_reaches_the_toggle_in_it_and_lights_its_label()
    {
        Harness menu = Open();
        ToggleBoxTask invert = Assert.IsType<ToggleBoxTask>(menu.Manager.Tree.FromId(957));
        MenuFrameTask row = Assert.IsType<MenuFrameTask>(invert.Parent);
        MenuTextTask label = row.Children.OfType<MenuTextTask>().Single();

        // The selection carries the row, not the toggle in it (0x418840).
        Assert.Contains(row, menu.Manager.Selection.Items);
        Assert.DoesNotContain(invert, menu.Manager.Selection.Items);

        menu.Manager.Selection.SelectItem(row);
        bool before = invert.IsOn;
        row.PendingCommand = before ? MenuWindowCommand.Right : MenuWindowCommand.Left;
        Tick(menu);

        Assert.Equal(!before, invert.IsOn);
        Assert.Equal(MenuWindowCommand.None, row.PendingCommand);
        Assert.True(label.IsHighlighted);
    }

    private static int Row(string action) => ControlsMenuTask.Actions.ToList().IndexOf(action);

    private static ScriptExpression Parse(string source) => ScriptExpressionParser.Parse(source, MenuManagerTask.ResolveConstant);

    private static void Click(Harness menu, int band)
    {
        ControlsMenuTask list = menu.List;
        int x = list.LayoutX + 100;
        int y = list.LayoutY + (band * ControlsMenuTask.RowHeight) + 10;
        menu.Host.MoveTo(x, y, down: true);
        Tick(menu);
        menu.Host.MoveTo(x, y, down: false);
        Tick(menu);
        menu.Host.MoveTo(-100, -100, down: false);
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

    private static Harness Open()
    {
        ManualTimeSource time = new();
        PointerHost host = new();
        host.MoveTo(-100, -100, down: false);
        GameLoop loop = new(time: time, host: host);
        GameConfig config = GameConfig.CreateDefault();

        MenuSystem? menu = null;
        host.OnShowMainMenu = () =>
        {
            menu = MenuSystem.Load(File.ReadAllBytes(GameData.Resolve("menusystem/mainmenu.qvm")), loop);
            menu.Manager.Configuration = new ConfigNatives(config, new StubConfigHost());
            menu.Manager.Initialize(menu, host);
        };

        loop.Flow.Request(FlowEvent.MainMenu);
        for (int i = 0; i < 64 && menu is null; i++)
        {
            time.Advance(40);
            loop.RunFrame();
        }

        Harness harness = new(loop, time, host, menu!, config);
        harness.Manager.RunAction($"MenuManager_RequestScreen({ControlsScreen}, FALSE)");
        for (int i = 0; i < 64 && (harness.Manager.CurrentScreen?.Id != ControlsScreen || harness.Manager.Navigator.State != MenuManagerState.Active); i++)
        {
            Tick(harness);
        }

        Assert.Equal(ControlsScreen, harness.Manager.CurrentScreen?.Id);
        return harness;
    }

    private sealed record Harness(GameLoop Loop, ManualTimeSource Time, PointerHost Host, MenuSystem Menu, GameConfig Config)
    {
        public MenuManagerTask Manager => Menu.Manager;

        public ControlsMenuTask List => Assert.IsType<ControlsMenuTask>(Manager.Tree.FromId(ControlsList));
    }
}

using OpenIGI.Core.Content;
using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the running mission's own saves, reached from the pause menu.</summary>
[Collection(ShellTestHarness.CollectionName)]
public sealed class MissionSavesShellTests : ShellTestHarness
{
    [Fact]
    public void A_save_made_with_a_mod_that_is_off_loads_only_once_chosen_again()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Saves saves = new([new TitleSaveSlot(0, true, "0:12:00", [new ModReference("nightfall", "1.2")])]);
            host.MissionSaves = saves;
            Pump(shell, 0.1f);
            shell.OpenPauseMenu();
            Pump(shell, 0.6f);
            Find<Button>(shell.Stack.Top!, static b => b.Text == "LOAD GAME")!.Activate();
            Pump(shell, 0.6f);

            var screen = Assert.IsType<MissionSavesScreen>(shell.Stack.Top);
            SlotTile slot = screen.Root!.Descendants().OfType<SlotTile>().First();
            slot.Activate();
            Pump(shell, 0.1f);

            Assert.Equal(0, saves.Loads);
            Assert.Contains(screen.Root.Descendants().OfType<Label>(), static label => label.Text.Contains("nightfall 1.2", StringComparison.Ordinal));

            slot.Activate();
            Pump(shell, 0.1f);

            Assert.Equal(1, saves.Loads);
            Assert.Equal(1, host.ResumeCalls);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// A save clicked in the load list loads and resumes the mission on the press, with the button still down: the click is the
    /// page's, so the game handed back does not read the held button as its own -- the player's gun fired as the menu shut.
    /// </summary>
    [Fact]
    public void A_save_clicked_in_the_load_list_does_not_leave_the_button_down_for_the_game()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Saves saves = new([new TitleSaveSlot(0, true, "0:12:00")]);
            host.MissionSaves = saves;
            Pump(shell, 0.1f);
            shell.OpenPauseMenu();
            Pump(shell, 0.6f);
            Find<Button>(shell.Stack.Top!, static b => b.Text == "LOAD GAME")!.Activate();
            Pump(shell, 0.6f);

            var screen = Assert.IsType<MissionSavesScreen>(shell.Stack.Top);
            UiVec2 centre = screen.Root!.Descendants().OfType<SlotTile>().First().Bounds.Center;
            var input = new InputState();
            input.BeginFrame();
            input.Mouse.SetPosition(centre.X, centre.Y);
            input.Mouse.AddMotion(4f, 4f);
            input.Mouse.SetButton(MouseButton.Left, true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);

            Assert.Equal(1, saves.Loads);
            Assert.Equal(1, host.ResumeCalls);
            Assert.False(input.Mouse.IsDown(MouseButton.Left));

            input.BeginFrame();
            Assert.False(input.Mouse.IsDown(MouseButton.Left));
            input.Mouse.SetButton(MouseButton.Left, false);
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, true);
            Assert.True(input.Mouse.IsDown(MouseButton.Left));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// A load that starts the mission afresh -- Project I.G.I.'s -- is left to the host, which brings the mission up as a restart
    /// does: the screen does not resume the level that is being replaced.
    /// </summary>
    [Fact]
    public void A_load_that_restarts_the_mission_does_not_resume_it()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Saves saves = new([new TitleSaveSlot(0, true, "0:12:00")]) { Restarts = true };
            host.MissionSaves = saves;
            Pump(shell, 0.1f);
            shell.OpenPauseMenu();
            Pump(shell, 0.6f);
            Find<Button>(shell.Stack.Top!, static b => b.Text == "LOAD GAME")!.Activate();
            Pump(shell, 0.6f);

            var screen = Assert.IsType<MissionSavesScreen>(shell.Stack.Top);
            screen.Root!.Descendants().OfType<SlotTile>().First().Activate();
            Pump(shell, 0.1f);

            Assert.Equal(1, saves.Loads);
            Assert.Equal(0, host.ResumeCalls);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// A failed mission offers its newest save beside its restart, as I.G.I. 2's menu does: it is read onto the level that is
    /// up, which plays on.
    /// </summary>
    [Fact]
    public void The_failed_page_loads_the_last_save_and_plays_on()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Saves saves = new([new TitleSaveSlot(0, true, "0:12:00")]);
            host.MissionSaves = saves;
            Pump(shell, 0.1f);
            shell.OpenMissionFailed();
            Pump(shell, 0.6f);
            Find<Button>(shell.Stack.Top!, static b => b.Text == "LOAD LAST SAVE")!.Activate();
            Pump(shell, 0.1f);

            Assert.Equal(1, saves.QuickLoads);
            Assert.Equal(1, host.ResumeCalls);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private sealed class Saves(IReadOnlyList<TitleSaveSlot> slots) : ITitleSaveGames
    {
        public int Loads { get; private set; }

        public int QuickLoads { get; private set; }

        public bool QuickLoad()
        {
            QuickLoads++;
            return true;
        }

        public bool Restarts { get; init; }

        public bool LoadRestartsMission => Restarts;

        public IReadOnlyList<TitleSaveSlot> Slots => slots;

        public int SavesLeft => 0;

        public bool Save(int slot) => false;

        public bool Load(int slot)
        {
            Loads++;
            return true;
        }
    }
}

using OpenIGI.Core.Content;
using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Player;
using OpenIGI.UI.Kit.Widgets;
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
            Assert.Contains(screen.Root!.Descendants().OfType<Label>(), static label => label.Text.Contains("nightfall 1.2", StringComparison.Ordinal));

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

    private sealed class Saves(IReadOnlyList<TitleSaveSlot> slots) : ITitleSaveGames
    {
        public int Loads { get; private set; }

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

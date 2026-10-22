using OpenIGI.Frontend.Screens;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the profiles section: creating a profile, and deleting one with its saves.</summary>
[Collection(ShellTestHarness.CollectionName)]
public sealed class ProfilesShellTests : ShellTestHarness
{
    [Fact]
    public void Profile_workspace_shows_progress_and_creates_a_named_profile_at_the_selected_difficulty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Assert.True(shell.OpenOn("profiles"));
            Pump(shell, 0.1f);

            var screen = Assert.IsType<ProfilesScreen>(shell.Stack.Top);
            ProfileCard joe = screen.Root!.Descendants().OfType<ProfileCard>().Single(card => card.Visibility == Visibility.Visible);
            Assert.True(joe.Active);
            Assert.Equal("Joe", joe.Name);
            Assert.Contains("UNLOCKED", joe.Progress);
            Assert.Contains("Mission Two", joe.Mission);

            Find<Button>(screen, button => button.Text == "NEW PROFILE")!.Activate();
            Pump(shell, 0.1f); // lays out and focuses the form field
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Enter a profile name to continue."));
            Type(shell, "Alex");
            Find<Dropdown>(screen, dropdown => dropdown.Label == "Difficulty")!.SelectedIndex = 2;
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Pump(shell, 0.1f);

            ProfileCard alex = screen.Root!.Descendants().OfType<ProfileCard>().Single(card => card.Visibility == Visibility.Visible && card.Name == "Alex");
            Assert.True(alex.Active);
            Assert.Equal("Hard difficulty", alex.Difficulty);
            Assert.Equal(1, host.SaveProfilesCalls);

            Find<Button>(screen, button => button.Text == "NEW PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Type(shell, "aLeX");
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "That profile name is already in use."));
            Assert.Equal(1, host.SaveProfilesCalls);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Profile_deletion_requires_confirmation_removes_its_saves_and_keeps_the_last_profile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Assert.True(shell.OpenOn("profiles"));
            Pump(shell, 0.1f);
            var screen = Assert.IsType<ProfilesScreen>(shell.Stack.Top);

            Find<Button>(screen, button => button.Text == "NEW PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Type(shell, "Alex");
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "SAVE")!.Activate();

            Find<Button>(screen, button => button.Text == "DELETE PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "DELETE PERMANENTLY")!.Activate();
            Pump(shell, 0.1f);

            List<ProfileCard> visible = [.. screen.Root!.Descendants().OfType<ProfileCard>().Where(card => card.Visibility == Visibility.Visible)];
            Assert.Single(visible);
            Assert.Equal("Joe", visible[0].Name);
            Assert.All(screen.Root!.Descendants().OfType<SlotTile>(), slot => Assert.True(slot.IsEmpty));
            Assert.Equal(2, host.SaveProfilesCalls);
            Assert.False(Find<Button>(screen, button => button.Text == "DELETE PROFILE")!.IsEnabled);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

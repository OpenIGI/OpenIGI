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

            Find<Button>(screen, button => button.Text == "New profile")!.Activate();
            Pump(shell, 0.1f); // lays out and focuses the form field
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Enter a profile name to continue."));
            Type(shell, "Alex");
            Find<Dropdown>(screen, dropdown => dropdown.Label == "Difficulty")!.SelectedIndex = 2;
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Pump(shell, 0.1f);

            ProfileCard alex = screen.Root.Descendants().OfType<ProfileCard>().Single(card => card.Visibility == Visibility.Visible && card.Name == "Alex");
            Assert.True(alex.Active);
            Assert.Equal("Hard", alex.Difficulty);
            Assert.Equal(1, host.SaveProfilesCalls);

            Find<Button>(screen, button => button.Text == "New profile")!.Activate();
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

            Find<Button>(screen, button => button.Text == "New profile")!.Activate();
            Pump(shell, 0.1f);
            Type(shell, "Alex");
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "Save here")!.Activate();

            Find<Button>(screen, button => button.Text == "Delete")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "DELETE PERMANENTLY")!.Activate();
            Pump(shell, 0.1f);

            List<ProfileCard> visible = [.. screen.Root!.Descendants().OfType<ProfileCard>().Where(card => card.Visibility == Visibility.Visible)];
            Assert.Single(visible);
            Assert.Equal("Joe", visible[0].Name);
            Assert.All(screen.Root.Descendants().OfType<SlotTile>(), slot => Assert.True(slot.IsEmpty));
            Assert.Equal(2, host.SaveProfilesCalls);
            Assert.False(Find<Button>(screen, button => button.Text == "Delete")!.IsEnabled);
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
    public void Profile_rename_keeps_its_saves_and_refuses_an_empty_or_taken_name()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Assert.True(shell.OpenOn("profiles"));
            Pump(shell, 0.1f);
            var screen = Assert.IsType<ProfilesScreen>(shell.Stack.Top);

            Find<Button>(screen, button => button.Text == "New profile")!.Activate();
            Pump(shell, 0.1f);
            Type(shell, "Alex");
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "Save here")!.Activate();

            // The box opens holding the name, so what is typed is added to it: Alex and "Joe" is nobody's name yet...
            Find<Button>(screen, button => button.Text == "Rename")!.Activate();
            Pump(shell, 0.1f);
            TextBox box = Find<TextBox>(screen, field => field.Id == "profile-rename")!;
            Assert.Equal("Alex", box.Text);

            // ...but Joe alone is the other profile's, whatever its case, and an empty name is none.
            box.Text = "joe";
            Find<Button>(screen, button => button.Text == "RENAME PROFILE")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "That profile name is already in use."));
            box.Text = "   ";
            Find<Button>(screen, button => button.Text == "RENAME PROFILE")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Enter a profile name to continue."));
            Assert.Equal(1, host.SaveProfilesCalls);

            box.Text = "Anya";
            Find<Button>(screen, button => button.Text == "RENAME PROFILE")!.Activate();
            Pump(shell, 0.1f);

            ProfileCard renamed = screen.Root!.Descendants().OfType<ProfileCard>().Single(card => card.Visibility == Visibility.Visible && card.Active);
            Assert.Equal("Anya", renamed.Name);
            Assert.Equal(2, host.SaveProfilesCalls);
            Assert.DoesNotContain(screen.Root.Descendants().OfType<ProfileCard>(), card => card.Visibility == Visibility.Visible && card.Name == "Alex");

            // Its save is still its own, under the new name.
            Assert.Single(screen.Root.Descendants().OfType<SlotTile>(), slot => !slot.IsEmpty);
            var saves = new OpenIGI.Frontend.Saves.SaveSlotStore(dir);
            Assert.Contains(saves.Slots, slot => slot.ProfileName == "Anya");
            Assert.DoesNotContain(saves.Slots, slot => slot.ProfileName == "Alex");
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

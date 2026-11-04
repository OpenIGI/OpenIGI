using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>Wizards: Next held while a step has a problem, then on through the steps to the task.</summary>
public sealed class WizardTests
{
    /// <summary>Next does nothing while the step reports a problem, goes on once it does not, and the last step's Next does the task and closes the dialog.</summary>
    [Fact]
    public void Next_waits_for_the_step_then_finishes()
    {
        EditorHarness ui = new();
        ui.Frame();
        bool ready = false;
        int finished = 0;
        Wizard wizard = new(
            ui.Theme,
            [
                new WizardPage("One", "First", "The first step.", new OptionCards(ui.Theme)) { Problem = () => ready ? null : "Not yet." },
                new WizardPage("Two", "Second", "The last step.", new OptionCards(ui.Theme)),
            ],
            "Do It",
            () =>
            {
                finished++;
                return true;
            });
        Dialog dialog = Dialog.Open(ui.Root, "Test", wizard, 600f, 400f);
        wizard.Finished += dialog.Close;
        ui.Frame();
        UiVec2 next = new(dialog.Bounds.Right - ui.Theme.Px(40f), dialog.Bounds.Bottom - ui.Theme.Px(24f));

        ui.Click(next);
        Assert.Equal(0, wizard.Current);

        ready = true;
        ui.Frame();
        ui.Click(next);
        Assert.Equal(1, wizard.Current);

        ui.Frame();
        ui.Click(next);
        Assert.Equal(1, finished);
        Assert.Empty(ui.Root.Popups);
    }

    /// <summary>A step that leaves only sometimes goes on when it does not leave, and closes the wizard when it does.</summary>
    [Fact]
    public void A_step_that_does_not_leave_goes_on()
    {
        EditorHarness ui = new();
        ui.Frame();
        bool leaving = false;
        Wizard wizard = new(
            ui.Theme,
            [
                new WizardPage("One", "First", "Maybe leaves.", new OptionCards(ui.Theme)) { Leave = () => leaving },
                new WizardPage("Two", "Second", "The last step.", new OptionCards(ui.Theme)),
            ],
            "Do It",
            static () => true);
        int finished = 0;
        wizard.Finished += () => finished++;
        Dialog dialog = Dialog.Open(ui.Root, "Test", wizard, 600f, 400f);
        ui.Frame();
        UiVec2 next = new(dialog.Bounds.Right - ui.Theme.Px(40f), dialog.Bounds.Bottom - ui.Theme.Px(24f));

        ui.Click(next);
        Assert.Equal(1, wizard.Current);
        Assert.Equal(0, finished);

        wizard.Show(0);
        leaving = true;
        ui.Frame();
        ui.Click(next);
        Assert.Equal(1, finished);
    }
}

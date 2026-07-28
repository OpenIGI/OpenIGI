using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Menus;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// Member access against the retail script: <c>this.&lt;field&gt;</c> and
/// <c>TypeName_id.&lt;field&gt;</c>, in the types the controls actually declare.
/// </summary>
/// <remarks>
/// These run the expressions <c>mainmenu.qvm</c> ships, not paraphrases of them, because the point is
/// that the whole chain resolves — the script's task names, the tasks' exported variables, the
/// evaluator's type dispatch and the <c>this</c> binding.
/// </remarks>
public sealed class MenuMemberAccessTests
{
    [RequiresGameDataFact]
    public void EveryMemberPathTheRetailScriptNamesResolves()
    {
        MenuSystem menu = Load();
        ScriptVarRegistry variables = menu.Variables!;

        // Taken from the strings in mainmenu.qvm, which is the whole set of absolute paths it uses.
        string[] paths =
        [
            "ControlsMenu_954.isReset",
            "ControlsMenu_954.nOffset",
            "InputBox_963.zData",
            "ListBox_951.nActiveID",
            "ListBox_962.nActiveID",
            "MenuScreen_905.nUserData",
            "ScrollListBox_964.nActiveID",
            "ToggleBox_957.isOn",
        ];

        foreach (string path in paths)
        {
            Assert.True(variables.TryRead(path, out _), $"{path} did not resolve");
        }
    }

    [RequiresGameDataFact]
    public void TheExportedTypesMatchWhatTheOriginalRegisters()
    {
        MenuSystem menu = Load();
        ScriptVarRegistry variables = menu.Variables!;

        // A slider's carret is a double and its option index is an integer — SlideBar_ExportVars
        // (0x4200C0) binds them through different registrars, and the volume sliders depend on it.
        SlideBarTask slider = menu.Screens.SelectMany(Descend).OfType<SlideBarTask>().First();

        Assert.Equal(QvmValueKind.Float, variables.GetValueKind($"{slider.Name}.vValue"));
        Assert.Equal(QvmValueKind.Integer, variables.GetValueKind($"{slider.Name}.nValue"));
        Assert.Equal(QvmValueKind.String, variables.GetValueKind("InputBox_963.zData"));
        Assert.Equal(QvmValueKind.Integer, variables.GetValueKind("ToggleBox_957.isOn"));
    }

    [RequiresGameDataFact]
    public void ThisBindsToTheTaskWhoseExpressionIsRunning()
    {
        MenuSystem menu = Load();
        MenuManagerTask manager = menu.Manager;

        SlideBarTask[] sliders = [.. menu.Screens.SelectMany(Descend).OfType<SlideBarTask>()];
        Assert.True(sliders.Length >= 2, "expected more than one slider in the retail menu");

        manager.RunAction("this.vValue = 0.75", sliders[0]);

        // The write landed on the slider that owned the expression and on no other.
        Assert.Equal(0.75, sliders[0].Value);
        Assert.Equal(0.0, sliders[1].Value);
    }

    [RequiresGameDataFact]
    public void AFloatMemberKeepsItsFractionWhereAnIntegerMemberTruncates()
    {
        MenuSystem menu = Load();
        SlideBarTask slider = menu.Screens.SelectMany(Descend).OfType<SlideBarTask>().First();

        menu.Manager.RunAction("this.vValue = 3 / 4, this.nValue = 3 / 4", slider);

        Assert.Equal(0.75, slider.Value);
        Assert.Equal(0, slider.Position);
    }

    [RequiresGameDataFact]
    public void TheResetPopupsConfirmExpressionWritesBothControls()
    {
        MenuSystem menu = Load();

        // Verbatim from mainmenu.qvm, minus the trailing navigation call.
        menu.Manager.RunAction("ControlsMenu_954.isReset = TRUE, ToggleBox_957.isOn = FALSE");

        ControlsMenuTask controls = (ControlsMenuTask)menu.FindWindow(954)!;
        ToggleBoxTask toggle = (ToggleBoxTask)menu.FindWindow(957)!;

        Assert.True(controls.IsReset);
        Assert.False(toggle.IsOn);
    }

    [RequiresGameDataFact]
    public void AStringMemberIsTruncatedToTheOriginalsBufferRatherThanRejected()
    {
        MenuSystem menu = Load();
        InputBoxTask input = (InputBoxTask)menu.FindWindow(963)!;

        string tooLong = new('x', InputBoxTask.DataCapacity + 10);
        menu.Manager.RunAction($"InputBox_963.zData = \"{tooLong}\"");

        Assert.Equal(InputBoxTask.DataCapacity - 1, input.Data.Length);
    }

    [RequiresGameDataFact]
    public void AnUnknownMemberReadsZeroAndDoesNotStopTheExpression()
    {
        MenuSystem menu = Load();
        MenuScreenTask screen = menu.Screens[0];

        // The original reports the unknown identifier and carries on with zero, so the assignment
        // after the comma still runs.
        menu.Manager.RunAction("this.nNotAThing = 1, this.nUserData = 5", screen);

        Assert.Equal(5, screen.UserData);
    }

    private static MenuSystem Load()
    {
        GameLoop loop = new();
        MenuSystem menu = MenuSystem.Load(File.ReadAllBytes(GameData.Resolve("menusystem/mainmenu.qvm")), loop);
        menu.Manager.Initialize(menu, new SilentHost());
        return menu;
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

    /// <summary>A host that supplies nothing, so only the expression path is under test.</summary>
    private sealed class SilentHost : IMenuHost
    {
        public bool TryGetPointer(out int x, out int y, out bool buttonDown)
        {
            x = 0;
            y = 0;
            buttonDown = false;
            return false;
        }

        public IEnumerable<MenuInputEvent> DrainEvents() => [];

        public void PrepareScreen(MenuScreenTask screen)
        {
        }

        public void DrawScreen(MenuScreenTask screen)
        {
        }

        public void PlaySound(string name)
        {
        }

        public void LeaveMenus(MenuManagerState state)
        {
        }
    }
}

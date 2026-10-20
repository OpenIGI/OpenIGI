using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using Xunit;
using OpenIGI.Game.Menus;

namespace OpenIGI.Game.Tests.Menus;

public sealed class MenuNavigatorTests
{
    [Fact]
    public void PushSavesTheCurrentScreenAndReturnsThroughIt()
    {
        MenuNavigator nav = new();
        nav.Start(900);

        nav.Push(914);
        Assert.Equal(914, nav.CurrentScreen);
        Assert.Equal([900], nav.SavedScreens);

        RunFade(nav);
        Assert.Equal(MenuManagerState.Active, nav.State);

        Assert.True(nav.Pop(save: false));
        Assert.Equal(900, nav.CurrentScreen);
        Assert.Equal(0, nav.Depth);
    }

    [Fact]
    public void RequestReplacesTheScreenWithoutTouchingTheStack()
    {
        // The consequence is real: a screen reached by request cannot be popped back out of.
        MenuNavigator nav = new();
        nav.Start(900);

        nav.Request(904, save: false);

        Assert.Equal(904, nav.CurrentScreen);
        Assert.Equal(0, nav.Depth);
        Assert.False(nav.Pop(save: false));
    }

    [Fact]
    public void PushAndPopCarryDifferentReturnStates()
    {
        MenuNavigator nav = new();
        nav.Start(900);

        nav.Push(914);
        Assert.Equal(MenuManagerState.EnterScreen, nav.ReturnState);

        RunFade(nav);
        nav.Pop(save: false);
        Assert.Equal(MenuManagerState.ReturnToScreen, nav.ReturnState);
    }

    [Fact]
    public void EveryNavigationDropsIntoAFadeOutAndTheScreenChangesAtTheBottom()
    {
        MenuNavigator nav = new();
        nav.Start(900);
        Assert.Equal(MenuNavigator.FullAlpha, nav.Alpha);

        nav.Push(914);
        Assert.Equal(MenuManagerState.FadingOut, nav.State);

        // 16 down to 0 in steps of 2.
        for (int i = 0; i < MenuNavigator.FullAlpha / MenuNavigator.FadeStep; i++)
        {
            nav.Tick();
        }

        Assert.Equal(0, nav.Alpha);
        Assert.Equal(MenuManagerState.EnterScreen, nav.State);
    }

    [Fact]
    public void AFadeRunsAllTheWayBackUpToActive()
    {
        MenuNavigator nav = new();
        nav.Start(900);
        nav.Push(914);

        RunFade(nav);

        Assert.Equal(MenuManagerState.Active, nav.State);
        Assert.Equal(MenuNavigator.FullAlpha, nav.Alpha);
    }

    [Fact]
    public void PoppingAnEmptyStackIsRefusedRatherThanReadingBelowIt()
    {
        // The one deliberate divergence: the original decrements unconditionally. Data that balances
        // its pushes and pops cannot tell the difference.
        MenuNavigator nav = new();
        nav.Start(900);

        Assert.False(nav.Pop(save: false));
        Assert.Equal(900, nav.CurrentScreen);
        Assert.Equal(MenuManagerState.Active, nav.State);
    }

    [Fact]
    public void PopupsLayOverTheCurrentScreenWithoutReplacingIt()
    {
        // 0x417900 enters state 14 directly: the screen stays, fully shown, while the popup fades in over it.
        MenuNavigator nav = new();
        nav.Start(900);

        nav.ActivatePopup(934);
        Assert.True(nav.PopupActive);
        Assert.Equal(934, nav.PopupScreen);
        Assert.Equal(MenuManagerState.ActivatePopup, nav.State);

        Assert.Equal(MenuStep.ActivatePopup, nav.Tick());
        RunFade(nav);
        Assert.Equal(900, nav.CurrentScreen);
        Assert.Equal(MenuNavigator.FullAlpha, nav.Alpha);
        Assert.Equal(MenuNavigator.FullAlpha, nav.PopupAlpha);

        // 0x417960 fades the popup out to state 15; the popup goes at the bottom of its fade.
        nav.DeactivatePopup();
        Assert.Equal(MenuManagerState.PopupFadingOut, nav.State);
        Assert.True(nav.PopupActive);
        while (nav.State == MenuManagerState.PopupFadingOut)
        {
            nav.Tick();
        }

        Assert.False(nav.PopupActive);
        Assert.Equal(MenuNavigator.FullAlpha, nav.Alpha);
        Assert.Equal(MenuManagerState.PopupGone, nav.State);
        nav.Tick();
        Assert.Equal(MenuManagerState.Active, nav.State);
        Assert.Equal(900, nav.CurrentScreen);
    }

    [Fact]
    public void APopReturnsWithoutTheScreenTakingItsDataIn()
    {
        // 0x417710 parks state 1, which 0x418710 opens without the init-data pass a push's state 0 runs.
        MenuNavigator nav = new();
        nav.Start(900);
        nav.Push(914);
        Assert.Equal(MenuStep.ActivateScreen, TickToOpen(nav));
        RunFade(nav);

        nav.Pop(save: false);

        Assert.Equal(MenuStep.ReturnToScreen, TickToOpen(nav));
    }

    [Fact]
    public void TheLanguageFadesOutAndBackWithoutChangingTheScreen()
    {
        MenuNavigator nav = new();
        nav.Start(900);

        nav.SetLanguage();

        Assert.Equal(MenuStep.ChangeLanguage, TickToOpen(nav));
        Assert.Equal(900, nav.CurrentScreen);
    }

    [Fact]
    public void AFailedLevelKeepsItsMenuOpen()
    {
        MenuNavigator nav = new();
        nav.Start(900);

        nav.Leave(0, MenuManagerState.Idle, levelFailed: true);
        Assert.Equal(MenuManagerState.Active, nav.State);

        nav.Leave(0, MenuManagerState.Idle);
        Assert.Equal(MenuManagerState.FadingOut, nav.State);
        Assert.Equal(MenuManagerState.Idle, nav.ReturnState);
    }

    [Fact]
    public void IGI2sLeaveGoesBackAPushedScreenFirst()
    {
        // MenuManager_LeaveMenues pops while there is a screen to pop, and leaves only from the bottom; Project I.G.I.'s leaves.
        MenuNavigator igi2 = new(MenuManagerDialect.Igi2);
        igi2.Start(901);
        igi2.Push(902);
        RunFade(igi2);

        igi2.Leave(901, MenuManagerState.Idle);

        Assert.Equal(901, igi2.CurrentScreen);
        Assert.Equal(MenuManagerState.ReturnToScreen, igi2.ReturnState);

        MenuNavigator igi1 = new();
        igi1.Start(901);
        igi1.Push(902);
        RunFade(igi1);

        igi1.Leave(-1, MenuManagerState.StartGame);

        Assert.Equal(MenuManagerState.StartGame, igi1.ReturnState);
        Assert.Equal(1, igi1.Depth);
    }

    [Fact]
    public void EachGameNumbersTheStatesItsScriptsName()
    {
        // I.G.I. 2 numbers an already-shown activation at 2 and the join at 15, so idle is 10 there and 9 here.
        Assert.Equal(9, MenuManagerDialect.Igi1.ValueOf(MenuManagerState.Idle));
        Assert.Equal(10, MenuManagerDialect.Igi2.ValueOf(MenuManagerState.Idle));
        Assert.Equal(14, MenuManagerDialect.Igi2.ValueOf(MenuManagerState.StartGame));
        Assert.Equal(15, MenuManagerDialect.Igi2.ValueOf(MenuManagerState.JoinGame));
        Assert.Equal(16, MenuManagerDialect.Igi2.ValueOf(MenuManagerState.ActivatePopup));
        Assert.Equal(-1, MenuManagerDialect.Igi1.ValueOf(MenuManagerState.JoinGame));
        Assert.Equal(MenuManagerState.QuitToMainMenu, MenuManagerDialect.Igi2.StateOf(12));
    }

    [Fact]
    public void TheMainMenuAndRestartLeavesAskOnceAndGoIdle()
    {
        MenuNavigator nav = new();
        nav.Start(900);
        nav.Leave(0, MenuManagerState.RestartLevel);
        while (nav.State == MenuManagerState.FadingOut)
        {
            nav.Tick();
        }

        Assert.Equal(MenuStep.Leave, nav.Tick());
        Assert.Equal(MenuManagerState.RestartLevel, nav.Left);
        Assert.Equal(MenuManagerState.Idle, nav.State);
        Assert.Equal(MenuStep.None, nav.Tick());
    }

    [Fact]
    public void TheLeaveStatesAreTheOnesTheScriptNamesByConstant()
    {
        Assert.Equal(9, (int)MenuManagerState.Idle);
        Assert.Equal(10, (int)MenuManagerState.Quit);
        Assert.Equal(11, (int)MenuManagerState.QuitToMainMenu);
        Assert.Equal(12, (int)MenuManagerState.RestartLevel);
        Assert.Equal(13, (int)MenuManagerState.StartGame);
    }

    [Fact]
    public void TheRetailMainMenuActionsDriveTheStack()
    {
        // Every expression here is verbatim from mainmenu.qvm.
        MenuNavigator nav = new();
        nav.Start(900);
        MenuScriptContext context = new(nav);
        ScriptExpressionEvaluator evaluator = new(context);

        Run(evaluator, "MenuManager_PushScreen(914)");
        Assert.Equal(914, nav.CurrentScreen);
        RunFade(nav);

        Run(evaluator, "MenuManager_PopScreen(FALSE)");
        Assert.Equal(900, nav.CurrentScreen);
        RunFade(nav);

        Run(evaluator, "MenuManager_ActivatePopupScreen(934)");
        Assert.True(nav.PopupActive);
        Assert.Equal(934, nav.PopupScreen);
    }

    [Fact]
    public void NavigationNativesReturnOneSoSequencedActionsBothRun()
    {
        // "MenuManager_RequestScreen(904, FALSE) && Game_UpdateVolume()" relies on this.
        MenuNavigator nav = new();
        nav.Start(900);

        List<string> fallbackCalls = [];
        MenuScriptContext context = new(nav) { Fallback = (name, _) => { fallbackCalls.Add(name); return QvmValue.FromInt32(1); } };
        ScriptExpressionEvaluator evaluator = new(context);

        Run(evaluator, "MenuManager_RequestScreen(904, FALSE) && Game_UpdateVolume()");

        Assert.Equal(904, nav.CurrentScreen);
        Assert.Equal(["Game_UpdateVolume"], fallbackCalls);
    }

    [Fact]
    public void SetEnabledAndLeaveMenusAreRouted()
    {
        MenuNavigator nav = new();
        nav.Start(900);
        MenuScriptContext context = new(nav);
        ScriptExpressionEvaluator evaluator = new(context);

        Run(evaluator, "MenuManager_SetEnabled(FALSE)");
        Assert.False(context.IsEnabled);

        // The state is the SECOND argument — retail scripts read MenuManager_LeaveMenus(0,
        // MENUMANAGER_QUIT) and (-1, MENUMANAGER_STARTGAME). This test had the pair the wrong way round,
        // which is how reading argument 0 as the state survived: -1 then threw "Not a leave state".
        Run(evaluator, "MenuManager_LeaveMenus(0, MENUMANAGER_QUIT)");

        // Leaving fades out first; the leave state is entered at the bottom of the fade.
        Assert.Equal(MenuManagerState.FadingOut, nav.State);
        while (nav.State == MenuManagerState.FadingOut)
        {
            nav.Tick();
        }

        Assert.True(nav.IsLeaving);
        Assert.Equal(MenuManagerState.Quit, nav.State);
    }

    private static void Run(ScriptExpressionEvaluator evaluator, string source) =>
        evaluator.Evaluate(ScriptExpressionParser.Parse(source, Constants));

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

    private static MenuStep TickToOpen(MenuNavigator nav)
    {
        for (int i = 0; i < 64; i++)
        {
            MenuStep step = nav.Tick();
            if (step is not (MenuStep.None or MenuStep.FadedOut))
            {
                return step;
            }
        }

        return MenuStep.None;
    }

    private static void RunFade(MenuNavigator nav)
    {
        for (int i = 0; i < 64 && nav.State != MenuManagerState.Active; i++)
        {
            nav.Tick();
        }
    }
}

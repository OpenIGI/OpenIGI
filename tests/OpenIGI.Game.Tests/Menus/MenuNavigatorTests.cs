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
        MenuNavigator nav = new();
        nav.Start(900);

        nav.ActivatePopup(934);
        Assert.True(nav.PopupActive);
        Assert.Equal(934, nav.PopupScreen);
        Assert.Equal(900, nav.CurrentScreen);

        nav.DeactivatePopup();
        Assert.False(nav.PopupActive);
        Assert.Equal(900, nav.CurrentScreen);
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

    private static void RunFade(MenuNavigator nav)
    {
        for (int i = 0; i < 64 && nav.State != MenuManagerState.Active; i++)
        {
            nav.Tick();
        }
    }
}

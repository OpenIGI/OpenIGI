using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Config;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Config;
using OpenIGI.Game.Igi1.Menus;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// The Select Mission screen (914), driven through the retail script with a stub mission list.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a bug that only shows up at this level. Making the briefing picture and
/// description follow the list by <em>reloading the screen's data</em> looked right and even appeared to
/// work — the boxes are built before the list, so they were visited first and did update. But the reload
/// then re-ran the <em>list's own</em> <c>Get Data Script</c>, which is
/// <c>Config_PlayerGetActiveMission(...)</c>, and snapped the selection straight back to the profile's
/// mission. The highlight never moved and the screen wedged.
/// </para>
/// <para>
/// The original reloads nothing when a row changes (<c>ScrollListBox_Update</c>, <c>0x41BDD0</c>): the
/// dependent controls re-evaluate their own expression every update. These tests pin that.
/// </para>
/// </remarks>
public sealed class MenuMissionSelectTests
{
    [RequiresGameDataFact]
    public void ClickingARowMovesTheSelectionAndKeepsIt()
    {
        Harness harness = Boot();
        ScrollListBoxTask list = MissionList(harness);

        Assert.True(list.Items.Count >= 3, "the stub host did not fill the list");

        // The screen opens whichever row the list's Get Data Script names, so move off it deliberately
        // rather than assuming which one that is — clicking the row already selected proves nothing.
        int opened = list.Index;
        int target = opened == 2 ? 0 : 2;

        ClickRow(harness, list, target);

        Assert.Equal(target, list.Index);
        Assert.Equal(list.Items[target].Id, list.ActiveId);

        // The reload bug showed up here: a tick later the selection was back where it opened, because
        // reloading the screen re-ran this list's own Get Data Script.
        for (int i = 0; i < 10; i++)
        {
            Tick(harness);
        }

        Assert.Equal(target, list.Index);
        Assert.Equal(list.Items[target].Id, list.ActiveId);
    }

    [RequiresGameDataFact]
    public void TheSelectionCanKeepMoving()
    {
        Harness harness = Boot();
        ScrollListBoxTask list = MissionList(harness);

        ClickRow(harness, list, 0);
        ClickRow(harness, list, 1);
        ClickRow(harness, list, 2);
        ClickRow(harness, list, 1);

        Assert.Equal(1, list.Index);
    }

    [RequiresGameDataFact]
    public void TheBriefingPictureAndTextFollowTheSelection()
    {
        Harness harness = Boot();
        ScrollListBoxTask list = MissionList(harness);

        PictureBoxTask picture = Find<PictureBoxTask>(harness);
        TextBoxTask text = Find<TextBoxTask>(harness);

        ClickRow(harness, list, 0);
        Tick(harness);
        string firstPicture = picture.Picture;
        string firstText = string.Join('\n', text.Lines);

        ClickRow(harness, list, 2);
        Tick(harness);

        Assert.NotEqual(firstPicture, picture.Picture);
        Assert.NotEqual(firstText, string.Join('\n', text.Lines));
        Assert.Equal(StubConfigHost.PictureFor(list.Items[2].Id), picture.Picture);
    }

    [RequiresGameDataFact]
    public void OkStartsTheSelectedMission()
    {
        Harness harness = Boot();
        ScrollListBoxTask list = MissionList(harness);

        ClickRow(harness, list, 1);
        int chosen = list.ActiveId;

        // The OK entry runs Game_SetMission(ScrollListBox_964.nActiveID) followed by
        // MenuManager_LeaveMenus(-1, MENUMANAGER_STARTGAME). Reading the *first* argument as the state
        // threw "Not a leave state" for -1, which is what starting a mission used to do.
        MenuTextTask ok = Descendants(harness.Menu.Manager.CurrentScreen!)
            .OfType<MenuTextTask>()
            .First(e => e.OnClickScript.Contains("MENUMANAGER_STARTGAME", StringComparison.Ordinal));

        Assert.True(ok.IsInteractive, "the OK entry is not clickable");
        Assert.True(ok.LayoutWidth > 0 && ok.LayoutHeight > 0, "the OK entry has no measured size");

        int okX = ok.LayoutX + (ok.LayoutWidth / 2);
        int okY = ok.LayoutY + (ok.LayoutHeight / 2);

        harness.Host.MoveTo(okX, okY, down: true);
        Tick(harness);
        harness.Host.MoveTo(okX, okY, down: false);
        Tick(harness);

        Assert.Equal(chosen, harness.Missions.PendingMissionId);

        // Leaving fades out first and only then enters the leave state, which is what the original's
        // state 6 plus a parked state does.
        Assert.Equal(MenuManagerState.FadingOut, harness.Menu.Manager.Navigator.State);
        Assert.Equal(-1, harness.Menu.Manager.Navigator.LeaveParameter);

        for (int i = 0; i < 40 && harness.Host.LeftWith is null; i++)
        {
            Tick(harness);
        }

        Assert.Equal(MenuManagerState.StartGame, harness.Host.LeftWith);

        // Once, not once per tick. Leaving is a request; nothing transitions out of a leave state, so
        // without clearing it the StartGame handler reloaded the whole mission every tick.
        int delivered = harness.Host.LeaveCount;
        for (int i = 0; i < 20; i++)
        {
            Tick(harness);
        }

        Assert.Equal(delivered, harness.Host.LeaveCount);
    }

    private static ScrollListBoxTask MissionList(Harness harness) => Find<ScrollListBoxTask>(harness);

    private static T Find<T>(Harness harness)
        where T : MenuWindowTask =>
        Descendants(harness.Menu.Manager.CurrentScreen!).OfType<T>().First();

    private static IEnumerable<MenuWindowTask> Descendants(MenuWindowTask root)
    {
        foreach (MenuWindowTask child in root.Children.OfType<MenuWindowTask>())
        {
            yield return child;
            foreach (MenuWindowTask nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static void ClickRow(Harness harness, ScrollListBoxTask list, int visibleRow)
    {
        int y = list.LayoutY + (visibleRow * Math.Max(1, list.RowHeight)) + 1;

        harness.Host.MoveTo(list.LayoutX + 4, y, down: true);
        Tick(harness);
        harness.Host.MoveTo(list.LayoutX + 4, y, down: false);
        Tick(harness);
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

    private static Harness Boot()
    {
        ManualTimeSource time = new();
        PointerHost host = new();
        GameLoop loop = new(time: time, host: host);

        GameMissionState missions = new();
        missions.Register(loop.Natives);

        MenuSystem? menu = null;
        host.OnShowMainMenu = () =>
        {
            menu = MenuSystem.Load(File.ReadAllBytes(GameData.Resolve("menusystem/mainmenu.qvm")), loop);
            menu.Manager.Configuration = new ConfigNatives(new GameConfig(), new StubConfigHost());
            menu.Manager.ExternalNatives = missions.TryInvokeFromMenu;
            menu.Manager.CurrentMenuScreen = 914;
            menu.Manager.Initialize(menu, host);
        };

        loop.Flow.Request(FlowEvent.MainMenu);

        for (int i = 0; i < 64 && menu is null; i++)
        {
            time.Advance(40);
            loop.RunFrame();
        }

        Harness harness = new(loop, time, host, menu!, missions);
        for (int i = 0; i < 30; i++)
        {
            Tick(harness);
        }

        return harness;
    }

    private sealed record Harness(
        GameLoop Loop,
        ManualTimeSource Time,
        PointerHost Host,
        MenuSystem Menu,
        GameMissionState Missions);
}

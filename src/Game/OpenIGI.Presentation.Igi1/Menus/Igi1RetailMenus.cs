using OpenIGI.Game.Igi1.Config;
using OpenIGI.Game.Igi1.Menus;

namespace OpenIGI.Presentation.Igi1.Menus;

/// <summary>Project I.G.I.'s retail menus: <c>mainmenu.qvm</c> run in the loop and drawn by the menu renderer.</summary>
/// <remarks>
/// The original's <c>MenuSystem_Load(root, "LOCAL:menusystem\mainmenu.qsc", 1, 1, 1)</c>. The mission briefing images are
/// in their own archive beside the menu's.
/// </remarks>
internal sealed class Igi1RetailMenus : IRetailMenus
{
    private readonly RetailMenuHost _shell;

    /// <summary>Initializes a new instance of the <see cref="Igi1RetailMenus"/> class, and starts the menu script.</summary>
    /// <param name="request">What the menus are started with.</param>
    public Igi1RetailMenus(in RetailMenusRequest request)
    {
        MenuSystem menu = MenuSystem.Load(
            request.FileSystem.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"),
            request.Loop,
            request.Logger);

        Action<bool> leave = request.Leave;
        _shell = new RetailMenuHost(new RetailMenuPresentationRequest(
            request.Device,
            request.FileSystem,
            menu.Manager.ResourcePath,
            request.Strings() ?? throw new InvalidOperationException("The language's text is loaded before the menus start."),
            ["LOCAL:menusystem/missionsprites.res"],
            request.Input,
            request.Batch,
            request.ScreenWidth,
            request.ScreenHeight,
            request.Scale,
            request.Sounds,
            // Leaving with StartGame starts the chosen mission; leaving any other way quits.
            leaving => leave(leaving == MenuManagerState.StartGame),
            request.Logger));

        // -screen is a development shortcut, not a game feature: the script decides where the menu
        // opens, and this overwrites that one parameter before the manager reads it.
        if (request.StartScreen > 0)
        {
            menu.Manager.CurrentMenuScreen = request.StartScreen;
        }

        RetailMenuDisplay display = request.Display;
        menu.Manager.Configuration = new ConfigNatives(
            request.Configuration,
            new MenuConfigHost(display.RendererName, display.DisplayModes, display.ApplyResolution, request.Missions, request.Strings));

        // Game_SetMission belongs to the game, not the menu system, and a menu expression is evaluated
        // by the menu's own evaluator rather than by the script VM -- so registering it with the VM is
        // not enough to make the Select Mission button or the music slider work.
        menu.Manager.ExternalNatives = request.ExternalNatives;
        menu.Manager.Initialize(menu, _shell, request.Logger);
    }

    /// <inheritdoc/>
    public void CloseScreens() => _shell.CloseScreens();

    /// <inheritdoc/>
    public void Dispose() => _shell.Dispose();
}

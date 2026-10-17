using OpenIGI.Engine.Flow;
using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Config;
using OpenIGI.Game.Igi1.Menus;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>A menu host whose pointer can be placed anywhere, not just over a control's centre.</summary>
/// <remarks>
/// A <c>ScrollListBox</c> maps the pointer's vertical offset onto one of its rows, so a test of it has to
/// be able to aim at a row rather than at the control.
/// </remarks>
public sealed class PointerHost : IFlowHost, IMenuHost
{
    private int _x;
    private int _y;
    private bool _down;

    /// <summary>Gets or sets what to run when the flow asks for the main menu.</summary>
    public Action? OnShowMainMenu { get; set; }

    /// <summary>Places the pointer and sets its button state.</summary>
    /// <param name="x">The horizontal position in authored coordinates.</param>
    /// <param name="y">The vertical position in authored coordinates.</param>
    /// <param name="down">Whether the button is held.</param>
    public void MoveTo(int x, int y, bool down)
    {
        _x = x;
        _y = y;
        _down = down;
    }

    /// <inheritdoc/>
    public void ShowMainMenu() => OnShowMainMenu?.Invoke();

    /// <inheritdoc/>
    public void ShowIntroScreens()
    {
    }

    /// <inheritdoc/>
    public void ShowOutroScreens()
    {
    }

    /// <inheritdoc/>
    public void ShowAttractScreens()
    {
    }

    /// <inheritdoc/>
    public void BeginGame(FlowTask flow)
    {
    }

    /// <inheritdoc/>
    public void LoadLevel()
    {
    }

    /// <inheritdoc/>
    public void EndGame()
    {
    }

    /// <inheritdoc/>
    public bool TryGetPointer(out int x, out int y, out bool buttonDown)
    {
        x = _x;
        y = _y;
        buttonDown = _down;
        return true;
    }

    /// <inheritdoc/>
    public IEnumerable<MenuInputEvent> DrainEvents() => [];

    /// <inheritdoc/>
    /// <remarks>
    /// A <c>MenuText</c> gets its size from the renderer measuring its label, so without a stand-in size
    /// every entry is a zero-width rectangle and the pointer can never be over one.
    /// </remarks>
    public void PrepareScreen(MenuScreenTask screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        foreach (MenuTextTask entry in Descend(screen).OfType<MenuTextTask>())
        {
            entry.LayoutWidth = 80;
            entry.LayoutHeight = 16;
        }
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

    /// <inheritdoc/>
    public void DrawScreen(MenuScreenTask screen)
    {
    }

    /// <inheritdoc/>
    public void DrawPointer(int x, int y)
    {
    }

    /// <inheritdoc/>
    public void PlaySound(string name)
    {
    }

    /// <summary>Gets the state the menus last left with, or null while they have not.</summary>
    public MenuManagerState? LeftWith { get; private set; }

    /// <summary>Gets how many times the menus have asked to leave.</summary>
    public int LeaveCount { get; private set; }

    /// <inheritdoc/>
    public void LeaveMenus(MenuManagerState state)
    {
        LeftWith = state;
        LeaveCount++;
    }
}

/// <summary>A configuration host with a small, fixed mission list.</summary>
/// <remarks>
/// Three missions is enough to tell "the selection moved" from "the selection snapped back", and using a
/// stub rather than the installed data keeps the test about the control instead of about the mission scan.
/// </remarks>
public sealed class StubConfigHost : IConfigHost
{
    /// <summary>The mission the stub reports the profile as having reached.</summary>
    public const int ReachedMission = 3;

    /// <summary>Builds the briefing sprite name for a mission id.</summary>
    /// <param name="id">The mission id.</param>
    /// <returns>The sprite name.</returns>
    public static string PictureFor(int id) => $"mission{id}.spr";

    /// <inheritdoc/>
    public IEnumerable<MenuListItem> GetRenderDevices() => [];

    /// <inheritdoc/>
    public IEnumerable<MenuListItem> GetDisplayModes(int device) => [];

    /// <inheritdoc/>
    public int DefaultDisplayMode => 0;

    /// <inheritdoc/>
    public int GetDeviceIndex(string guid) => 0;

    /// <inheritdoc/>
    public string GetDeviceGuid(int device) => string.Empty;

    /// <inheritdoc/>
    public void SetResolution(int mode)
    {
    }

    /// <inheritdoc/>
    public void SetGamma(float gamma)
    {
    }

    /// <inheritdoc/>
    public IEnumerable<MenuListItem> GetMissions(int reached)
    {
        for (int id = 1; id <= ReachedMission; id++)
        {
            yield return new MenuListItem(id, $"Mission {id}");
        }
    }

    /// <inheritdoc/>
    public void FillMissionPictures(MenuWindowTask? target)
    {
        if (target is PictureBoxTask box)
        {
            for (int id = 1; id <= ReachedMission; id++)
            {
                box.Entries[id] = PictureFor(id);
            }
        }
    }

    /// <inheritdoc/>
    public void FillMissionText(MenuWindowTask? target)
    {
        if (target is TextBoxTask box)
        {
            for (int id = 1; id <= ReachedMission; id++)
            {
                box.Entries[id] = $"Briefing for mission {id}";
            }
        }
    }
}

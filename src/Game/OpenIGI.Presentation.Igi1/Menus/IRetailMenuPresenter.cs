using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.Menus;

/// <summary>The retail menu system's shell: its screens drawn, its pointer and keys read, its sounds played.</summary>
/// <remarks>
/// The title's presentation implements it over the menu renderer, built from a <see cref="RetailMenuPresentationRequest"/>;
/// the menu manager is initialised with it.
/// </remarks>
public interface IRetailMenuPresenter : IMenuHost, IDisposable
{
    /// <summary>Stops drawing the screens and frees what they were drawn with, as a mission takes the frame.</summary>
    /// <remarks>The shell stays answerable: input and sounds still work, and a screen asked for draws nothing.</remarks>
    void CloseScreens();
}

/// <summary>What the retail menu system is shown with.</summary>
/// <param name="Device">The graphics device the screens are uploaded to and drawn with.</param>
/// <param name="FileSystem">The game's files, which hold the menu's sprites and fonts.</param>
/// <param name="ResourcePath">The menu script's own resource archive.</param>
/// <param name="Strings">The menu's text.</param>
/// <param name="ExtraArchives">Further archives the screens draw from, such as the mission briefing images.</param>
/// <param name="Input">This frame's keyboard and mouse.</param>
/// <param name="Batch">The batch the screens and the pointer are drawn into.</param>
/// <param name="ScreenWidth">The window's width, in pixels.</param>
/// <param name="ScreenHeight">The window's height, in pixels.</param>
/// <param name="Scale">The interface magnification the screens are drawn and hit-tested at.</param>
/// <param name="Sounds">Where the menu's sounds play, or <see langword="null"/> for silence.</param>
/// <param name="Leave">What leaving the menus does, given how the manager left them.</param>
/// <param name="Logger">Where diagnostics go.</param>
public readonly record struct RetailMenuPresentationRequest(
    IGraphicsDevice Device,
    VirtualFileSystem FileSystem,
    string ResourcePath,
    MenuStringTable Strings,
    IReadOnlyList<string> ExtraArchives,
    InputState Input,
    SpriteBatch Batch,
    Func<int> ScreenWidth,
    Func<int> ScreenHeight,
    int Scale,
    SoundSystem? Sounds,
    Action<MenuManagerState> Leave,
    IOpenIgiLogger Logger);

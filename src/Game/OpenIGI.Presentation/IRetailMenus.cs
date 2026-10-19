using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Config;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Presentation;

/// <summary>What the retail menus' settings screens ask of the shell: the renderer, the monitor's sizes, and a size applied.</summary>
/// <param name="RendererName">The name the graphics device reports for itself.</param>
/// <param name="DisplayModes">The sizes the monitor reports, in any order and with repeats.</param>
/// <param name="ApplyResolution">What picking a resolution does.</param>
public sealed record RetailMenuDisplay(
    Func<string> RendererName,
    Func<IEnumerable<(int Width, int Height)>> DisplayModes,
    Action<int, int> ApplyResolution);

/// <summary>What a title's retail menus are started with.</summary>
/// <param name="Device">The graphics device the screens are drawn with.</param>
/// <param name="FileSystem">The game's files, which hold the menu script, its sprites and its fonts.</param>
/// <param name="Loop">The task loop the menu's tasks run in.</param>
/// <param name="Configuration">The player profiles the settings screens read and write.</param>
/// <param name="Display">What the settings screens ask of the shell.</param>
/// <param name="Missions">The title's missions, which the mission screens list, or <see langword="null"/> before they are scanned.</param>
/// <param name="Strings">The language's text, or <see langword="null"/> before it is loaded.</param>
/// <param name="Input">This frame's keyboard and mouse.</param>
/// <param name="Batch">The batch the screens and the pointer are drawn into.</param>
/// <param name="ScreenWidth">The window's width, in pixels.</param>
/// <param name="ScreenHeight">The window's height, in pixels.</param>
/// <param name="Scale">The interface magnification the screens are drawn and hit-tested at.</param>
/// <param name="Sounds">Where the menu's sounds play, or <see langword="null"/> for silence.</param>
/// <param name="StartScreen">The screen to open on in place of the script's own, or nought to leave the script's.</param>
/// <param name="ExternalNatives">The game's natives a menu expression may call, which answer <see langword="null"/> for a name they do not know.</param>
/// <param name="Leave">What leaving the menus does: <see langword="true"/> to start the chosen mission, <see langword="false"/> to quit.</param>
/// <param name="Logger">Where diagnostics go.</param>
public readonly record struct RetailMenusRequest(
    IGraphicsDevice Device,
    VirtualFileSystem FileSystem,
    GameLoop Loop,
    GameConfig Configuration,
    RetailMenuDisplay Display,
    Func<IMissionCatalogue<MissionDescriptor>?> Missions,
    Func<MenuStringTable?> Strings,
    InputState Input,
    SpriteBatch Batch,
    Func<int> ScreenWidth,
    Func<int> ScreenHeight,
    int Scale,
    SoundSystem? Sounds,
    int StartScreen,
    Func<string, ScriptArguments, QvmValue?> ExternalNatives,
    Action<bool> Leave,
    IOpenIgiLogger Logger);

/// <summary>A title's own retail menus: its menu script run in the loop, its screens drawn, its pointer and keys read.</summary>
public interface IRetailMenus : IDisposable
{
    /// <summary>Stops drawing the screens and frees what they were drawn with, as a mission takes the frame.</summary>
    void CloseScreens();
}

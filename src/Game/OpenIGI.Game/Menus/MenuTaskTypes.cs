using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Menus;

/// <summary>
/// Registers the menu task types and the constants their scripts name.
/// </summary>
/// <remarks>
/// <para>
/// The original spreads this over fifteen registrars — <c>MenuManager</c> at <c>0x4170A0</c>,
/// <c>MenuScreen</c> at <c>0x421750</c>, <c>MenuText</c> at <c>0x420CC0</c> and so on. Each declares
/// its class, binds its methods and builds a property block; only the property blocks matter to a
/// reimplementation, and those are declared by the script rather than the engine.
/// </para>
/// <para>
/// <b>Parameter order is wire order.</b> Every list here was transcribed from the
/// <c>Task_DeclareParameters</c> calls in retail <c>mainmenu.qvm</c>, which is the authority on what
/// the data contains: <c>Task_New</c> parses positionally, so one wrong entry shifts every field after
/// it and silently swallows the following child tasks. <c>MenuTaskRegistrationTests</c> checks the
/// lists against the script itself rather than against this file.
/// </para>
/// </remarks>
public static class MenuTaskTypes
{
    /// <summary>The colour indices <c>MenuText</c> registers, and the values a script's <c>ColourIndex</c> takes.</summary>
    /// <remarks>Registered by the original inside the <c>MenuText</c> registrar (<c>0x420CC0</c>).</remarks>
    public static IReadOnlyList<(string Name, int Value)> TextColours { get; } =
    [
        ("TEXT_BLACK", 0),
        ("TEXT_WHITE", 1),
        ("TEXT_GREEN", 2),
        ("TEXT_ORANGE", 3),
    ];

    /// <summary>Registers every menu task type.</summary>
    /// <param name="types">The registry to add to.</param>
    /// <param name="parent">The base type the menu types derive from, if the host has one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterAll(TaskTypeRegistry types, TaskType? parent = null)
    {
        ArgumentNullException.ThrowIfNull(types);

        types.Register<MenuManagerTask>("MenuManager", parent);
        types.Register<MenuFrameTask>("MenuFrame", parent);
        types.Register<MenuScreenTask>("MenuScreen", parent);
        types.Register<MenuTextTask>("MenuText", parent);
        types.Register<MenuTextConditionalTask>("MenuTextConditional", parent);
        types.Register<SlideBarTask>("SlideBar", parent);
        types.Register<ListBoxTask>("ListBox", parent);
        types.Register<ToggleBoxTask>("ToggleBox", parent);
        types.Register<InputBoxTask>("InputBox", parent);
        types.Register<AsciiBoxTask>("AsciiBox", parent);
        types.Register<ScrollListBoxTask>("ScrollListBox", parent);
        types.Register<PictureBoxTask>("PictureBox", parent);
        types.Register<TextBoxTask>("TextBox", parent);
        types.Register<TypeWriterBoxTask>("TypeWriterBox", parent);
        types.Register<DialogWindowTask>("DialogWindow", parent);

        // Spawned without a declaration because it takes no parameters, like the structural types.
        types.Register<ControlsMenuTask>("ControlsMenu", parent);

        // Two more the engine owns rather than the script: no menu file names either, but a SlideBar
        // builds a pair of MenuButtons in its constructor and they have to resolve to a type.
        types.Register<MenuButtonTask>("MenuButton", parent);
    }

    /// <summary>Registers the constants a menu script names.</summary>
    /// <param name="natives">The script namespace to add to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public static void RegisterConstants(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        foreach ((string name, int value) in TextColours)
        {
            natives.RegisterConstant(name, value);
        }
    }
}

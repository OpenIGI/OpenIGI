using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Config;

/// <summary>
/// The constants the game's configuration names for its scripts, as <c>Config_Open</c> (config.c) registers them: the input buttons
/// the controls are bound to, the input devices, every key and mouse source, the languages and the difficulties.
/// </summary>
public static class Igi2ConfigConstants
{
    /// <summary>
    /// The input buttons, <c>Config_atInputButton</c>, each with the bit it raises (<c>bInputButton</c>); a button's script value is the
    /// place of the first button raising the same bit, so the tab headings, which raise none, are all nought.
    /// </summary>
    public static IReadOnlyList<(string Name, ulong Bit)> InputButtons { get; } =
    [
        ("TAB-Movement", 0x0UL),
        ("MoveForwards", 0x4UL),
        ("MoveBackwards", 0x8UL),
        ("MoveLeft", 0x1UL),
        ("MoveRight", 0x2UL),
        ("TurnLeft", 0x100UL),
        ("TurnRight", 0x200UL),
        ("Jump", 0x800UL),
        ("Crouch", 0x400UL),
        ("RaiseStance", 0x1000UL),
        ("LowerStance", 0x2000UL),
        ("ToggleStandLie", 0x40000000000000UL),
        ("ToggleWalkRun", 0x4000UL),
        ("ModifyWalkRun", 0x8000UL),
        ("TAB-Actions & Equipment", 0x0UL),
        ("Activate", 0x20000UL),
        ("Peek", 0x10000UL),
        ("Binoculars", 0x1000000UL),
        ("ZoomIn", 0x2000000UL),
        ("ZoomOut", 0x4000000UL),
        ("Thermal", 0x8000000UL),
        ("ComputerMap", 0x10000000UL),
        ("ComputerLog", 0x100000000000UL),
        ("ComputerObjectives", 0x200000000000UL),
        ("ComputerIGIHQ", 0x400000000000UL),
        ("QuickLoad", 0x40000000000UL),
        ("TAB-Combat", 0x0UL),
        ("Fire", 0x80000UL),
        ("AlternateFire", 0x100000UL),
        ("NextWeapon", 0x200000UL),
        ("PrevWeapon", 0x400000UL),
        ("Reload", 0x40000UL),
        ("DropWeapon", 0x800000UL),
        ("WeaponCategory3", 0x400000000UL),
        ("WeaponCategory2", 0x200000000UL),
        ("WeaponCategory1", 0x100000000UL),
        ("WeaponCategory4", 0x800000000UL),
        ("WeaponCategory5", 0x1000000000UL),
        ("WeaponCategory6", 0x2000000000UL),
        ("WeaponCategory7", 0x4000000000UL),
        ("WeaponCategory9", 0x10000000000UL),
        ("Inventory", 0x4000000000000UL),
        ("TAB-Multiplayer", 0x0UL),
        ("Shop", 0x20000000000UL),
        ("Radio", 0x20000000UL),
        ("SquadRadio", 0x20000000000000UL),
        ("Say", 0x40000000UL),
        ("TeamSay", 0x80000000UL),
        ("ChangeTeams", 0x800000000000UL),
        ("Scores", 0x1000000000000UL),
        ("Console", 0x2000000000000UL),
        ("BuyPrimaryAmmo", 0x8000000000000UL),
        ("BuySecondaryAmmo", 0x10000000000000UL),
    ];

    /// <summary>The input devices, <c>Config_atInputPortDevice</c>.</summary>
    public static IReadOnlyList<(string Name, int Device)> InputDevices { get; } =
    [
        ("INPUTPORT_DEVICE_KEYBOARD", 1),
        ("INPUTPORT_DEVICE_JOYPAD", 2),
        ("INPUTPORT_DEVICE_MOUSE", 3),
    ];

    /// <summary>
    /// The keys and mouse buttons a control may be bound to, <c>Config_atDeviceSource</c>: a key is its DirectInput scan code with
    /// bit 30 set, a mouse source its bit with bits 30 and 31 set.
    /// </summary>
    public static IReadOnlyList<(string Name, uint Source)> DeviceSources { get; } =
    [
        ("KEY_ANGLE", 0x40000056u),
        ("KEY_A", 0x4000001Eu),
        ("KEY_B", 0x40000030u),
        ("KEY_C", 0x4000002Eu),
        ("KEY_D", 0x40000020u),
        ("KEY_E", 0x40000012u),
        ("KEY_F", 0x40000021u),
        ("KEY_G", 0x40000022u),
        ("KEY_H", 0x40000023u),
        ("KEY_I", 0x40000017u),
        ("KEY_J", 0x40000024u),
        ("KEY_K", 0x40000025u),
        ("KEY_L", 0x40000026u),
        ("KEY_M", 0x40000032u),
        ("KEY_N", 0x40000031u),
        ("KEY_O", 0x40000018u),
        ("KEY_P", 0x40000019u),
        ("KEY_Q", 0x40000010u),
        ("KEY_R", 0x40000013u),
        ("KEY_S", 0x4000001Fu),
        ("KEY_T", 0x40000014u),
        ("KEY_U", 0x40000016u),
        ("KEY_V", 0x4000002Fu),
        ("KEY_W", 0x40000011u),
        ("KEY_X", 0x4000002Du),
        ("KEY_Y", 0x40000015u),
        ("KEY_Z", 0x4000002Cu),
        ("KEY_0", 0x4000000Bu),
        ("KEY_1", 0x40000002u),
        ("KEY_2", 0x40000003u),
        ("KEY_3", 0x40000004u),
        ("KEY_4", 0x40000005u),
        ("KEY_5", 0x40000006u),
        ("KEY_6", 0x40000007u),
        ("KEY_7", 0x40000008u),
        ("KEY_8", 0x40000009u),
        ("KEY_9", 0x4000000Au),
        ("KEY_SEPARATOR", 0x40000034u),
        ("KEY_DECIMAL", 0x40000053u),
        ("KEY_COMMA", 0x40000033u),
        ("KEY_SLASH", 0x40000035u),
        ("KEY_CAPITAL", 0x4000003Au),
        ("KEY_BACKSLASH", 0x4000002Bu),
        ("KEY_BACKSPACE", 0x4000000Eu),
        ("KEY_UNDERLINE", 0x40000093u),
        ("KEY_NUMLOCK", 0x40000045u),
        ("KEY_SCROLL", 0x40000046u),
        ("KEY_TAB", 0x4000000Fu),
        ("KEY_RETURN", 0x4000001Cu),
        ("KEY_LEFT_SHIFT", 0x4000002Au),
        ("KEY_RIGHT_SHIFT", 0x40000036u),
        ("KEY_LEFT_CTRL", 0x4000001Du),
        ("KEY_RIGHT_CTRL", 0x4000009Du),
        ("KEY_DIVIDE", 0x400000B5u),
        ("KEY_MULTIPLY", 0x40000037u),
        ("KEY_SUBTRACT", 0x4000004Au),
        ("KEY_ADD", 0x4000004Eu),
        ("KEY_MINUS", 0x4000000Cu),
        ("KEY_EQUALS", 0x4000000Du),
        ("KEY_LEFT_BRACKET", 0x4000001Au),
        ("KEY_RIGHT_BRACKET", 0x4000001Bu),
        ("KEY_SEMICOLON", 0x40000027u),
        ("KEY_APOSTROPHE", 0x40000028u),
        ("KEY_GRAVE", 0x40000029u),
        ("KEY_SPACE", 0x40000039u),
        ("KEY_END", 0x400000CFu),
        ("KEY_HOME", 0x400000C7u),
        ("KEY_LEFT", 0x400000CBu),
        ("KEY_UP", 0x400000C8u),
        ("KEY_RIGHT", 0x400000CDu),
        ("KEY_DOWN", 0x400000D0u),
        ("KEY_INSERT", 0x400000D2u),
        ("KEY_DELETE", 0x400000D3u),
        ("KEY_PAGEUP", 0x400000C9u),
        ("KEY_PAGEDOWN", 0x400000D1u),
        ("KEY_F1", 0x4000003Bu),
        ("KEY_F2", 0x4000003Cu),
        ("KEY_F3", 0x4000003Du),
        ("KEY_F4", 0x4000003Eu),
        ("KEY_F5", 0x4000003Fu),
        ("KEY_F6", 0x40000040u),
        ("KEY_F7", 0x40000041u),
        ("KEY_F8", 0x40000042u),
        ("KEY_F9", 0x40000043u),
        ("KEY_F10", 0x40000044u),
        ("KEY_F11", 0x40000057u),
        ("KEY_F12", 0x40000058u),
        ("KEY_F13", 0x40000064u),
        ("KEY_F14", 0x40000065u),
        ("KEY_F15", 0x40000066u),
        ("KEY_NUMPAD0", 0x40000052u),
        ("KEY_NUMPAD1", 0x4000004Fu),
        ("KEY_NUMPAD2", 0x40000050u),
        ("KEY_NUMPAD3", 0x40000051u),
        ("KEY_NUMPAD4", 0x4000004Bu),
        ("KEY_NUMPAD5", 0x4000004Cu),
        ("KEY_NUMPAD6", 0x4000004Du),
        ("KEY_NUMPAD7", 0x40000047u),
        ("KEY_NUMPAD8", 0x40000048u),
        ("KEY_NUMPAD9", 0x40000049u),
        ("KEY_NUMPADENTER", 0x4000009Cu),
        ("KEY_NUMPADCOMMA", 0x400000B3u),
        ("KEY_NUMPADEQUALS", 0x4000008Du),
        ("MOUSE_BUTTON_1", 0xC0000001u),
        ("MOUSE_BUTTON_2", 0xC0000002u),
        ("MOUSE_BUTTON_3", 0xC0000004u),
        ("MOUSE_BUTTON_4", 0xC0000008u),
        ("MOUSE_BUTTON_5", 0xC0000010u),
        ("MOUSE_BUTTON_6", 0xC0000020u),
        ("MOUSE_BUTTON_7", 0xC0000040u),
        ("MOUSE_BUTTON_8", 0xC0000080u),
        ("MOUSE_WHEEL_UP", 0xC0000100u),
        ("MOUSE_WHEEL_DOWN", 0xC0000200u),
    ];

    /// <summary>The languages, <c>Config_azGameLanguage</c>, each worth its place.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["ENGLISH", "USA", "GERMAN", "FRENCH", "SPANISH", "ITALIAN"];

    /// <summary>The difficulties, <c>Config_azGameDifficulty</c>, each worth its place.</summary>
    public static IReadOnlyList<string> Difficulties { get; } = ["GD_1", "GD_2", "GD_3"];

    /// <summary>Registers every constant.</summary>
    /// <param name="natives">The namespace.</param>
    public static void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        for (int i = 0; i < InputButtons.Count; i++)
        {
            ulong bit = InputButtons[i].Bit;
            int first = 0;
            while (InputButtons[first].Bit != bit)
            {
                first++;
            }

            natives.RegisterConstant(InputButtons[i].Name, first);
        }

        foreach ((string name, int device) in InputDevices)
        {
            natives.RegisterConstant(name, device);
        }

        foreach ((string name, uint source) in DeviceSources)
        {
            natives.RegisterConstant(name, unchecked((int)source));
        }

        for (int i = 0; i < Languages.Count; i++)
        {
            natives.RegisterConstant(Languages[i], i);
        }

        for (int i = 0; i < Difficulties.Count; i++)
        {
            natives.RegisterConstant(Difficulties[i], i);
        }
    }
}

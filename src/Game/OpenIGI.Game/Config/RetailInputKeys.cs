namespace OpenIGI.Game.Config;

/// <summary>One key or button a Project I.G.I. action can be bound to.</summary>
/// <param name="Name">The config symbol, which is also the menu text's key for its label.</param>
/// <param name="IsMouse">Whether it is a mouse button or wheel notch rather than a key.</param>
/// <param name="Code">A key's DirectInput scan code, or a mouse input's bit in the mouse's button word.</param>
public readonly record struct RetailInputKey(string Name, bool IsMouse, int Code)
{
    /// <summary>Gets the config symbol of the device it is on.</summary>
    public string Device => IsMouse ? InputBindingKeyNames.MouseDevice : InputBindingKeyNames.KeyboardDevice;
}

/// <summary>The keys and buttons Project I.G.I. can bind, in the order its table lists them.</summary>
/// <remarks>
/// <para>
/// The 127 records at <c>0x536998</c>, <c>{ char name[32]; u32 code; }</c>, whose code carries the device in its top two bits
/// (1 a key, 3 the mouse) under the scan code or the mouse bit. <c>0x406270</c> counts them and <c>0x406240</c> reads one.
/// </para>
/// <para>
/// The order matters: the controls list takes the first one held (<c>0x4234E0</c>), and a few scan codes have two names --
/// <c>KEY_CAPITAL</c> and <c>KEY_CAPSLOCK</c>, <c>KEY_LEFT_ALT</c> and <c>KEY_LEFT_MENU</c> -- of which the earlier one is
/// the one taken.
/// </para>
/// </remarks>
public static class RetailInputKeys
{
    /// <summary>Gets the keys and buttons, in table order.</summary>
    public static IReadOnlyList<RetailInputKey> All { get; } =
    [
        new("KEY_ANGLE", false, 0x56),
        new("KEY_ESC", false, 0x01),
        new("KEY_A", false, 0x1E),
        new("KEY_B", false, 0x30),
        new("KEY_C", false, 0x2E),
        new("KEY_D", false, 0x20),
        new("KEY_E", false, 0x12),
        new("KEY_F", false, 0x21),
        new("KEY_G", false, 0x22),
        new("KEY_H", false, 0x23),
        new("KEY_I", false, 0x17),
        new("KEY_J", false, 0x24),
        new("KEY_K", false, 0x25),
        new("KEY_L", false, 0x26),
        new("KEY_M", false, 0x32),
        new("KEY_N", false, 0x31),
        new("KEY_O", false, 0x18),
        new("KEY_P", false, 0x19),
        new("KEY_Q", false, 0x10),
        new("KEY_R", false, 0x13),
        new("KEY_S", false, 0x1F),
        new("KEY_T", false, 0x14),
        new("KEY_U", false, 0x16),
        new("KEY_V", false, 0x2F),
        new("KEY_W", false, 0x11),
        new("KEY_X", false, 0x2D),
        new("KEY_Y", false, 0x15),
        new("KEY_Z", false, 0x2C),
        new("KEY_0", false, 0x0B),
        new("KEY_1", false, 0x02),
        new("KEY_2", false, 0x03),
        new("KEY_3", false, 0x04),
        new("KEY_4", false, 0x05),
        new("KEY_5", false, 0x06),
        new("KEY_6", false, 0x07),
        new("KEY_7", false, 0x08),
        new("KEY_8", false, 0x09),
        new("KEY_9", false, 0x0A),
        new("KEY_SEPARATOR", false, 0x34),
        new("KEY_DECIMAL", false, 0x53),
        new("KEY_COMMA", false, 0x33),
        new("KEY_COLON", false, 0x92),
        new("KEY_SLASH", false, 0x35),
        new("KEY_CAPITAL", false, 0x3A),
        new("KEY_BACKSLASH", false, 0x2B),
        new("KEY_BACKSPACE", false, 0x0E),
        new("KEY_UNDERLINE", false, 0x93),
        new("KEY_CAPSLOCK", false, 0x3A),
        new("KEY_NUMLOCK", false, 0x45),
        new("KEY_SCROLL", false, 0x46),
        new("KEY_TAB", false, 0x0F),
        new("KEY_RETURN", false, 0x1C),
        new("KEY_LEFT_SHIFT", false, 0x2A),
        new("KEY_RIGHT_SHIFT", false, 0x36),
        new("KEY_LEFT_CTRL", false, 0x1D),
        new("KEY_RIGHT_CTRL", false, 0x9D),
        new("KEY_LEFT_ALT", false, 0x38),
        new("KEY_RIGHT_ALT", false, 0xB8),
        new("KEY_DIVIDE", false, 0xB5),
        new("KEY_MULTIPLY", false, 0x37),
        new("KEY_SUBTRACT", false, 0x4A),
        new("KEY_ADD", false, 0x4E),
        new("KEY_MINUS", false, 0x0C),
        new("KEY_EQUALS", false, 0x0D),
        new("KEY_LEFT_BRACKET", false, 0x1A),
        new("KEY_RIGHT_BRACKET", false, 0x1B),
        new("KEY_SEMICOLON", false, 0x27),
        new("KEY_APOSTROPHE", false, 0x28),
        new("KEY_GRAVE", false, 0x29),
        new("KEY_LEFT_MENU", false, 0x38),
        new("KEY_RIGHT_MENU", false, 0xB8),
        new("KEY_LEFT_WIN", false, 0xDB),
        new("KEY_RIGHT_WIN", false, 0xDC),
        new("KEY_SPACE", false, 0x39),
        new("KEY_END", false, 0xCF),
        new("KEY_HOME", false, 0xC7),
        new("KEY_LEFT", false, 0xCB),
        new("KEY_UP", false, 0xC8),
        new("KEY_RIGHT", false, 0xCD),
        new("KEY_DOWN", false, 0xD0),
        new("KEY_INSERT", false, 0xD2),
        new("KEY_DELETE", false, 0xD3),
        new("KEY_PAGEUP", false, 0xC9),
        new("KEY_PAGEDOWN", false, 0xD1),
        new("KEY_F1", false, 0x3B),
        new("KEY_F2", false, 0x3C),
        new("KEY_F3", false, 0x3D),
        new("KEY_F4", false, 0x3E),
        new("KEY_F5", false, 0x3F),
        new("KEY_F6", false, 0x40),
        new("KEY_F7", false, 0x41),
        new("KEY_F8", false, 0x42),
        new("KEY_F9", false, 0x43),
        new("KEY_F10", false, 0x44),
        new("KEY_F11", false, 0x57),
        new("KEY_F12", false, 0x58),
        new("KEY_F13", false, 0x64),
        new("KEY_F14", false, 0x65),
        new("KEY_F15", false, 0x66),
        new("KEY_NUMPAD0", false, 0x52),
        new("KEY_NUMPAD1", false, 0x4F),
        new("KEY_NUMPAD2", false, 0x50),
        new("KEY_NUMPAD3", false, 0x51),
        new("KEY_NUMPAD4", false, 0x4B),
        new("KEY_NUMPAD5", false, 0x4C),
        new("KEY_NUMPAD6", false, 0x4D),
        new("KEY_NUMPAD7", false, 0x47),
        new("KEY_NUMPAD8", false, 0x48),
        new("KEY_NUMPAD9", false, 0x49),
        new("KEY_NUMPADENTER", false, 0x9C),
        new("KEY_NUMPADCOMMA", false, 0xB3),
        new("KEY_NUMPADEQUALS", false, 0x8D),
        new("KEY_NUMPADSTAR", false, 0x37),
        new("KEY_NUMPADMINUS", false, 0x4A),
        new("KEY_NUMPADPLUS", false, 0x4E),
        new("KEY_NUMPADPERIOD", false, 0x53),
        new("KEY_NUMPADSLASH", false, 0xB5),
        new("MOUSE_BUTTON_1", true, 0x01),
        new("MOUSE_BUTTON_2", true, 0x02),
        new("MOUSE_BUTTON_3", true, 0x04),
        new("MOUSE_BUTTON_4", true, 0x08),
        new("MOUSE_BUTTON_5", true, 0x10),
        new("MOUSE_BUTTON_6", true, 0x20),
        new("MOUSE_BUTTON_7", true, 0x40),
        new("MOUSE_BUTTON_8", true, 0x80),
        new("MOUSE_WHEEL_UP", true, 0x100),
        new("MOUSE_WHEEL_DOWN", true, 0x200),
    ];

    /// <summary>Finds a binding's key in the table.</summary>
    /// <param name="device">The binding's device symbol.</param>
    /// <param name="key">The binding's key symbol.</param>
    /// <returns>The table index, or -1 when the table has no such key.</returns>
    /// <remarks>
    /// <c>0x423860</c> matches the device and the code; a binding names its key by symbol, so the first record of that name
    /// on that device is the one.
    /// </remarks>
    public static int IndexOf(string device, string key)
    {
        bool mouse = string.Equals(device, InputBindingKeyNames.MouseDevice, StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i].IsMouse == mouse && string.Equals(All[i].Name, key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

namespace OpenIGI.Input;

/// <summary>
/// A keyboard key, identified by its DirectInput <c>DIK_*</c> scan code.
/// </summary>
/// <remarks>
/// <para>
/// The original indexes its three 256-byte key arrays by these codes, and mission scripts and config
/// files store them as raw numbers — so the numbering is part of the data format, not an
/// implementation detail. A key binding read from a retail config must resolve to the same physical
/// key.
/// </para>
/// <para>
/// These are <em>positional</em> codes: <see cref="Q"/> is the key where Q sits on a US layout,
/// whatever the active layout prints on it.
/// </para>
/// </remarks>
public enum KeyCode : byte
{
    /// <summary>No key.</summary>
    None = 0x00,

    /// <summary>The Escape key.</summary>
    Escape = 0x01,

    /// <summary>The 1 key on the number row.</summary>
    D1 = 0x02,

    /// <summary>The 2 key on the number row.</summary>
    D2 = 0x03,

    /// <summary>The 3 key on the number row.</summary>
    D3 = 0x04,

    /// <summary>The 4 key on the number row.</summary>
    D4 = 0x05,

    /// <summary>The 5 key on the number row.</summary>
    D5 = 0x06,

    /// <summary>The 6 key on the number row.</summary>
    D6 = 0x07,

    /// <summary>The 7 key on the number row.</summary>
    D7 = 0x08,

    /// <summary>The 8 key on the number row.</summary>
    D8 = 0x09,

    /// <summary>The 9 key on the number row.</summary>
    D9 = 0x0A,

    /// <summary>The 0 key on the number row.</summary>
    D0 = 0x0B,

    /// <summary>The minus key on the number row.</summary>
    Minus = 0x0C,

    /// <summary>The equals key on the number row.</summary>
    Equals = 0x0D,

    /// <summary>The Backspace key.</summary>
    Backspace = 0x0E,

    /// <summary>The Tab key.</summary>
    Tab = 0x0F,

    /// <summary>The Q key.</summary>
    Q = 0x10,

    /// <summary>The W key.</summary>
    W = 0x11,

    /// <summary>The E key.</summary>
    E = 0x12,

    /// <summary>The R key.</summary>
    R = 0x13,

    /// <summary>The T key.</summary>
    T = 0x14,

    /// <summary>The Y key.</summary>
    Y = 0x15,

    /// <summary>The U key.</summary>
    U = 0x16,

    /// <summary>The I key.</summary>
    I = 0x17,

    /// <summary>The O key.</summary>
    O = 0x18,

    /// <summary>The P key.</summary>
    P = 0x19,

    /// <summary>The left bracket key.</summary>
    LeftBracket = 0x1A,

    /// <summary>The right bracket key.</summary>
    RightBracket = 0x1B,

    /// <summary>The Return key on the main block.</summary>
    Return = 0x1C,

    /// <summary>The left Control key.</summary>
    LeftControl = 0x1D,

    /// <summary>The A key.</summary>
    A = 0x1E,

    /// <summary>The S key.</summary>
    S = 0x1F,

    /// <summary>The D key.</summary>
    D = 0x20,

    /// <summary>The F key.</summary>
    F = 0x21,

    /// <summary>The G key.</summary>
    G = 0x22,

    /// <summary>The H key.</summary>
    H = 0x23,

    /// <summary>The J key.</summary>
    J = 0x24,

    /// <summary>The K key.</summary>
    K = 0x25,

    /// <summary>The L key.</summary>
    L = 0x26,

    /// <summary>The semicolon key.</summary>
    Semicolon = 0x27,

    /// <summary>The apostrophe key.</summary>
    Apostrophe = 0x28,

    /// <summary>The grave accent key.</summary>
    Grave = 0x29,

    /// <summary>The left Shift key.</summary>
    LeftShift = 0x2A,

    /// <summary>The backslash key.</summary>
    Backslash = 0x2B,

    /// <summary>The Z key.</summary>
    Z = 0x2C,

    /// <summary>The X key.</summary>
    X = 0x2D,

    /// <summary>The C key.</summary>
    C = 0x2E,

    /// <summary>The V key.</summary>
    V = 0x2F,

    /// <summary>The B key.</summary>
    B = 0x30,

    /// <summary>The N key.</summary>
    N = 0x31,

    /// <summary>The M key.</summary>
    M = 0x32,

    /// <summary>The comma key.</summary>
    Comma = 0x33,

    /// <summary>The period key.</summary>
    Period = 0x34,

    /// <summary>The slash key.</summary>
    Slash = 0x35,

    /// <summary>The right Shift key.</summary>
    RightShift = 0x36,

    /// <summary>The multiply key on the numeric keypad.</summary>
    NumpadMultiply = 0x37,

    /// <summary>The left Alt key.</summary>
    LeftAlt = 0x38,

    /// <summary>The space bar.</summary>
    Space = 0x39,

    /// <summary>The Caps Lock key.</summary>
    CapsLock = 0x3A,

    /// <summary>The F1 key.</summary>
    F1 = 0x3B,

    /// <summary>The F2 key.</summary>
    F2 = 0x3C,

    /// <summary>The F3 key.</summary>
    F3 = 0x3D,

    /// <summary>The F4 key.</summary>
    F4 = 0x3E,

    /// <summary>The F5 key.</summary>
    F5 = 0x3F,

    /// <summary>The F6 key.</summary>
    F6 = 0x40,

    /// <summary>The F7 key.</summary>
    F7 = 0x41,

    /// <summary>The F8 key.</summary>
    F8 = 0x42,

    /// <summary>The F9 key. The original's cheat gate listens on this one.</summary>
    F9 = 0x43,

    /// <summary>The F10 key.</summary>
    F10 = 0x44,

    /// <summary>The Num Lock key.</summary>
    NumLock = 0x45,

    /// <summary>The Scroll Lock key.</summary>
    ScrollLock = 0x46,

    /// <summary>The 7 key on the numeric keypad.</summary>
    Numpad7 = 0x47,

    /// <summary>The 8 key on the numeric keypad.</summary>
    Numpad8 = 0x48,

    /// <summary>The 9 key on the numeric keypad.</summary>
    Numpad9 = 0x49,

    /// <summary>The subtract key on the numeric keypad.</summary>
    NumpadSubtract = 0x4A,

    /// <summary>The 4 key on the numeric keypad.</summary>
    Numpad4 = 0x4B,

    /// <summary>The 5 key on the numeric keypad.</summary>
    Numpad5 = 0x4C,

    /// <summary>The 6 key on the numeric keypad.</summary>
    Numpad6 = 0x4D,

    /// <summary>The add key on the numeric keypad.</summary>
    NumpadAdd = 0x4E,

    /// <summary>The 1 key on the numeric keypad.</summary>
    Numpad1 = 0x4F,

    /// <summary>The 2 key on the numeric keypad.</summary>
    Numpad2 = 0x50,

    /// <summary>The 3 key on the numeric keypad.</summary>
    Numpad3 = 0x51,

    /// <summary>The 0 key on the numeric keypad.</summary>
    Numpad0 = 0x52,

    /// <summary>The decimal key on the numeric keypad.</summary>
    NumpadDecimal = 0x53,

    /// <summary>The F11 key.</summary>
    F11 = 0x57,

    /// <summary>The F12 key.</summary>
    F12 = 0x58,

    /// <summary>The Return key on the numeric keypad.</summary>
    NumpadEnter = 0x9C,

    /// <summary>The right Control key.</summary>
    RightControl = 0x9D,

    /// <summary>The divide key on the numeric keypad.</summary>
    NumpadDivide = 0xB5,

    /// <summary>The Print Screen key.</summary>
    PrintScreen = 0xB7,

    /// <summary>The right Alt key.</summary>
    RightAlt = 0xB8,

    /// <summary>The Pause key.</summary>
    Pause = 0xC5,

    /// <summary>The Home key.</summary>
    Home = 0xC7,

    /// <summary>The up arrow key.</summary>
    Up = 0xC8,

    /// <summary>The Page Up key.</summary>
    PageUp = 0xC9,

    /// <summary>The left arrow key.</summary>
    Left = 0xCB,

    /// <summary>The right arrow key.</summary>
    Right = 0xCD,

    /// <summary>The End key.</summary>
    End = 0xCF,

    /// <summary>The down arrow key.</summary>
    Down = 0xD0,

    /// <summary>The Page Down key.</summary>
    PageDown = 0xD1,

    /// <summary>The Insert key.</summary>
    Insert = 0xD2,

    /// <summary>The Delete key.</summary>
    Delete = 0xD3,

    /// <summary>The left Windows key.</summary>
    LeftWindows = 0xDB,

    /// <summary>The right Windows key.</summary>
    RightWindows = 0xDC,

    /// <summary>The Application (menu) key.</summary>
    Application = 0xDD,
}

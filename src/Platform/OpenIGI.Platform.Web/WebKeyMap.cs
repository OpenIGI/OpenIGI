using OpenIGI.Input;

namespace OpenIGI.Platform.Web;

/// <summary>
/// Translates the browser's physical key names into the DirectInput scan codes the engine indexes by.
/// </summary>
/// <remarks>
/// The twin of the desktop's key map. A browser names a key by <c>KeyboardEvent.code</c>, which is
/// positional -- <c>KeyW</c> is the key where W sits on a US layout, whatever the layout prints on it --
/// and the scan codes are positional too, so the mapping is layout-independent in the same way.
/// </remarks>
public static class WebKeyMap
{
    private static readonly Dictionary<string, KeyCode> Map = new(StringComparer.Ordinal)
    {
        ["Escape"] = KeyCode.Escape,
        ["Digit1"] = KeyCode.D1,
        ["Digit2"] = KeyCode.D2,
        ["Digit3"] = KeyCode.D3,
        ["Digit4"] = KeyCode.D4,
        ["Digit5"] = KeyCode.D5,
        ["Digit6"] = KeyCode.D6,
        ["Digit7"] = KeyCode.D7,
        ["Digit8"] = KeyCode.D8,
        ["Digit9"] = KeyCode.D9,
        ["Digit0"] = KeyCode.D0,
        ["Minus"] = KeyCode.Minus,
        ["Equal"] = KeyCode.Equals,
        ["Backspace"] = KeyCode.Backspace,
        ["Tab"] = KeyCode.Tab,
        ["KeyQ"] = KeyCode.Q,
        ["KeyW"] = KeyCode.W,
        ["KeyE"] = KeyCode.E,
        ["KeyR"] = KeyCode.R,
        ["KeyT"] = KeyCode.T,
        ["KeyY"] = KeyCode.Y,
        ["KeyU"] = KeyCode.U,
        ["KeyI"] = KeyCode.I,
        ["KeyO"] = KeyCode.O,
        ["KeyP"] = KeyCode.P,
        ["BracketLeft"] = KeyCode.LeftBracket,
        ["BracketRight"] = KeyCode.RightBracket,
        ["Enter"] = KeyCode.Return,
        ["ControlLeft"] = KeyCode.LeftControl,
        ["KeyA"] = KeyCode.A,
        ["KeyS"] = KeyCode.S,
        ["KeyD"] = KeyCode.D,
        ["KeyF"] = KeyCode.F,
        ["KeyG"] = KeyCode.G,
        ["KeyH"] = KeyCode.H,
        ["KeyJ"] = KeyCode.J,
        ["KeyK"] = KeyCode.K,
        ["KeyL"] = KeyCode.L,
        ["Semicolon"] = KeyCode.Semicolon,
        ["Quote"] = KeyCode.Apostrophe,
        ["Backquote"] = KeyCode.Grave,
        ["ShiftLeft"] = KeyCode.LeftShift,
        ["Backslash"] = KeyCode.Backslash,
        ["KeyZ"] = KeyCode.Z,
        ["KeyX"] = KeyCode.X,
        ["KeyC"] = KeyCode.C,
        ["KeyV"] = KeyCode.V,
        ["KeyB"] = KeyCode.B,
        ["KeyN"] = KeyCode.N,
        ["KeyM"] = KeyCode.M,
        ["Comma"] = KeyCode.Comma,
        ["Period"] = KeyCode.Period,
        ["Slash"] = KeyCode.Slash,
        ["ShiftRight"] = KeyCode.RightShift,
        ["NumpadMultiply"] = KeyCode.NumpadMultiply,
        ["AltLeft"] = KeyCode.LeftAlt,
        ["Space"] = KeyCode.Space,
        ["CapsLock"] = KeyCode.CapsLock,
        ["F1"] = KeyCode.F1,
        ["F2"] = KeyCode.F2,
        ["F3"] = KeyCode.F3,
        ["F4"] = KeyCode.F4,
        ["F5"] = KeyCode.F5,
        ["F6"] = KeyCode.F6,
        ["F7"] = KeyCode.F7,
        ["F8"] = KeyCode.F8,
        ["F9"] = KeyCode.F9,
        ["F10"] = KeyCode.F10,
        ["NumLock"] = KeyCode.NumLock,
        ["ScrollLock"] = KeyCode.ScrollLock,
        ["Numpad7"] = KeyCode.Numpad7,
        ["Numpad8"] = KeyCode.Numpad8,
        ["Numpad9"] = KeyCode.Numpad9,
        ["NumpadSubtract"] = KeyCode.NumpadSubtract,
        ["Numpad4"] = KeyCode.Numpad4,
        ["Numpad5"] = KeyCode.Numpad5,
        ["Numpad6"] = KeyCode.Numpad6,
        ["NumpadAdd"] = KeyCode.NumpadAdd,
        ["Numpad1"] = KeyCode.Numpad1,
        ["Numpad2"] = KeyCode.Numpad2,
        ["Numpad3"] = KeyCode.Numpad3,
        ["Numpad0"] = KeyCode.Numpad0,
        ["NumpadDecimal"] = KeyCode.NumpadDecimal,
        ["F11"] = KeyCode.F11,
        ["F12"] = KeyCode.F12,
        ["NumpadEnter"] = KeyCode.NumpadEnter,
        ["ControlRight"] = KeyCode.RightControl,
        ["NumpadDivide"] = KeyCode.NumpadDivide,
        ["PrintScreen"] = KeyCode.PrintScreen,
        ["AltRight"] = KeyCode.RightAlt,
        ["Pause"] = KeyCode.Pause,
        ["Home"] = KeyCode.Home,
        ["ArrowUp"] = KeyCode.Up,
        ["PageUp"] = KeyCode.PageUp,
        ["ArrowLeft"] = KeyCode.Left,
        ["ArrowRight"] = KeyCode.Right,
        ["End"] = KeyCode.End,
        ["ArrowDown"] = KeyCode.Down,
        ["PageDown"] = KeyCode.PageDown,
        ["Insert"] = KeyCode.Insert,
        ["Delete"] = KeyCode.Delete,
        ["MetaLeft"] = KeyCode.LeftWindows,
        ["MetaRight"] = KeyCode.RightWindows,
        ["ContextMenu"] = KeyCode.Application,
    };

    /// <summary>Gets the number of browser key names that have a scan code.</summary>
    public static int MappedKeyCount => Map.Count;

    /// <summary>Translates a browser key name to its scan code.</summary>
    /// <param name="code">The key's <c>KeyboardEvent.code</c>.</param>
    /// <returns>The scan code, or <see cref="KeyCode.None"/> for a key the original has no code for.</returns>
    public static KeyCode Translate(string? code) =>
        code is not null && Map.TryGetValue(code, out KeyCode key) ? key : KeyCode.None;
}

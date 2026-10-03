using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenIGI.Platform.Desktop;

/// <summary>One item of a <see cref="TrayIcon"/>'s menu.</summary>
/// <param name="Text">What the item says.</param>
/// <param name="Run">What choosing it does.</param>
public sealed record TrayMenuItem(string Text, Action Run);

/// <summary>An icon in the Windows notification area, with a menu, that also shows the program's notifications.</summary>
/// <remarks>
/// <para>
/// GLFW has no notion of the notification area, so this talks to the shell directly. It owns a message-only
/// window, created on the thread that runs the program's event loop, so the clicks the shell sends it arrive
/// through that loop's own event wait, with nothing else to pump. A left click raises <see cref="Activated"/>;
/// a right click opens the menu. When Explorer restarts, the icon is put back.
/// </para>
/// <para>
/// Windows only: GLFW gives macOS and Linux nothing equivalent, and a program there minimises to the Dock or
/// the taskbar instead.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmLeftButtonDoubleClick = 0x0203;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint WmNull = 0x0000;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 0x01;
    private const uint NifIcon = 0x02;
    private const uint NifTip = 0x04;
    private const uint NifInfo = 0x10;
    private const uint NiifUser = 0x04;
    private const uint NiifLargeIcon = 0x20;
    private const uint TpmReturnCommand = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const int SmallIconWidth = 49; // SM_CXSMICON
    private const nint MessageOnlyParent = -3; // HWND_MESSAGE

    private static readonly Dictionary<nint, TrayIcon> ByWindow = [];
    private static readonly uint TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

    private readonly string _tooltip;
    private readonly IReadOnlyList<TrayMenuItem> _menu;
    private readonly nint _className;
    private readonly nint _window;
    private readonly nint _icon;
    private bool _added;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TrayIcon"/> class and shows the icon.</summary>
    /// <param name="tooltip">What hovering over the icon says.</param>
    /// <param name="menu">The right-click menu, top to bottom.</param>
    /// <exception cref="InvalidOperationException">The icon's window could not be created.</exception>
    public TrayIcon(string tooltip, IReadOnlyList<TrayMenuItem> menu)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        ArgumentNullException.ThrowIfNull(menu);
        _tooltip = tooltip;
        _menu = menu;

        nint instance = GetModuleHandleW(null);
        _className = Marshal.StringToHGlobalUni("OpenIGI.TrayIcon." + Guid.NewGuid().ToString("N"));
        WindowClass windowClass = new()
        {
            Size = (uint)sizeof(WindowClass),
            Procedure = &WindowProcedure,
            Instance = instance,
            ClassName = (char*)_className,
        };

        if (RegisterClassExW(&windowClass) == 0)
        {
            Marshal.FreeHGlobal(_className);
            throw new InvalidOperationException("The notification-area icon's window class could not be registered.");
        }

        _window = CreateWindowExW(0, (char*)_className, null, 0, 0, 0, 0, 0, MessageOnlyParent, 0, instance, 0);
        if (_window == 0)
        {
            _ = UnregisterClassW((char*)_className, instance);
            Marshal.FreeHGlobal(_className);
            throw new InvalidOperationException("The notification-area icon's window could not be created.");
        }

        ByWindow[_window] = this;
        _icon = WindowIcon.CreateWindowsIcon(GetSystemMetrics(SmallIconWidth));
        Add();
    }

    /// <summary>Occurs when the icon is clicked: the program's window should come back.</summary>
    public event EventHandler? Activated;

    /// <summary>Shows a notification from the icon, which Windows 10 and 11 show as a toast.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">The line under it.</param>
    public void Notify(string title, string message)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        NotifyIconData data = NewData();
        data.Flags = NifInfo;
        data.InfoFlags = _icon != 0 ? NiifUser | NiifLargeIcon : 0;
        data.BalloonIcon = _icon;
        Copy(title, data.InfoTitle, 64);
        Copy(message, data.Info, 256);
        _ = ShellNotifyIconW(NimModify, &data);
    }

    /// <summary>Removes the icon and releases its window.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_added)
        {
            NotifyIconData data = NewData();
            _ = ShellNotifyIconW(NimDelete, &data);
        }

        ByWindow.Remove(_window);
        _ = DestroyWindow(_window);
        _ = UnregisterClassW((char*)_className, GetModuleHandleW(null));
        Marshal.FreeHGlobal(_className);
        if (_icon != 0)
        {
            _ = DestroyIcon(_icon);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (ByWindow.TryGetValue(window, out TrayIcon? tray))
        {
            if (message == CallbackMessage)
            {
                // An exception cannot unwind through the system's call into this procedure: it would end the
                // process. A failing click handler costs that click, not the program.
                try
                {
                    tray.OnIconMessage((uint)lParam);
                }
#pragma warning disable CA1031 // See above: nothing may escape a window procedure.
                catch (Exception)
#pragma warning restore CA1031
                {
                }

                return 0;
            }

            // Explorer restarted, and every notification-area icon with it.
            if (message == TaskbarCreated && TaskbarCreated != 0)
            {
                tray.Add();
                return 0;
            }
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private static void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    private void Add()
    {
        NotifyIconData data = NewData();
        data.Flags = NifMessage | NifIcon | NifTip;
        data.CallbackMessage = CallbackMessage;
        data.Icon = _icon;
        Copy(_tooltip, data.Tip, 128);
        _added = ShellNotifyIconW(NimAdd, &data) || ShellNotifyIconW(NimModify, &data);
    }

    private void OnIconMessage(uint mouseMessage)
    {
        switch (mouseMessage)
        {
            case WmLeftButtonUp or WmLeftButtonDoubleClick:
                Activated?.Invoke(this, EventArgs.Empty);
                break;
            case WmRightButtonUp or WmContextMenu:
                ShowMenu();
                break;
        }
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            for (int i = 0; i < _menu.Count; i++)
            {
                _ = AppendMenuW(menu, 0, (nuint)(i + 1), _menu[i].Text);
            }

            // Without the foreground, the menu would not close when the player clicks elsewhere.
            _ = GetCursorPos(out Point cursor);
            _ = SetForegroundWindow(_window);
            int chosen = TrackPopupMenu(menu, TpmReturnCommand | TpmRightButton | TpmNoNotify, cursor.X, cursor.Y, 0, _window, 0);
            _ = PostMessageW(_window, WmNull, 0, 0);
            if (chosen > 0 && chosen <= _menu.Count)
            {
                _menu[chosen - 1].Run();
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private NotifyIconData NewData() => new()
    {
        Size = (uint)sizeof(NotifyIconData),
        Window = _window,
        Id = 1,
    };

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WindowClass* windowClass);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(char* className, nint instance);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(
        uint extendedStyle, char* className, char* windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rectangle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>WNDCLASSEXW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    /// <summary>NOTIFYICONDATAW, as Windows Vista and later lay it out.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }
}

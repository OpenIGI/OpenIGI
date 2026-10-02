using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenIGI.Launcher.Services;

/// <summary>Shows a notification on the desktop, the way each system does it, with nothing to install.</summary>
/// <remarks>
/// <para>
/// Windows: the shell's notification-area API (<c>Shell_NotifyIconW</c> with an info balloon), which Windows 10
/// and 11 show as a toast. It needs neither WinRT nor an app registration; the launcher's icon sits in the
/// notification area from the first notification until the launcher closes.
/// </para>
/// <para>
/// Linux: <c>notify-send</c>, which every desktop's notification daemon answers, else <c>gdbus</c> straight to
/// <c>org.freedesktop.Notifications</c>. macOS: <c>osascript</c>'s <c>display notification</c>. Both are started
/// with an argument list, never through a shell, and never waited on; a system without either shows nothing,
/// and the launcher's own UPDATE button still says it.
/// </para>
/// </remarks>
internal sealed partial class DesktopNotifier(Func<nint> windowHandle) : IDisposable
{
    private const string AppName = "OpenIGI Launcher";
    private bool _iconAdded;

    /// <summary>Shows a notification. Never throws: a notification is a courtesy, not something to fail on.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">The line under it.</param>
    public void Show(string title, string message)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                ShowWindows(title, message);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Run("osascript", "-e", $"display notification {AppleString(message)} with title {AppleString(title)}");
            }
            else if (!Run("notify-send", "--app-name", AppName, title, message))
            {
                Run(
                    "gdbus", "call", "--session", "--dest", "org.freedesktop.Notifications",
                    "--object-path", "/org/freedesktop/Notifications", "--method", "org.freedesktop.Notifications.Notify",
                    AppName, "0", string.Empty, title, message, "[]", "{}", "10000");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                       or DllNotFoundException or EntryPointNotFoundException)
        {
            // No notification system here; the launcher's own button is the announcement.
        }
    }

    /// <summary>Removes the notification-area icon, if one was added.</summary>
    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && _iconAdded)
        {
            NotifyIconData data = NewData(windowHandle());
            _ = ShellNotifyIcon(NimDelete, ref data);
            _iconAdded = false;
        }
    }

    // An AppleScript string literal: backslashes and quotes escaped, so the text cannot end the literal early.
    private static string AppleString(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static bool Run(string program, params string[] arguments)
    {
        ProcessStartInfo start = new(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(start);
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private unsafe void ShowWindows(string title, string message)
    {
        nint hwnd = windowHandle();
        if (hwnd == 0)
        {
            return;
        }

        NotifyIconData data = NewData(hwnd);
        data.Flags = NifIcon | NifTip | NifInfo;
        data.Icon = LoadIcon(0, IdiApplication);
        data.InfoFlags = NiifInfo;
        Copy(AppName, data.Tip, 128);
        Copy(title, data.InfoTitle, 64);
        Copy(message, data.Info, 256);

        _iconAdded = ShellNotifyIcon(_iconAdded ? NimModify : NimAdd, ref data) || _iconAdded;
    }

    private static NotifyIconData NewData(nint hwnd) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = hwnd,
        Id = 1,
    };

    private static unsafe void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifIcon = 0x02;
    private const uint NifTip = 0x04;
    private const uint NifInfo = 0x10;
    private const uint NiifInfo = 0x01;
    private const nint IdiApplication = 32512;

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial nint LoadIcon(nint instance, nint name);

    /// <summary>NOTIFYICONDATAW, as Windows Vista and later lay it out.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NotifyIconData
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

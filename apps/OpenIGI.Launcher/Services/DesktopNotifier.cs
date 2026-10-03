using System.Diagnostics;
using OpenIGI.Platform.Desktop;

namespace OpenIGI.Launcher.Services;

/// <summary>Shows a notification on the desktop, the way each system does it, with nothing to install.</summary>
/// <remarks>
/// <para>
/// Windows: from the launcher's notification-area icon (<see cref="TrayIcon"/>), which Windows 10 and 11 show
/// as a toast. It needs neither WinRT nor an app registration.
/// </para>
/// <para>
/// Linux: <c>notify-send</c>, which every desktop's notification daemon answers, else <c>gdbus</c> straight to
/// <c>org.freedesktop.Notifications</c>. macOS: <c>osascript</c>'s <c>display notification</c>. Both are started
/// with an argument list, never through a shell, and never waited on; a system without either shows nothing,
/// and the launcher's own UPDATE button still says it.
/// </para>
/// </remarks>
/// <param name="tray">The launcher's notification-area icon on Windows; <see langword="null"/> elsewhere.</param>
internal sealed class DesktopNotifier(TrayIcon? tray)
{
    private const string AppName = "OpenIGI Launcher";

    /// <summary>Shows a notification. Never throws: a notification is a courtesy, not something to fail on.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">The line under it.</param>
    public void Show(string title, string message)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                tray?.Notify(title, message);
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
                                       or ObjectDisposedException or DllNotFoundException or EntryPointNotFoundException)
        {
            // No notification system here; the launcher's own button is the announcement.
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
}

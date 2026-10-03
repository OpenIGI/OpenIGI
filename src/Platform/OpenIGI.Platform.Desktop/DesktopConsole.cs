using System.Runtime.InteropServices;

namespace OpenIGI.Platform.Desktop;

/// <summary>Connects a windowed game to the terminal it was started from, if any.</summary>
/// <remarks>
/// <para>
/// On Windows the game is a windowed (WinExe) program, so starting it never opens a console window of its
/// own. That also means Windows gives it no console at all: started from a terminal, its output would go
/// nowhere. <see cref="AttachToParent"/> borrows the parent's console in that case, so a capture or soak run
/// from a shell still prints its log. When the output is already redirected — a launcher reading it, a
/// script piping it to a file — the inherited handles are used as they are.
/// </para>
/// <para>
/// On macOS and Linux a program always has its parent's streams, and none of this applies.
/// </para>
/// </remarks>
public static class DesktopConsole
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint MessageBoxIconError = 0x00000010;

    /// <summary>Gets a value indicating whether standard output reaches anything: a terminal, a pipe or a file.</summary>
    /// <remarks>Answered by <see cref="AttachToParent"/>; always true off Windows.</remarks>
    public static bool HasOutput { get; private set; } = !OperatingSystem.IsWindows();

    /// <summary>On Windows, attaches to the parent's console when output is not already redirected. Call before anything touches <c>System.Console</c>.</summary>
    /// <returns><see cref="HasOutput"/>.</returns>
    public static bool AttachToParent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return HasOutput = true;
        }

        if (IsValid(GetStdHandle(StdOutputHandle)))
        {
            return HasOutput = true;
        }

        if (AttachConsole(AttachParentProcess) == 0)
        {
            return HasOutput = false;
        }

        // A windowed program attached late has no standard handles of its own; point both at the console.
        nint console = CreateFileW("CONOUT$", GenericWrite, FileShareReadWrite, 0, OpenExisting, 0, 0);
        if (!IsValid(console))
        {
            return HasOutput = false;
        }

        return HasOutput = SetStdHandle(StdOutputHandle, console) != 0 && SetStdHandle(StdErrorHandle, console) != 0;
    }

    /// <summary>Tells the player why the game stopped, when there is nowhere it could be printed.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="text">What went wrong and what to do about it.</param>
    /// <remarks>Shows a dialog on Windows when <see cref="HasOutput"/> is false; does nothing otherwise, since the log already holds the text.</remarks>
    public static void ShowFailure(string title, string text)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);

        if (OperatingSystem.IsWindows() && !HasOutput)
        {
            _ = MessageBoxW(0, text, title, MessageBoxIconError);
        }
    }

    private static bool IsValid(nint handle) => handle != 0 && handle != -1;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int AttachConsole(int processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int SetStdHandle(int standardHandle, nint handle);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateFileW(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}

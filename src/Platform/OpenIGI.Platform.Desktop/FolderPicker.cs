using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenIGI.Platform.Desktop;

/// <summary>Asks the player for a folder with the system's own folder picker.</summary>
/// <remarks>
/// <para>
/// Windows: the shell's file dialog in folder mode (<c>IFileOpenDialog</c> with <c>FOS_PICKFOLDERS</c>), the
/// one Explorer uses, on a thread of its own because the dialog wants a single-threaded COM apartment and
/// runs its own message loop. It is owned by the caller's window, so it stays in front of it.
/// </para>
/// <para>
/// macOS: <c>osascript</c>'s <c>choose folder</c>. Linux: <c>zenity</c>, else <c>kdialog</c>, whichever the
/// desktop has; with neither, there is no picker and the answer is <see langword="null"/>, the same as cancelling.
/// </para>
/// </remarks>
public static partial class FolderPicker
{
    /// <summary>Shows the picker and waits for the player's answer without blocking the caller's thread.</summary>
    /// <param name="title">What the picker asks.</param>
    /// <param name="initial">The folder it opens on, if it exists.</param>
    /// <param name="owner">The window it belongs to, on Windows; 0 for none.</param>
    /// <returns>The folder chosen, or <see langword="null"/> when the player cancelled or there is no picker.</returns>
    public static Task<string?> PickAsync(string title, string? initial, nint owner = 0)
    {
        ArgumentNullException.ThrowIfNull(title);
        string? start = Directory.Exists(initial) ? Path.GetFullPath(initial) : null;

        return OperatingSystem.IsWindows()
            ? PickOnOwnThread(title, start, owner)
            : Task.Run(() => OperatingSystem.IsMacOS() ? PickMac(title, start) : PickLinux(title, start));
    }

    [SupportedOSPlatform("windows")]
    private static Task<string?> PickOnOwnThread(string title, string? start, nint owner)
    {
        TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                answer.SetResult(PickWindows(title, start, owner));
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                answer.SetResult(null);
            }
        })
        {
            IsBackground = true,
            Name = "Folder picker",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return answer.Task;
    }

    private static string? PickMac(string title, string? start)
    {
        string script = $"POSIX path of (choose folder with prompt {AppleString(title)}"
            + (start is null ? string.Empty : $" default location POSIX file {AppleString(start)}") + ")";
        return Run("osascript", "-e", script);
    }

    private static string? PickLinux(string title, string? start)
    {
        string initial = start is null ? string.Empty : start.TrimEnd('/') + "/";
        return Run("zenity", "--file-selection", "--directory", $"--title={title}", $"--filename={initial}")
            ?? Run("kdialog", "--getexistingdirectory", start ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "--title", title);
    }

    // The program's answer on standard output, or null when it is missing, was cancelled or said nothing.
    private static string? Run(string program, params string[] arguments)
    {
        ProcessStartInfo start = new(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || output.Length == 0)
            {
                return null;
            }

            // macOS answers "/Users/me/Games/"; the trailing separator is dropped, except from the root itself.
            return output.Length > 1 ? output.TrimEnd('/') : output;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string AppleString(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [SupportedOSPlatform("windows")]
    private static unsafe string? PickWindows(string title, string? start, nint owner)
    {
        const uint ApartmentThreaded = 0x2;
        const uint ClassContextInProcess = 0x1;
        const uint PickFolders = 0x20;
        const uint ForceFileSystem = 0x40;
        const uint NoChangeDirectory = 0x8;
        const uint FileSystemPath = 0x80058000; // SIGDN_FILESYSPATH
        const int Cancelled = unchecked((int)0x800704C7);

        _ = CoInitializeEx(0, ApartmentThreaded);
        nint dialog = 0;
        nint result = 0;
        nint folder = 0;
        try
        {
            Guid classId = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7"); // FileOpenDialog
            Guid fileDialog = new("42f85136-db7e-439c-85f1-e4075d135fc8"); // IFileDialog
            Marshal.ThrowExceptionForHR(CoCreateInstance(&classId, 0, ClassContextInProcess, &fileDialog, &dialog));
            void** methods = *(void***)dialog;

            // IFileDialog's slots after IUnknown's three: Show 3, SetOptions 9, GetOptions 10, SetFolder 12,
            // SetTitle 17, GetResult 20.
            uint options;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint*, int>)methods[10])(dialog, &options));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, int>)methods[9])(
                dialog, options | PickFolders | ForceFileSystem | NoChangeDirectory));

            fixed (char* caption = title)
            {
                _ = ((delegate* unmanaged[Stdcall]<nint, char*, int>)methods[17])(dialog, caption);
            }

            if (start is not null)
            {
                Guid shellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe"); // IShellItem
                fixed (char* path = start)
                {
                    if (SHCreateItemFromParsingName(path, 0, &shellItem, &folder) >= 0)
                    {
                        _ = ((delegate* unmanaged[Stdcall]<nint, nint, int>)methods[12])(dialog, folder);
                    }
                }
            }

            int shown = ((delegate* unmanaged[Stdcall]<nint, nint, int>)methods[3])(dialog, owner);
            if (shown == Cancelled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(shown);
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)methods[20])(dialog, &result));

            // IShellItem: GetDisplayName is slot 5.
            char* name;
            void** itemMethods = *(void***)result;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)itemMethods[5])(result, FileSystemPath, &name));
            try
            {
                return new string(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem((nint)name);
            }
        }
        finally
        {
            Release(folder);
            Release(result);
            Release(dialog);
            CoUninitialize();
        }
    }

    private static unsafe void Release(nint unknown)
    {
        if (unknown != 0)
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)unknown)[2])(unknown);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* instance);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHCreateItemFromParsingName(char* path, nint bindContext, Guid* interfaceId, nint* item);
}

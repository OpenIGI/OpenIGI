using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.D3D12.Interop;

/// <summary>The three Win32 calls a fence wait needs.</summary>
internal static partial class Kernel32
{
    /// <summary>Creates an auto-reset event a fence signals.</summary>
    /// <param name="attributes">Security attributes; nought.</param>
    /// <param name="manualReset">Whether it resets by hand.</param>
    /// <param name="initialState">Whether it starts signalled.</param>
    /// <param name="name">Its name; nought.</param>
    /// <returns>The event handle.</returns>
    [LibraryImport("kernel32", SetLastError = true)]
    public static partial nint CreateEventW(nint attributes, int manualReset, int initialState, nint name);

    /// <summary>Waits for an event.</summary>
    /// <param name="handle">The event.</param>
    /// <param name="milliseconds">How long, or <see cref="uint.MaxValue"/> for ever.</param>
    /// <returns>Why it returned.</returns>
    [LibraryImport("kernel32", SetLastError = true)]
    public static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    /// <summary>Closes a handle.</summary>
    /// <param name="handle">The handle.</param>
    /// <returns>Non-zero on success.</returns>
    [LibraryImport("kernel32", SetLastError = true)]
    public static partial int CloseHandle(nint handle);
}

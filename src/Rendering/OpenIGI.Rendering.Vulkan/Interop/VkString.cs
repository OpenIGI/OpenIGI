using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Interop;

/// <summary>Reads the fixed-size UTF-8 name buffers Vulkan reports in its property structures.</summary>
/// <remarks>
/// Layer, extension, and device names come back as inline <c>char[256]</c> buffers rather than as
/// pointers, so they are read where they sit. The buffers are null-terminated, which is what
/// <see cref="Marshal.PtrToStringAnsi(nint)"/> stops at.
/// </remarks>
internal static class VkString
{
    /// <summary>Reads a null-terminated UTF-8 string from a fixed buffer.</summary>
    /// <param name="utf8">A pointer to the first byte of the buffer.</param>
    /// <returns>The decoded string, empty when the pointer is null.</returns>
    public static unsafe string Read(byte* utf8) =>
        utf8 is null ? string.Empty : Marshal.PtrToStringAnsi((nint)utf8) ?? string.Empty;
}

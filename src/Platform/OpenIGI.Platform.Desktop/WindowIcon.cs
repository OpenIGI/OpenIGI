using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.Core;
using Silk.NET.Windowing;

namespace OpenIGI.Platform.Desktop;

/// <summary>The OpenIGI icon, for a window's title bar, the taskbar, the window switcher and the notification area.</summary>
/// <remarks>
/// <para>
/// A GLFW window has no icon of its own: the one embedded in the executable is the file's, and the window
/// gets the system's blank default however it was started. So the icon is set on the window, from the same
/// <c>openigi.ico</c> both executables carry, embedded here once for the game and the launcher alike.
/// </para>
/// <para>
/// Not on macOS, where windows have no icons and GLFW reports the attempt as an error; the Dock shows the
/// application bundle's.
/// </para>
/// </remarks>
public static partial class WindowIcon
{
    private const string ResourceName = "OpenIGI.Platform.Desktop.Assets.openigi.ico";
    private const int DirectoryEntrySize = 16;
    private const int BitmapHeaderSize = 40;

    private static readonly Lazy<byte[]> File = new(ReadFile);

    // Every size the icon file carries, decoded once; the platform picks the one nearest each use.
    private static readonly Lazy<RawImage[]> Images = new(() => Decode(File.Value));

    /// <summary>Sets the icon on a window that has been initialised.</summary>
    /// <param name="window">The window.</param>
    public static void Apply(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (OperatingSystem.IsMacOS() || Images.Value.Length == 0)
        {
            return;
        }

        window.SetWindowIcon(Images.Value);
    }

    /// <summary>Creates a Windows icon handle from the frame nearest a size, for the notification area.</summary>
    /// <param name="size">The size wanted, in pixels.</param>
    /// <returns>The handle, which the caller destroys with <c>DestroyIcon</c>; 0 when none could be made.</returns>
    [SupportedOSPlatform("windows")]
    public static unsafe nint CreateWindowsIcon(int size)
    {
        // An .ico frame is exactly the icon resource Windows builds handles from, so it is handed over as it is.
        if (FindFrame(File.Value, size) is not { } frame)
        {
            return 0;
        }

        fixed (byte* data = &File.Value[frame.Offset])
        {
            return CreateIconFromResourceEx(data, (uint)frame.Length, icon: 1, version: 0x00030000, size, size, flags: 0);
        }
    }

    private static byte[] ReadFile()
    {
        using Stream? stream = typeof(WindowIcon).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return [];
        }

        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // The directory's frames, as (width, offset, length), skipping any entry that points outside the file.
    private static IEnumerable<(int Width, int Offset, int Length)> Frames(byte[] file)
    {
        if (file.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(2)) != 1)
        {
            yield break;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4));
        for (int i = 0; i < count && 6 + ((i + 1) * DirectoryEntrySize) <= file.Length; i++)
        {
            ReadOnlySpan<byte> entry = file.AsSpan(6 + (i * DirectoryEntrySize), DirectoryEntrySize);
            int width = entry[0] == 0 ? 256 : entry[0];
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (offset >= 0 && length > BitmapHeaderSize && offset + length <= file.Length)
            {
                yield return (width, offset, length);
            }
        }
    }

    // The smallest frame at least as large as asked for, else the largest there is.
    private static (int Width, int Offset, int Length)? FindFrame(byte[] file, int size)
    {
        (int Width, int Offset, int Length)? best = null;
        foreach ((int Width, int Offset, int Length) frame in Frames(file))
        {
            if (best is not { } current)
            {
                best = frame;
            }
            else if (frame.Width >= size)
            {
                // Large enough: the closest of those, and any of those beats one that is too small.
                if (current.Width < size || frame.Width < current.Width)
                {
                    best = frame;
                }
            }
            else if (current.Width < size && frame.Width > current.Width)
            {
                best = frame;
            }
        }

        return best;
    }

    // This file's frames are 32-bit bitmaps: a BITMAPINFOHEADER, then bottom-up BGRA rows, then a mask the
    // alpha channel makes redundant. A frame in any other form is skipped rather than guessed at.
    private static RawImage[] Decode(byte[] file)
    {
        List<RawImage> images = [];
        foreach ((_, int offset, int length) in Frames(file))
        {
            ReadOnlySpan<byte> frame = file.AsSpan(offset, length);
            int header = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame);
            int width = BinaryPrimitives.ReadInt32LittleEndian(frame[4..]);
            int height = BinaryPrimitives.ReadInt32LittleEndian(frame[8..]) / 2;
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(frame[14..]);
            if (header != BitmapHeaderSize || bits != 32 || width <= 0 || height <= 0 || header + (width * height * 4) > frame.Length)
            {
                continue;
            }

            byte[] rgba = new byte[width * height * 4];
            ReadOnlySpan<byte> pixels = frame[header..];
            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<byte> source = pixels.Slice((height - 1 - y) * width * 4, width * 4);
                Span<byte> target = rgba.AsSpan(y * width * 4, width * 4);
                for (int x = 0; x < width * 4; x += 4)
                {
                    target[x] = source[x + 2];
                    target[x + 1] = source[x + 1];
                    target[x + 2] = source[x];
                    target[x + 3] = source[x + 3];
                }
            }

            images.Add(new RawImage(width, height, rgba));
        }

        return [.. images];
    }

    [LibraryImport("user32.dll")]
    private static unsafe partial nint CreateIconFromResourceEx(byte* data, uint length, int icon, uint version, int width, int height, uint flags);
}

using System.Buffers.Binary;
using Silk.NET.Core;
using Silk.NET.Windowing;

namespace OpenIGI.Platform.Desktop;

/// <summary>Gives a window the OpenIGI icon, which the title bar, the taskbar and the window switcher show.</summary>
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
public static class WindowIcon
{
    private const string ResourceName = "OpenIGI.Platform.Desktop.Assets.openigi.ico";

    // Every size the icon file carries, decoded once; the platform picks the one nearest each use.
    private static readonly Lazy<RawImage[]> Images = new(Load);

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

    private static RawImage[] Load()
    {
        using Stream? stream = typeof(WindowIcon).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return [];
        }

        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return Decode(copy.ToArray());
    }

    // An .ico is a directory of frames. This one's frames are 32-bit bitmaps: a BITMAPINFOHEADER, then
    // bottom-up BGRA rows, then a mask the alpha channel makes redundant. A frame in any other form is
    // skipped rather than guessed at.
    private static RawImage[] Decode(ReadOnlySpan<byte> file)
    {
        if (file.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(file[2..]) != 1)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(file[4..]);
        List<RawImage> images = [];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = file.Slice(6 + (i * 16), 16);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (offset < 0 || size <= 40 || offset + size > file.Length)
            {
                continue;
            }

            ReadOnlySpan<byte> frame = file.Slice(offset, size);
            int header = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame);
            int width = BinaryPrimitives.ReadInt32LittleEndian(frame[4..]);
            int height = BinaryPrimitives.ReadInt32LittleEndian(frame[8..]) / 2;
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(frame[14..]);
            if (header != 40 || bits != 32 || width <= 0 || height <= 0 || header + (width * height * 4) > frame.Length)
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
}

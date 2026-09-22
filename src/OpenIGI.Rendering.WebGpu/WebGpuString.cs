using System.Runtime.InteropServices;
using System.Text;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// A string the C API can read, for as long as this is alive.
/// </summary>
/// <remarks>
/// <para>
/// WebGPU takes every string as a pointer and a length in UTF-8 bytes. A managed array cannot be handed
/// over as one: the collector may move it between the call being made and the driver reading it, and
/// what arrives is whatever now sits at the old address. So the bytes are copied into unmanaged memory
/// that does not move, and freed when this is disposed.
/// </para>
/// <para>
/// A class rather than a struct, and deliberately. A struct holding unmanaged memory is copied by
/// assignment and by being passed anywhere, and each copy's disposal frees the same allocation — a
/// double free, which corrupts the heap and reports itself somewhere else entirely. Every string here is
/// a label or a shader entry point, created once with a resource rather than per frame, so what an
/// allocation costs is not on any path that matters.
/// </para>
/// </remarks>
public sealed unsafe class WebGpuString : IDisposable
{
    private byte* _bytes;
    private nuint _length;

    /// <summary>Initialises a new instance of the <see cref="WebGpuString"/> class.</summary>
    /// <param name="text">The text, which may be empty.</param>
    public WebGpuString(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int count = Encoding.UTF8.GetByteCount(text);
        _bytes = (byte*)NativeMemory.Alloc((nuint)count);
        _length = (nuint)count;

        Encoding.UTF8.GetBytes(text, new Span<byte>(_bytes, count));
    }

    /// <summary>Gets the view to pass to the C API.</summary>
    /// <remarks>
    /// An empty string is a null pointer with a zero length, which the API reads as "no string" rather
    /// than as the empty one. The difference matters for a label and for nothing else.
    /// </remarks>
    public WGPUStringView View => new() { data = _bytes, length = _length };

    /// <summary>Gets how many bytes the text occupies.</summary>
    public int ByteCount => (int)_length;

    /// <summary>Reads a string the C API returned.</summary>
    /// <param name="view">The view it returned.</param>
    /// <returns>The text, or an empty string when there was none.</returns>
    public static string Read(WGPUStringView view) =>
        view.data is null || view.length == 0
            ? string.Empty
            : Encoding.UTF8.GetString(view.data, (int)view.length);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_bytes is null)
        {
            return;
        }

        NativeMemory.Free(_bytes);
        _bytes = null;
        _length = 0;
        GC.SuppressFinalize(this);
    }

    /// <summary>Frees the allocation for a string nobody disposed.</summary>
    /// <remarks>
    /// A backstop, not a plan. Everything that makes one of these owns it and disposes it with the
    /// resource it labels; this only stops a leak becoming permanent when something forgets.
    /// </remarks>
    ~WebGpuString()
    {
        if (_bytes is not null)
        {
            NativeMemory.Free(_bytes);
            _bytes = null;
        }
    }
}

using System.Globalization;
using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// A stage's specialisation constants, in the form WebGPU overrides them with.
/// </summary>
/// <remarks>
/// <para>
/// The one place the two backends genuinely differ about what a constant <em>is</em>. Vulkan takes four
/// raw bytes and lets the module's own declaration say what they mean; WebGPU takes a <c>double</c> and
/// converts it to the declared type. So the bits of a float and the bits of an integer are
/// interchangeable on one side and not on the other, which is why the RHI carries what kind of value a
/// constant is.
/// </para>
/// <para>
/// The key is the identifier written as a decimal number, which is what WebGPU defines for an override
/// declared with <c>@id(n)</c>. Not its name: Slang mangles those on the way out — <c>cClipYDown</c>
/// is emitted as <c>cClipYDown_0</c> — so a name taken from the reflection is one the driver has never
/// heard of. The reflection is still read, to refuse an identifier the module does not declare with
/// something a reader can act on rather than a panic from inside wgpu.
/// </para>
/// <para>
/// The keys and the array outlive the pipeline creation call because this holds them, and are released
/// once it returns.
/// </para>
/// </remarks>
internal sealed unsafe class WebGpuConstants : IDisposable
{
    private readonly List<WebGpuString> _keys = [];
    private readonly nint _entries;
    private readonly int _count;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuConstants"/> class.</summary>
    /// <param name="stage">The stage, or null for one that is not there.</param>
    public WebGpuConstants(RhiShaderStage? stage)
    {
        if (stage?.Constants is not { Count: > 0 } constants)
        {
            return;
        }

        WebGpuShaderModule module = (WebGpuShaderModule)stage.Module;

        _count = constants.Count;
        _entries = Marshal.AllocHGlobal(sizeof(WGPUConstantEntry) * _count);

        WGPUConstantEntry* entries = (WGPUConstantEntry*)_entries;
        int slot = 0;

        foreach ((int id, RhiConstant constant) in constants)
        {
            // Read for its side effect: it throws, naming the module and the identifier, when the
            // module declares no such constant.
            _ = module.ConstantName(id);

            WebGpuString key = new(id.ToString(CultureInfo.InvariantCulture));
            _keys.Add(key);

            entries[slot] = new WGPUConstantEntry
            {
                key = key.View,
                value = constant.Number,
            };

            slot++;
        }
    }

    /// <summary>Gets how many constants there are.</summary>
    public nuint Count => (nuint)_count;

    /// <summary>Gets the entries, or null when there are none.</summary>
    public WGPUConstantEntry* Entries => _count > 0 ? (WGPUConstantEntry*)_entries : null;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (WebGpuString key in _keys)
        {
            key.Dispose();
        }

        _keys.Clear();

        if (_count > 0)
        {
            Marshal.FreeHGlobal(_entries);
        }
    }
}

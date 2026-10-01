using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// A stage's specialisation constants, laid out the way Vulkan wants to read them.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SpecializationInfo"/> is a pair of pointers and nothing else, so whatever it points at
/// has to outlive the pipeline creation call. This owns that storage and is disposed once the pipeline
/// exists — which is also why it is a class rather than something built inline: the alternative is a
/// stack frame that happens to be the right one, and a constant block that silently reads whatever is
/// at that address is not a failure anybody notices.
/// </para>
/// <para>
/// Every constant is four bytes here. That is what the RHI carries — a map of identifier to raw bits —
/// because it is the shape both backends can express: a float, an int and a bool all fit, and the
/// shader's own declaration is what says which it is.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanSpecialization : IDisposable
{
    private readonly nint _data;
    private readonly nint _entries;
    private readonly int _count;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanSpecialization"/> class.</summary>
    /// <param name="constants">The constants by identifier, or null for a stage with none.</param>
    public VulkanSpecialization(IReadOnlyDictionary<int, RhiConstant>? constants)
    {
        if (constants is not { Count: > 0 })
        {
            return;
        }

        _count = constants.Count;
        _data = Marshal.AllocHGlobal(sizeof(uint) * _count);
        _entries = Marshal.AllocHGlobal(sizeof(SpecializationMapEntry) * _count);

        uint* values = (uint*)_data;
        SpecializationMapEntry* map = (SpecializationMapEntry*)_entries;
        int slot = 0;

        foreach ((int id, RhiConstant constant) in constants)
        {
            values[slot] = constant.Bits;
            map[slot] = new SpecializationMapEntry
            {
                ConstantID = (uint)id,
                Offset = (uint)(sizeof(uint) * slot),
                Size = sizeof(uint),
            };

            slot++;
        }
    }

    /// <summary>Gets whether there is anything to supply.</summary>
    public bool HasConstants => _count > 0;

    /// <summary>Gets what a stage points at, or null for a stage with no constants.</summary>
    /// <param name="info">Storage for the description, which must outlive the pipeline creation call.</param>
    /// <returns>The pointer to hand the stage.</returns>
    public SpecializationInfo* Describe(SpecializationInfo* info)
    {
        if (!HasConstants)
        {
            return null;
        }

        *info = new SpecializationInfo
        {
            MapEntryCount = (uint)_count,
            PMapEntries = (SpecializationMapEntry*)_entries,
            DataSize = (nuint)(sizeof(uint) * _count),
            PData = (void*)_data,
        };

        return info;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed || !HasConstants)
        {
            _disposed = true;
            return;
        }

        _disposed = true;
        Marshal.FreeHGlobal(_data);
        Marshal.FreeHGlobal(_entries);
    }
}

using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Capabilities;

/// <summary>
/// Builds a Vulkan <c>pNext</c> chain of extension structures whose addresses stay valid for as long as
/// the chain is alive.
/// </summary>
/// <remarks>
/// <para>
/// Vulkan extends a structure by linking further structures off its <c>pNext</c> pointer, and every one
/// of them must still be at the address it was linked at when the call is made. That rules out ordinary
/// locals in C#: a managed local can only be pinned for the body of a <c>fixed</c> block, and a chain of
/// a dozen optional structures would need a dozen nested ones. Allocating each link on the unmanaged
/// heap instead gives an address that simply does not move, and makes the chain something a method can
/// build up conditionally and hand back.
/// </para>
/// <para>
/// Every Vulkan extension structure begins with the same two fields — a 32-bit structure type and then,
/// after padding, the <c>pNext</c> pointer — so on a 64-bit target <c>pNext</c> is always the second
/// pointer-sized slot. That is what lets the link be written here without knowing which structure is
/// being linked.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanStructChain : IDisposable
{
    private const int PNextSlot = 1;

    private readonly List<nint> _blocks = [];
    private bool _disposed;

    /// <summary>Gets the head of the chain, to assign to the root structure's <c>pNext</c>.</summary>
    public void* Head { get; private set; }

    /// <summary>Copies a structure onto the unmanaged heap and links it into the chain.</summary>
    /// <typeparam name="T">The Vulkan extension structure type.</typeparam>
    /// <param name="value">The structure to copy, with its structure type already set.</param>
    /// <returns>A pointer to the copy, stable until the chain is disposed.</returns>
    public T* Add<T>(in T value)
        where T : unmanaged
    {
        void* block = NativeMemory.AllocZeroed((nuint)sizeof(T));
        *(T*)block = value;
        ((void**)block)[PNextSlot] = Head;
        Head = block;
        _blocks.Add((nint)block);
        return (T*)block;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Head = null;
        foreach (nint block in _blocks)
        {
            NativeMemory.Free((void*)block);
        }

        _blocks.Clear();
    }
}

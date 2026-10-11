using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Descriptors;

/// <summary>Render-target or depth-stencil descriptors, in heaps the GPU never sees, added a page at a time.</summary>
/// <remarks>
/// <para>
/// A pass binds these by copying their CPU handles into the command list, so unlike the shader-visible heaps
/// they need not all live in one heap: a new page costs a few kilobytes and nothing that is already recorded
/// refers to the heap a handle came from. A fixed heap was a ceiling the renderer could reach honestly -- the
/// surface cache's lamp bounce maps take one depth view per layer, up to a thousand and twenty of them, and
/// with the lamp shadow maps and the cascades beside them the first heap of 1024 ran out the moment ray
/// tracing was switched off.
/// </para>
/// <para>
/// A descriptor's index names its page and its place in it, so freeing one finds its page without a search.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12AttachmentDescriptors : IDisposable
{
    private readonly ID3D12Device10* _device;
    private readonly DescriptorHeapType _type;
    private readonly int _pageSize;
    private readonly string _label;
    private readonly List<D3D12DescriptorHeap> _pages = [];

    /// <summary>Initialises a new instance of the <see cref="D3D12AttachmentDescriptors"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="type">Render-target or depth-stencil.</param>
    /// <param name="pageSize">How many descriptors each page holds.</param>
    /// <param name="label">A name for captures; each page is numbered after it.</param>
    public D3D12AttachmentDescriptors(ID3D12Device10* device, DescriptorHeapType type, int pageSize, string label)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        _device = device;
        _type = type;
        _pageSize = pageSize;
        _label = label;
        AddPage();
    }

    /// <summary>Gets how many descriptors are handed out.</summary>
    public int InUse => _pages.Sum(static page => page.InUse);

    /// <summary>Gets how many the pages made so far hold.</summary>
    public int Capacity => _pages.Count * _pageSize;

    /// <summary>Takes one descriptor, adding a page when every page is full.</summary>
    /// <returns>Its index.</returns>
    public int Allocate()
    {
        for (int page = 0; page < _pages.Count; page++)
        {
            if (_pages[page].InUse < _pageSize)
            {
                return (page * _pageSize) + _pages[page].Allocate(1);
            }
        }

        AddPage();
        return ((_pages.Count - 1) * _pageSize) + _pages[^1].Allocate(1);
    }

    /// <summary>Gives a descriptor back.</summary>
    /// <param name="index">Its index.</param>
    public void Free(int index) => _pages[index / _pageSize].Free(index % _pageSize, 1);

    /// <summary>The CPU handle a view is written through and a pass binds.</summary>
    /// <param name="index">The descriptor.</param>
    /// <returns>The handle.</returns>
    public CpuDescriptorHandle Cpu(int index) => _pages[index / _pageSize].Cpu(index % _pageSize);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (D3D12DescriptorHeap page in _pages)
        {
            page.Dispose();
        }

        _pages.Clear();
    }

    private void AddPage() =>
        _pages.Add(new D3D12DescriptorHeap(_device, _type, _pageSize, shaderVisible: false, $"{_label} {_pages.Count}"));
}

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Interop;

/// <summary>The class identifiers Silk.NET does not carry, from <c>d3d12.h</c>.</summary>
internal static class D3D12Classes
{
    /// <summary><c>CLSID_D3D12SDKConfiguration</c>: the Agility SDK's way in from a process that exports nothing.</summary>
    public static readonly Guid SdkConfiguration = new("7cda6aca-a03e-49c8-9458-0334d20e07ce");

    /// <summary><c>CLSID_D3D12Debug</c>: the debug layer, asked of the device factory.</summary>
    public static readonly Guid Debug = new("f2352aeb-dd84-49fe-b97b-a9dcfdcc1b4f");

    /// <summary><c>CLSID_D3D12DeviceRemovedExtendedData</c>: DRED's settings, asked of the device factory.</summary>
    public static readonly Guid DeviceRemovedExtendedData = new("4a75bbc4-9ff4-4ad8-9f18-abae84dc5ff2");
}

/// <summary>HRESULT checking: one place a failed call becomes an exception that says what it was.</summary>
internal static class D3D12Check
{
    /// <summary><c>DXGI_ERROR_DEVICE_REMOVED</c>.</summary>
    public const int DeviceRemoved = unchecked((int)0x887A0005);

    /// <summary><c>DXGI_ERROR_DEVICE_RESET</c>.</summary>
    public const int DeviceReset = unchecked((int)0x887A0007);

    /// <summary>
    /// Gets or sets what reports a removed device -- its reason, and what DRED recorded -- the first time a call
    /// finds it removed. Set by the device's context; a call that fails this way has no device of its own to ask.
    /// </summary>
    public static Action? DeviceLost { get; set; }

    /// <summary>Throws when a call failed.</summary>
    /// <param name="result">The HRESULT.</param>
    /// <param name="call">What was called, for the message.</param>
    /// <exception cref="GraphicsDeviceException">It failed.</exception>
    public static void Ok(int result, string call)
    {
        if (result >= 0)
        {
            return;
        }

        if (result is DeviceRemoved or DeviceReset && DeviceLost is { } report)
        {
            // Once: everything after a removal fails the same way, and the first report is the one that matters.
            DeviceLost = null;
            report();
        }

        throw new GraphicsDeviceException(
            result is DeviceRemoved or DeviceReset
                ? $"{call} failed: the Direct3D 12 device was removed (0x{result:X8})."
                : $"{call} failed: 0x{result:X8}.");
    }

    /// <summary>Releases a COM object and clears the pointer, if it is set.</summary>
    /// <typeparam name="T">The interface.</typeparam>
    /// <param name="pointer">The pointer, null afterwards.</param>
    public static unsafe void Release<T>(ref T* pointer)
        where T : unmanaged, IComVtbl<T>
    {
        if (pointer is not null)
        {
            ((IUnknown*)pointer)->Release();
            pointer = null;
        }
    }
}

/// <summary>Names a Direct3D 12 object, so a capture, the debug layer and DRED call it what the RHI did.</summary>
internal static unsafe class D3D12Names
{
    /// <summary>Sets an object's name.</summary>
    /// <param name="target">The object.</param>
    /// <param name="name">The name; nothing is set for an empty one.</param>
    public static void Set(ID3D12Object* target, string name)
    {
        if (target is null || string.IsNullOrEmpty(name))
        {
            return;
        }

        fixed (char* text = name)
        {
            target->SetName(text);
        }
    }
}

/// <summary>Tells the OS what to page out last when video memory runs short.</summary>
internal static unsafe class D3D12Residency
{
    /// <summary>Marks memory the frame cannot do without -- render targets, shared buffer pools, acceleration
    /// structures -- as the last to be evicted under memory pressure.</summary>
    /// <param name="device">The device.</param>
    /// <param name="resource">The resource.</param>
    public static void High(ID3D12Device10* device, ID3D12Resource* resource)
    {
        ID3D12Pageable* pageable = (ID3D12Pageable*)resource;
        ResidencyPriority priority = ResidencyPriority.High;
        _ = device->SetResidencyPriority(1, &pageable, &priority);
    }
}

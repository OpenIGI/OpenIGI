using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Capabilities;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The Vulkan instance, its debug messenger, and the presentable surface — everything that exists once
/// per backend and outlives every swapchain and device reset.
/// </summary>
/// <remarks>
/// <para>
/// An instance is the loader-level object the rest of the backend hangs off: the physical devices are
/// enumerated from it, the surface is created against it, and the validation layer — when present — is
/// attached to it. The surface is kept here rather than in the swapchain because it survives a
/// swapchain rebuild; a resize destroys the swapchain and makes a new one against the same surface.
/// </para>
/// <para>
/// Validation is requested when asked for and dropped silently when the layer is not installed, so a
/// developer machine gets the checks and a player's machine — which has no validation layer — still
/// starts. The same is true of the debug-utils extension that carries the messages back.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanInstance : IDisposable
{
    private const string ValidationLayerName = "VK_LAYER_KHRONOS_validation";

    // The messenger is handed the callback as a delegate, which the driver keeps for the messenger's
    // whole life; the delegate must therefore outlive it, so it is held in a static field rather than a
    // local that the GC could reclaim once the constructor returns.
    private static readonly DebugUtilsMessengerCallbackFunctionEXT DebugCallback = OnDebugMessage;

    // The callback is static, so the one logger it routes to is stashed here. There is a single backend
    // per process, so a static sink is enough.
    private static IIgiLogger _debugLogger = NullLogger.Instance;

    private readonly ExtDebugUtils? _debugUtils;
    private readonly DebugUtilsMessengerEXT _debugMessenger;

    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanInstance"/> class.</summary>
    /// <param name="surfaceProvider">The window's surface bridge.</param>
    /// <param name="enableValidation">Whether to attach the Khronos validation layer if it is present.</param>
    /// <param name="logger">Where to report backend diagnostics.</param>
    /// <exception cref="GraphicsDeviceException">The instance or the surface could not be created.</exception>
    public VulkanInstance(IVulkanSurfaceProvider surfaceProvider, bool enableValidation, IIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(surfaceProvider);
        _debugLogger = logger;
        Vk = Vk.GetApi();

        List<string> extensions = [.. surfaceProvider.GetRequiredInstanceExtensions()];
        bool wantDebug = enableValidation && ValidationLayerAvailable();
        if (wantDebug && !extensions.Contains(ExtDebugUtils.ExtensionName))
        {
            extensions.Add(ExtDebugUtils.ExtensionName);
        }

        // Asked for wherever the loader offers it, which in practice is macOS and nowhere else. It costs
        // nothing on a conformant loader and is the difference between seeing an adapter and seeing none
        // on a portable one, so it is taken on availability rather than on a platform check.
        bool portability = InstanceExtensionAvailable(VulkanExtensionNames.PortabilityEnumeration);
        if (portability && !extensions.Contains(VulkanExtensionNames.PortabilityEnumeration))
        {
            extensions.Add(VulkanExtensionNames.PortabilityEnumeration);
        }

        Instance = CreateInstance(extensions, wantDebug, portability);

        if (wantDebug && Vk.TryGetInstanceExtension(Instance, out ExtDebugUtils debugUtils))
        {
            _debugUtils = debugUtils;
            _debugMessenger = CreateDebugMessenger(debugUtils);
        }

        if (!Vk.TryGetInstanceExtension(Instance, out KhrSurface khrSurface))
        {
            throw new GraphicsDeviceException("VK_KHR_surface is not available on this instance.");
        }

        KhrSurface = khrSurface;
        Surface = surfaceProvider.CreateSurface(Vk, Instance, khrSurface);
    }

    /// <summary>Gets the core Vulkan API bound to the loader.</summary>
    public Vk Vk { get; }

    /// <summary>Gets the instance.</summary>
    public Instance Instance { get; }

    /// <summary>Gets the surface extension loaded for the instance.</summary>
    public KhrSurface KhrSurface { get; }

    /// <summary>Gets the presentable surface for the window.</summary>
    public SurfaceKHR Surface { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        KhrSurface.DestroySurface(Instance, Surface, null);

        if (_debugUtils is not null)
        {
            _debugUtils.DestroyDebugUtilsMessenger(Instance, _debugMessenger, null);
            _debugUtils.Dispose();
        }

        KhrSurface.Dispose();
        Vk.DestroyInstance(Instance, null);
        Vk.Dispose();
    }

    private Instance CreateInstance(List<string> extensions, bool withValidationLayer, bool withPortability)
    {
        uint apiVersion = Vk.Version12;
        uint supported;
        if (Vk.EnumerateInstanceVersion(&supported) == Result.Success && supported >= Vk.Version13)
        {
            // Request 1.3 when the loader offers it — that is where dynamic rendering and the
            // synchronisation-2 barriers this backend prefers become core rather than extensions.
            apiVersion = Vk.Version13;
        }

        nint appName = SilkMarshal.StringToPtr("OpenIGI");
        nint engineName = SilkMarshal.StringToPtr("OpenIGI");
        nint extensionNames = SilkMarshal.StringArrayToPtr(extensions);
        nint layerNames = withValidationLayer ? SilkMarshal.StringArrayToPtr(new[] { ValidationLayerName }) : 0;

        try
        {
            ApplicationInfo appInfo = new()
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appName,
                ApplicationVersion = new Version32(1, 0, 0),
                PEngineName = (byte*)engineName,
                EngineVersion = new Version32(1, 0, 0),
                ApiVersion = apiVersion,
            };

            InstanceCreateInfo createInfo = new()
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,

                // The extension only makes the flag legal; the flag is what actually widens the search.
                // Enabling one without the other still enumerates nothing, which is why they are carried
                // together from the same decision rather than set in two places.
                Flags = withPortability ? InstanceCreateFlags.EnumeratePortabilityBitKhr : 0,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = (byte**)extensionNames,
                EnabledLayerCount = withValidationLayer ? 1u : 0u,
                PpEnabledLayerNames = withValidationLayer ? (byte**)layerNames : null,
            };

            Instance instance;
            VkCheck.Ok(Vk.CreateInstance(&createInfo, null, &instance), "vkCreateInstance");
            return instance;
        }
        finally
        {
            SilkMarshal.Free(appName);
            SilkMarshal.Free(engineName);
            SilkMarshal.Free(extensionNames);
            if (layerNames != 0)
            {
                SilkMarshal.Free(layerNames);
            }
        }
    }

    private bool InstanceExtensionAvailable(string name)
    {
        uint count = 0;
        if (Vk.EnumerateInstanceExtensionProperties((byte*)null, &count, null) != Result.Success || count == 0)
        {
            return false;
        }

        ExtensionProperties[] properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* first = properties)
        {
            if (Vk.EnumerateInstanceExtensionProperties((byte*)null, &count, first) != Result.Success)
            {
                return false;
            }

            for (uint i = 0; i < count; i++)
            {
                if (VkString.Read(first[i].ExtensionName) == name)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool ValidationLayerAvailable()
    {
        uint count = 0;
        if (Vk.EnumerateInstanceLayerProperties(&count, null) != Result.Success || count == 0)
        {
            return false;
        }

        LayerProperties[] layers = new LayerProperties[count];
        fixed (LayerProperties* first = layers)
        {
            if (Vk.EnumerateInstanceLayerProperties(&count, first) != Result.Success)
            {
                return false;
            }

            for (uint i = 0; i < count; i++)
            {
                if (VkString.Read(first[i].LayerName) == ValidationLayerName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private DebugUtilsMessengerEXT CreateDebugMessenger(ExtDebugUtils debugUtils)
    {
        DebugUtilsMessengerCreateInfoEXT createInfo = new()
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
                | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT(DebugCallback),
        };

        DebugUtilsMessengerEXT messenger;
        VkCheck.Ok(
            debugUtils.CreateDebugUtilsMessenger(Instance, &createInfo, null, &messenger),
            "vkCreateDebugUtilsMessengerEXT");
        return messenger;
    }

    private static uint OnDebugMessage(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT types,
        DebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        string message = data is null || data->PMessage is null
            ? "(no message)"
            : Marshal.PtrToStringAnsi((nint)data->PMessage) ?? "(no message)";

        if (severity.HasFlag(DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt))
        {
            _debugLogger.Error("Vulkan", message);
        }
        else
        {
            _debugLogger.Warning("Vulkan", message);
        }

        // The spec requires the application to always return false here: true is reserved for the
        // layer's own use and aborts the call that triggered the message.
        return Vk.False;
    }
}

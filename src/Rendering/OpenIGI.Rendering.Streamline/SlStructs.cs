using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Streamline;

/// <summary>A resource handed to Streamline, described in full because Vulkan cannot be asked.</summary>
/// <remarks>
/// Direct3D lets Streamline query a resource for its dimensions and format. Vulkan has no equivalent
/// for a <c>VkImage</c>, so the host has to supply every field, and the image view is mandatory
/// rather than optional. <see cref="State"/> is the <c>VkImageLayout</c> the image will be in
/// <em>when Streamline uses it</em>, which is not necessarily the layout it is in when tagged. The
/// RHI rests every image in <c>General</c> between passes and Streamline only ever runs between passes,
/// which makes that distinction moot here and is why it is safe.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlResource
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>What kind of resource this is.</summary>
    public SlResourceType Type;

    /// <summary>The <c>VkImage</c> or <c>VkBuffer</c>.</summary>
    public nint Native;

    /// <summary>The <c>VkDeviceMemory</c>, or zero. Streamline does not need it for an image.</summary>
    public nint Memory;

    /// <summary>The <c>VkImageView</c>. Mandatory on Vulkan.</summary>
    public nint View;

    /// <summary>The <c>VkImageLayout</c> the image is in when Streamline reads or writes it.</summary>
    public uint State;

    /// <summary>The width in pixels.</summary>
    public uint Width;

    /// <summary>The height in pixels.</summary>
    public uint Height;

    /// <summary>The <c>VkFormat</c>.</summary>
    public uint NativeFormat;

    /// <summary>The number of mip levels.</summary>
    public uint MipLevels;

    /// <summary>The number of array layers.</summary>
    public uint ArrayLayers;

    /// <summary>The GPU virtual address, where one applies. Zero for an image.</summary>
    public ulong GpuVirtualAddress;

    /// <summary>The <c>VkImageCreateFlags</c>. Has no default in the C++ header — always set it.</summary>
    public uint Flags;

    /// <summary>The <c>VkImageUsageFlags</c>.</summary>
    public uint Usage;

    /// <summary>Streamline's own bookkeeping flags. The host leaves them zero.</summary>
    /// <remarks>
    /// Was a single reserved <c>uint32_t</c> until 2.14, which split it in two. The size and every
    /// other offset are unchanged.
    /// </remarks>
    public ushort InternalFlags;

    /// <summary>Reserved by Streamline.</summary>
    public ushort Reserved;

    /// <summary>The structure's identity, from <c>sl_core_types.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x3a9d70cf,
        0x2418,
        0x4b72,
        SlStructType.Pack(0x83, 0x91, 0x13, 0xf8, 0x72, 0x1c, 0x72, 0x61));

    /// <summary>Creates a described two-dimensional image resource.</summary>
    /// <param name="image">The <c>VkImage</c> handle.</param>
    /// <param name="view">The <c>VkImageView</c> handle.</param>
    /// <param name="layout">The <c>VkImageLayout</c> the image is in when Streamline uses it.</param>
    /// <param name="format">The <c>VkFormat</c>.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="usage">The <c>VkImageUsageFlags</c> the image was created with.</param>
    /// <returns>The resource description.</returns>
    public static SlResource Image2D(
        nint image,
        nint view,
        uint layout,
        uint format,
        uint width,
        uint height,
        uint usage) =>
        new()
        {
            Header = SlStructHeader.For(TypeId, 1),
            Type = SlResourceType.Tex2d,
            Native = image,
            Memory = 0,
            View = view,
            State = layout,
            Width = width,
            Height = height,
            NativeFormat = format,
            MipLevels = 1,
            ArrayLayers = 1,
            GpuVirtualAddress = 0,
            Flags = 0,
            Usage = usage,
            InternalFlags = 0,
            Reserved = 0,
        };
}

/// <summary>A resource paired with the role it plays for a feature.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlResourceTag
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>A pointer to the <see cref="SlResource"/>, which must outlive the call.</summary>
    public nint Resource;

    /// <summary>Which <see cref="SlBufferType"/> this resource is.</summary>
    public uint Type;

    /// <summary>How long the resource stays valid.</summary>
    public SlResourceLifecycle Lifecycle;

    /// <summary>The region to use. Left zeroed, the whole resource is used.</summary>
    public SlExtent Extent;

    /// <summary>The structure's identity, from <c>sl_core_types.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x4c6a5aad,
        0xb445,
        0x496c,
        SlStructType.Pack(0x87, 0xff, 0x1a, 0xf3, 0x84, 0x5b, 0xe6, 0x53));

    /// <summary>Creates a tag over the whole of a resource.</summary>
    /// <param name="resource">A pointer to the resource description.</param>
    /// <param name="bufferType">The role the resource plays, from <see cref="SlBufferType"/>.</param>
    /// <param name="lifecycle">How long the resource stays valid.</param>
    /// <returns>The tag.</returns>
    public static SlResourceTag For(nint resource, uint bufferType, SlResourceLifecycle lifecycle) =>
        new()
        {
            Header = SlStructHeader.For(TypeId, 1),
            Resource = resource,
            Type = bufferType,
            Lifecycle = lifecycle,
            Extent = default,
        };
}

/// <summary>The per-frame camera state every Streamline feature reads.</summary>
/// <remarks>
/// <para>
/// Every matrix here is row-major and must be free of the temporal jitter — the jitter is supplied
/// separately in <see cref="JitterOffset"/>, in pixels, because the features need to know what the
/// rasteriser was displaced by rather than to have it folded into the transform.
/// </para>
/// <para>
/// Fields left at <see cref="SlAbi.InvalidFloat"/> or <see cref="SlBoolean.Invalid"/> are rejected
/// where Streamline considers them mandatory, so <see cref="Reset"/> below fills the whole structure
/// rather than relying on a zeroed one.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlConstants
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Camera view space to clip space, unjittered.</summary>
    public SlFloat4x4 CameraViewToClip;

    /// <summary>Clip space to camera view space.</summary>
    public SlFloat4x4 ClipToCameraView;

    /// <summary>Optional lens distortion in clip space.</summary>
    public SlFloat4x4 ClipToLensClip;

    /// <summary>This frame's clip space to the previous frame's clip space.</summary>
    public SlFloat4x4 ClipToPrevClip;

    /// <summary>The inverse of <see cref="ClipToPrevClip"/>.</summary>
    public SlFloat4x4 PrevClipToClip;

    /// <summary>This frame's sub-pixel jitter, in pixels.</summary>
    public SlFloat2 JitterOffset;

    /// <summary>What the motion-vector buffer must be multiplied by to land in the range -1 to 1.</summary>
    public SlFloat2 MvecScale;

    /// <summary>Optional pinhole offset.</summary>
    public SlFloat2 CameraPinholeOffset;

    /// <summary>The camera position in world space.</summary>
    public SlFloat3 CameraPos;

    /// <summary>The camera's up vector in world space.</summary>
    public SlFloat3 CameraUp;

    /// <summary>The camera's right vector in world space.</summary>
    public SlFloat3 CameraRight;

    /// <summary>The camera's forward vector in world space.</summary>
    public SlFloat3 CameraFwd;

    /// <summary>The near plane distance.</summary>
    public float CameraNear;

    /// <summary>The far plane distance.</summary>
    public float CameraFar;

    /// <summary>The vertical field of view in radians.</summary>
    public float CameraFov;

    /// <summary>Width over height.</summary>
    public float CameraAspectRatio;

    /// <summary>What an uninitialised motion vector reads as. Only needed when motion is not camera-inclusive.</summary>
    public float MotionVectorsInvalidValue;

    /// <summary>Whether nearer surfaces have the higher depth value. True here — this renderer is reverse-Z.</summary>
    public SlBoolean DepthInverted;

    /// <summary>Whether camera motion is already in the motion-vector buffer.</summary>
    public SlBoolean CameraMotionIncluded;

    /// <summary>Whether motion vectors carry a third component.</summary>
    public SlBoolean MotionVectors3D;

    /// <summary>Whether the previous frame bears no relation to this one.</summary>
    public SlBoolean Reset;

    /// <summary>Whether the projection is orthographic.</summary>
    public SlBoolean OrthographicProjection;

    /// <summary>Whether the motion vectors have already been dilated.</summary>
    public SlBoolean MotionVectorsDilated;

    /// <summary>Whether the motion vectors carry the jitter.</summary>
    public SlBoolean MotionVectorsJittered;

    /// <summary>How far apart two surfaces must be, in linear depth, to count as separate objects.</summary>
    public float MinRelativeLinearDepthObjectSeparation;

    /// <summary>The structure's identity, from <c>sl_consts.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xdcd35ad7,
        0x4e4a,
        0x4bad,
        SlStructType.Pack(0xa9, 0x0c, 0xe0, 0xc4, 0x9e, 0xb2, 0x3a, 0xfe));

    /// <summary>Creates a fully-populated structure with every optional field explicitly unset.</summary>
    /// <returns>The constants, ready to have the caller's values written over them.</returns>
    /// <remarks>
    /// Streamline's own defaults are not zero — an unset float is
    /// <see cref="SlAbi.InvalidFloat"/> and an unset flag is <see cref="SlBoolean.Invalid"/> — so a
    /// default-constructed mirror would be telling Streamline that every optional value is a
    /// deliberate zero. This reproduces the C++ constructor instead.
    /// </remarks>
    public static SlConstants Create()
    {
        SlConstants constants = default;
        constants.Header = SlStructHeader.For(TypeId, 2);
        constants.CameraPinholeOffset = new SlFloat2(0f, 0f);
        constants.CameraNear = SlAbi.InvalidFloat;
        constants.CameraFar = SlAbi.InvalidFloat;
        constants.CameraFov = SlAbi.InvalidFloat;
        constants.CameraAspectRatio = SlAbi.InvalidFloat;
        constants.MotionVectorsInvalidValue = SlAbi.InvalidFloat;
        constants.DepthInverted = SlBoolean.Invalid;
        constants.CameraMotionIncluded = SlBoolean.Invalid;
        constants.MotionVectors3D = SlBoolean.Invalid;
        constants.Reset = SlBoolean.Invalid;
        constants.OrthographicProjection = SlBoolean.False;
        constants.MotionVectorsDilated = SlBoolean.False;
        constants.MotionVectorsJittered = SlBoolean.False;
        constants.MinRelativeLinearDepthObjectSeparation = 40f;
        return constants;
    }
}

/// <summary>What <c>slInit</c> is told about the host.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SlPreferences
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Whether Streamline opens its own console window. A C++ <c>bool</c>, so one byte.</summary>
    public byte ShowConsole;

    /// <summary>How much Streamline logs.</summary>
    public SlLogLevel LogLevel;

    /// <summary>An array of directories to search for plugins.</summary>
    public nint PathsToPlugins;

    /// <summary>How many entries <see cref="PathsToPlugins"/> holds.</summary>
    public uint NumPathsToPlugins;

    /// <summary>Where Streamline may write its log.</summary>
    public nint PathToLogsAndData;

    /// <summary>An optional host allocator.</summary>
    public nint AllocateCallback;

    /// <summary>An optional host deallocator.</summary>
    public nint ReleaseCallback;

    /// <summary>Where Streamline's log lines are delivered.</summary>
    public delegate* unmanaged[Cdecl]<SlLogType, nint, void> LogMessageCallback;

    /// <summary>Behaviour switches.</summary>
    public SlPreferenceFlags Flags;

    /// <summary>An array of feature identifiers to load. Nothing loads if this is empty.</summary>
    public nint FeaturesToLoad;

    /// <summary>How many entries <see cref="FeaturesToLoad"/> holds.</summary>
    public uint NumFeaturesToLoad;

    /// <summary>The NVIDIA-issued application identifier, where one has been obtained.</summary>
    public uint ApplicationId;

    /// <summary>Which engine this is.</summary>
    public SlEngineType Engine;

    /// <summary>The engine version, as UTF-8.</summary>
    public nint EngineVersion;

    /// <summary>The project identifier, as UTF-8.</summary>
    public nint ProjectId;

    /// <summary>Which rendering API the host drives. Must be set, or the Vulkan requirements come back empty.</summary>
    public SlRenderApi RenderApi;

    /// <summary>The structure's identity, from <c>sl_core_types.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x1ca10965,
        0xbf8e,
        0x432b,
        SlStructType.Pack(0x8d, 0xa1, 0x67, 0x16, 0xd8, 0x79, 0xfb, 0x14));

    /// <summary>Creates preferences for a host that loads only the plugins it was shipped with.</summary>
    /// <param name="renderApi">The graphics API the host drives.</param>
    /// <returns>The preferences.</returns>
    /// <remarks>
    /// Streamline's own defaults turn on over-the-air updates and the loading of what they download.
    /// Both are left off: every plugin is signature-checked before <c>slInit</c>, and a plugin fetched
    /// afterwards would never have been.
    /// </remarks>
    public static SlPreferences Create(SlRenderApi renderApi) =>
        new()
        {
            Header = SlStructHeader.For(TypeId, 1),
            LogLevel = SlLogLevel.Default,
            Flags = SlPreferenceFlags.DisableCommandListStateTracking,
            Engine = SlEngineType.Custom,
            RenderApi = renderApi,
        };
}

/// <summary>Which adapter a feature is being asked about.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlAdapterInfo
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>A pointer to the adapter LUID. Ignored when <see cref="VkPhysicalDevice"/> is set.</summary>
    public nint DeviceLuid;

    /// <summary>How many bytes the LUID occupies.</summary>
    public uint DeviceLuidSizeInBytes;

    /// <summary>The <c>VkPhysicalDevice</c>, which is how a Vulkan host identifies its adapter.</summary>
    public nint VkPhysicalDevice;

    /// <summary>The structure's identity, from <c>sl_core_types.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x0677315f,
        0xa746,
        0x4492,
        SlStructType.Pack(0x9f, 0x42, 0xcb, 0x61, 0x42, 0xc9, 0xc3, 0xd4));

    /// <summary>Creates adapter information naming a Vulkan physical device.</summary>
    /// <param name="physicalDevice">The <c>VkPhysicalDevice</c> handle.</param>
    /// <returns>The adapter information.</returns>
    public static SlAdapterInfo ForVulkan(nint physicalDevice) =>
        new()
        {
            Header = SlStructHeader.For(TypeId, 1),
            VkPhysicalDevice = physicalDevice,
        };

    /// <summary>Creates adapter information naming a DXGI adapter by its LUID.</summary>
    /// <param name="luid">Where the eight bytes of the LUID are, which must outlive every query.</param>
    /// <returns>The adapter information.</returns>
    public static SlAdapterInfo ForLuid(nint luid) =>
        new()
        {
            Header = SlStructHeader.For(TypeId, 1),
            DeviceLuid = luid,
            DeviceLuidSizeInBytes = 8,
        };
}

/// <summary>What a feature needs of the machine, the driver and the Vulkan device.</summary>
/// <remarks>
/// The Vulkan members are the reason this must be read before the instance and device are created:
/// they name extensions, <c>VkPhysicalDeviceVulkan12Features</c>/<c>13Features</c> bits and extra
/// queues that have to be in the create-infos.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlFeatureRequirements
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Which APIs the feature supports and what it needs turned on.</summary>
    public SlFeatureRequirementFlags Flags;

    /// <summary>How many CPU threads the feature may use.</summary>
    public uint MaxNumCpuThreads;

    /// <summary>How many viewports the feature supports at once.</summary>
    public uint MaxNumViewports;

    /// <summary>How many tags the feature requires.</summary>
    public uint NumRequiredTags;

    /// <summary>An array of the required <see cref="SlBufferType"/> values.</summary>
    public nint RequiredTags;

    /// <summary>The operating-system version found.</summary>
    public SlVersion OsVersionDetected;

    /// <summary>The operating-system version required.</summary>
    public SlVersion OsVersionRequired;

    /// <summary>The driver version found.</summary>
    public SlVersion DriverVersionDetected;

    /// <summary>The driver version required.</summary>
    public SlVersion DriverVersionRequired;

    /// <summary>How many extra compute queues the feature wants.</summary>
    public uint VkNumComputeQueuesRequired;

    /// <summary>How many extra graphics queues the feature wants.</summary>
    public uint VkNumGraphicsQueuesRequired;

    /// <summary>How many device extensions the feature needs.</summary>
    public uint VkNumDeviceExtensions;

    /// <summary>An array of UTF-8 device extension names.</summary>
    public nint VkDeviceExtensions;

    /// <summary>How many instance extensions the feature needs.</summary>
    public uint VkNumInstanceExtensions;

    /// <summary>An array of UTF-8 instance extension names.</summary>
    public nint VkInstanceExtensions;

    /// <summary>How many Vulkan 1.2 feature bits the feature needs.</summary>
    public uint VkNumFeatures12;

    /// <summary>An array of UTF-8 <c>VkPhysicalDeviceVulkan12Features</c> field names.</summary>
    public nint VkFeatures12;

    /// <summary>How many Vulkan 1.3 feature bits the feature needs.</summary>
    public uint VkNumFeatures13;

    /// <summary>An array of UTF-8 <c>VkPhysicalDeviceVulkan13Features</c> field names.</summary>
    public nint VkFeatures13;

    /// <summary>How many optical-flow queues the feature wants. Frame generation only.</summary>
    public uint VkNumOpticalFlowQueuesRequired;

    /// <summary>The structure's identity, from <c>sl_core_types.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x66714097,
        0xac6d,
        0x4bc6,
        SlStructType.Pack(0x89, 0x15, 0x1e, 0x0f, 0x55, 0xa6, 0xb6, 0x1f));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The requirements.</returns>
    public static SlFeatureRequirements Create() =>
        new() { Header = SlStructHeader.For(TypeId, 2) };
}

/// <summary>The Streamline and NGX versions behind a feature.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlFeatureVersion
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>The Streamline plugin version.</summary>
    public SlVersion VersionSl;

    /// <summary>The NGX model version — for DLSS, which neural network is actually loaded.</summary>
    public SlVersion VersionNgx;

    /// <summary>The structure's identity, from <c>sl_core_types.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x6d5b51f0,
        0x076b,
        0x486d,
        SlStructType.Pack(0x99, 0x95, 0x5a, 0x56, 0x10, 0x43, 0xf5, 0xc1));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The version.</returns>
    public static SlFeatureVersion Create() =>
        new() { Header = SlStructHeader.For(TypeId, 1) };
}

/// <summary>Which viewport a call refers to. The host picks the number.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlViewportHandle
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>The host-chosen viewport identifier.</summary>
    public uint Value;

    /// <summary>The structure's identity, from <c>sl_core_types.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x171b6435,
        0x9b3c,
        0x4fc8,
        SlStructType.Pack(0x99, 0x94, 0xfb, 0xe5, 0x25, 0x69, 0xaa, 0xa4));

    /// <summary>Creates a handle for a viewport number.</summary>
    /// <param name="value">The viewport identifier.</param>
    /// <returns>The handle.</returns>
    public static SlViewportHandle For(uint value) =>
        new() { Header = SlStructHeader.For(TypeId, 1), Value = value };
}

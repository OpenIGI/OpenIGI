using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Streamline;

/// <summary>
/// The Streamline SDK's own types, mirrored for P/Invoke.
/// </summary>
/// <remarks>
/// <para>
/// Streamline's headers are C++ — namespaces, member functions, default arguments and a polymorphic
/// frame token — but every function this backend needs is a flat <c>extern "C"</c> export on
/// <c>sl.interposer.dll</c> or a feature function fetched by name, and every structure crossing that
/// boundary is plain data. So the types are mirrored here rather than reached through a native shim:
/// it keeps the repository free of C++, which matters because nothing else in it is native and the CI
/// matrix builds on machines with no MSVC at all.
/// </para>
/// <para>
/// The price is that the layouts have to be exactly right, and a wrong one does not fail loudly — it
/// hands the plugin a garbage pointer. The layouts below were therefore taken from the compiler
/// rather than derived by hand: a throwaway program compiled against the 2.14.1 headers printed every
/// <c>sizeof</c>, <c>offsetof</c> and the enum values that matter, and <c>StreamlineLayoutTests</c>
/// pins those numbers. Change a field here and that test is what tells you.
/// </para>
/// <para>
/// Two traps are worth naming because a reasonable guess gets both wrong. <c>structVersion</c> is a
/// <c>size_t</c>, so eight bytes and not four; a <c>uint</c> there shifts every field after it.
/// And <c>Boolean</c> is <c>enum Boolean : char</c> — one byte, with a third value
/// <see cref="SlBoolean.Invalid"/> that means "not supplied", which is what several fields default to.
/// </para>
/// </remarks>
internal static class SlAbi
{
    /// <summary>The SDK version <c>slInit</c> is called with, from <c>sl_version.h</c>.</summary>
    /// <remarks>
    /// <c>(major &lt;&lt; 48) | (minor &lt;&lt; 32) | (patch &lt;&lt; 16) | 0xfedc</c> for Streamline
    /// 2.14.1. Streamline checks it to reject a header/binary mismatch, so it moves with the DLLs
    /// that <c>tools/fetch-streamline.ps1</c> pins.
    /// </remarks>
    public const ulong SdkVersion = 0x0002_000E_0001_FEDCUL;

    /// <summary>The value Streamline treats as "this float was never set".</summary>
    /// <remarks>
    /// Streamline's own <c>float2/3/4</c> default-construct to this rather than to zero, so a field
    /// left at zero reads as a deliberate zero and a field left unset must carry this instead. Every
    /// optional constant is filled explicitly for that reason.
    /// </remarks>
    public const float InvalidFloat = 3.402823466e38f;

    /// <summary>The value Streamline treats as "this integer was never set".</summary>
    public const uint InvalidUint = uint.MaxValue;
}

/// <summary>Streamline's tri-state boolean: one byte, with an explicit "not supplied".</summary>
internal enum SlBoolean : byte
{
    /// <summary>False.</summary>
    False = 0,

    /// <summary>True.</summary>
    True = 1,

    /// <summary>Not supplied. Streamline rejects this for any field it considers mandatory.</summary>
    Invalid = 2,
}

/// <summary>Streamline's result codes, numbered exactly as <c>sl_result.h</c> declares them.</summary>
/// <remarks>
/// The header declares a plain sequence with no explicit values, so every member carries its ordinal
/// here. An earlier mirror named a handful from memory and put most of them on the wrong number, which
/// made every refusal it logged describe a different failure from the one that happened.
/// </remarks>
internal enum SlResult : uint
{
    /// <summary>The call succeeded.</summary>
    Ok = 0,

    /// <summary>A file could not be read or written.</summary>
    ErrorIO = 1,

    /// <summary>The graphics driver is older than the feature requires.</summary>
    ErrorDriverOutOfDate = 2,

    /// <summary>The operating system is older than the feature requires.</summary>
    ErrorOSOutOfDate = 3,

    /// <summary>Hardware-accelerated GPU scheduling is off and the feature needs it.</summary>
    ErrorOSDisabledHWS = 4,

    /// <summary>The device was not created, or was created before Streamline was told about it.</summary>
    ErrorDeviceNotCreated = 5,

    /// <summary>No adapter on this machine supports the feature.</summary>
    ErrorNoSupportedAdapterFound = 6,

    /// <summary>This adapter does not support the feature.</summary>
    ErrorAdapterNotSupported = 7,

    /// <summary>No plugins were found.</summary>
    ErrorNoPlugins = 8,

    /// <summary>A Vulkan call inside Streamline failed.</summary>
    ErrorVulkanAPI = 9,

    /// <summary>A DXGI call inside Streamline failed.</summary>
    ErrorDXGIAPI = 10,

    /// <summary>A Direct3D call inside Streamline failed.</summary>
    ErrorD3DAPI = 11,

    /// <summary>Retired with the denoiser plugin it named; kept so the numbering holds.</summary>
    ErrorNRDAPI = 12,

    /// <summary>An NVAPI call inside Streamline failed.</summary>
    ErrorNVAPI = 13,

    /// <summary>A Reflex call inside Streamline failed.</summary>
    ErrorReflexAPI = 14,

    /// <summary>NGX, the runtime the DLSS models run on, failed.</summary>
    ErrorNGXFailed = 15,

    /// <summary>A plugin's JSON configuration could not be parsed.</summary>
    ErrorJSONParsing = 16,

    /// <summary>A proxy object Streamline expected the host to be using is missing.</summary>
    ErrorMissingProxy = 17,

    /// <summary>A tagged resource carries no state.</summary>
    ErrorMissingResourceState = 18,

    /// <summary>The host is calling Streamline in a way it does not support.</summary>
    ErrorInvalidIntegration = 19,

    /// <summary>A required input was not supplied.</summary>
    ErrorMissingInputParameter = 20,

    /// <summary>The feature was not initialised.</summary>
    ErrorNotInitialized = 21,

    /// <summary>A compute dispatch inside Streamline failed.</summary>
    ErrorComputeFailed = 22,

    /// <summary><c>slInit</c> has not been called.</summary>
    ErrorInitNotCalled = 23,

    /// <summary>Streamline's own exception handler caught something.</summary>
    ErrorExceptionHandler = 24,

    /// <summary>A parameter was out of range or malformed.</summary>
    ErrorInvalidParameter = 25,

    /// <summary>The per-frame constants were not supplied for this frame.</summary>
    ErrorMissingConstants = 26,

    /// <summary>The per-frame constants were supplied twice for this frame.</summary>
    ErrorDuplicatedConstants = 27,

    /// <summary>A required API entry point is missing or wrong.</summary>
    ErrorMissingOrInvalidAPI = 28,

    /// <summary>The per-frame constants every feature shares are missing.</summary>
    ErrorCommonConstantsMissing = 29,

    /// <summary>The interface handed in is not one Streamline supports.</summary>
    ErrorUnsupportedInterface = 30,

    /// <summary>The feature's plugin is missing from the search path.</summary>
    ErrorFeatureMissing = 31,

    /// <summary>The feature exists but is not supported in this configuration.</summary>
    ErrorFeatureNotSupported = 32,

    /// <summary>The feature needs hooks that are not in place.</summary>
    ErrorFeatureMissingHooks = 33,

    /// <summary>The feature's plugin failed to load.</summary>
    ErrorFeatureFailedToLoad = 34,

    /// <summary>The feature's plugin has the wrong priority.</summary>
    ErrorFeatureWrongPriority = 35,

    /// <summary>A plugin the feature depends on is missing.</summary>
    ErrorFeatureMissingDependency = 36,

    /// <summary>The plugin manager is in a state that does not allow the call.</summary>
    ErrorFeatureManagerInvalidState = 37,

    /// <summary>Streamline is in a state that does not allow the call.</summary>
    ErrorInvalidState = 38,

    /// <summary>The call succeeded, but video memory is running out.</summary>
    WarnOutOfVRAM = 39,
}

/// <summary>Which rendering API the host is driving. Must be set before <c>slInit</c>.</summary>
/// <remarks>
/// Getting this wrong is quiet rather than loud: <c>slGetFeatureRequirements</c> reports the Vulkan
/// extension and feature requirements only when Streamline has been told the host is on Vulkan.
/// </remarks>
internal enum SlRenderApi : uint
{
    /// <summary>Direct3D 11.</summary>
    D3D11 = 0,

    /// <summary>Direct3D 12.</summary>
    D3D12 = 1,

    /// <summary>Vulkan.</summary>
    Vulkan = 2,
}

/// <summary>How Streamline should describe the host engine in its logs.</summary>
internal enum SlEngineType : uint
{
    /// <summary>An engine of the host's own.</summary>
    Custom = 0,
}

/// <summary>How much Streamline logs.</summary>
internal enum SlLogLevel : uint
{
    /// <summary>Nothing.</summary>
    Off = 0,

    /// <summary>Warnings and errors.</summary>
    Default = 1,

    /// <summary>Everything.</summary>
    Verbose = 2,
}

/// <summary>The severity of a message arriving through Streamline's log callback.</summary>
internal enum SlLogType : uint
{
    /// <summary>Informational.</summary>
    Info = 0,

    /// <summary>A warning.</summary>
    Warn = 1,

    /// <summary>An error.</summary>
    Error = 2,
}

/// <summary>Behaviour switches passed to <c>slInit</c>.</summary>
[Flags]
internal enum SlPreferenceFlags : ulong
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>
    /// Streamline will not save and restore command-buffer state around its own work, so the host
    /// must. The RHI binds everything per pass and evaluates only between passes, so there is never
    /// any state for Streamline to put back, and having it shadow every bind would be pure cost.
    /// </summary>
    DisableCommandListStateTracking = 1UL << 0,

    /// <summary>Suppress the debug text the development plugins draw over the frame.</summary>
    DisableDebugText = 1UL << 1,

    /// <summary>
    /// The host hooks only the entry points Streamline names, rather than routing the whole graphics API
    /// through the interposer. Direct3D 12 uses it, since its device comes from the Agility SDK's device
    /// factory; Vulkan does not, and loads its API through the interposer instead.
    /// </summary>
    UseManualHooking = 1UL << 2,

    /// <summary>Allow Streamline to fetch newer plugins over the air.</summary>
    /// <remarks>
    /// Never set. A plugin downloaded at run time is not one the signature check before
    /// <c>slInit</c> ever saw, so taking one would undo the reason that check exists.
    /// </remarks>
    AllowOta = 1UL << 3,

    /// <summary>Load plugins that the over-the-air updater downloaded. Never set, as above.</summary>
    LoadDownloadedPlugins = 1UL << 6,

    /// <summary>
    /// Required to use <c>slSetTagForFrame</c>. Without it only the deprecated, frame-less
    /// <c>slSetTag</c> is legal, and the two cannot be mixed.
    /// </summary>
    UseFrameBasedResourceTagging = 1UL << 7,
}

/// <summary>What a feature reports about the machine it has been asked to run on.</summary>
[Flags]
internal enum SlFeatureRequirementFlags : uint
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>The feature supports Direct3D 11.</summary>
    D3D11Supported = 1 << 0,

    /// <summary>The feature supports Direct3D 12.</summary>
    D3D12Supported = 1 << 1,

    /// <summary>The feature supports Vulkan.</summary>
    VulkanSupported = 1 << 2,

    /// <summary>The feature needs vertical sync to be off.</summary>
    VSyncOffRequired = 1 << 3,

    /// <summary>The feature needs hardware-accelerated GPU scheduling.</summary>
    HardwareSchedulingRequired = 1 << 4,
}

/// <summary>What kind of thing a tagged resource is.</summary>
internal enum SlResourceType : byte
{
    /// <summary>A two-dimensional texture. Everything this backend tags is one.</summary>
    Tex2d = 0,

    /// <summary>A buffer.</summary>
    Buffer = 1,
}

/// <summary>How long a tagged resource stays valid, which decides whether Streamline must copy it.</summary>
internal enum SlResourceLifecycle : uint
{
    /// <summary>
    /// The host may change or destroy it once the tag is taken, so Streamline copies it. Costs video
    /// memory; use only where the resource genuinely cannot be held.
    /// </summary>
    OnlyValidNow = 0,

    /// <summary>
    /// It stays untouched until the frame is presented. What frame generation needs of depth and
    /// motion, because it reads them at present time, long after the evaluate that consumed them.
    /// </summary>
    ValidUntilPresent = 1,

    /// <summary>It stays untouched until <c>slEvaluateFeature</c> returns. Avoids the copy.</summary>
    ValidUntilEvaluate = 2,
}

/// <summary>The feature identifiers this backend can ask Streamline for, from <c>sl_core_types.h</c>.</summary>
internal static class SlFeature
{
    /// <summary>DLSS super resolution, which also provides DLAA.</summary>
    public const uint Dlss = 0;

    /// <summary>NVIDIA Image Scaling. Not loaded; named so a log line reads.</summary>
    public const uint Nis = 2;

    /// <summary>Reflex, the low-latency mode.</summary>
    public const uint Reflex = 3;

    /// <summary>The PC latency markers Reflex is driven by.</summary>
    public const uint Pcl = 4;

    /// <summary>DLSS frame generation, including multi-frame generation.</summary>
    public const uint DlssG = 1000;

    /// <summary>DLSS ray reconstruction.</summary>
    public const uint DlssRr = 1001;
}

/// <summary>The buffer roles a tagged resource can fill, from <c>sl_core_types.h</c>.</summary>
/// <remarks>Only the roles this backend supplies are named; the full table is in the header.</remarks>
internal static class SlBufferType
{
    /// <summary>The hardware depth buffer.</summary>
    public const uint Depth = 0;

    /// <summary>Per-pixel motion vectors.</summary>
    public const uint MotionVectors = 1;

    /// <summary>The final colour before any interface is drawn over it. Frame generation interpolates it.</summary>
    public const uint HudLessColor = 2;

    /// <summary>The colour to upscale, at render resolution, before tone mapping.</summary>
    public const uint ScalingInputColor = 3;

    /// <summary>Where the upscaled colour is written, at output resolution.</summary>
    public const uint ScalingOutputColor = 4;

    /// <summary>The diffuse albedo: what is left of the base colour once the metal has taken its share.</summary>
    public const uint Albedo = 7;

    /// <summary>The specular albedo: the directional reflectance the specular lighting was scaled by.</summary>
    public const uint SpecularAlbedo = 8;

    /// <summary>A one-by-one texture holding the exposure the colour buffer has <em>not</em> had applied.</summary>
    public const uint Exposure = 13;

    /// <summary>The world normal, with roughness in its alpha when the packed mode is asked for.</summary>
    public const uint NormalRoughness = 14;

    /// <summary>The interface's colour, premultiplied, with its coverage in alpha.</summary>
    public const uint UiColorAndAlpha = 23;

    /// <summary>A per-pixel hint that the current frame should be trusted over the history.</summary>
    public const uint BiasCurrentColorHint = 29;

    /// <summary>The scene colour before transparent surfaces were composited over it.</summary>
    public const uint ColorBeforeTransparency = 40;

    /// <summary>The world-space distance a specular ray travelled from the primary surface to its hit.</summary>
    public const uint SpecularHitDistance = 42;

    /// <summary>View-space depth, as an alternative to the hardware depth buffer.</summary>
    public const uint LinearDepth = 49;

    /// <summary>The interface's coverage alone, one channel. Preferred over colour-and-alpha where both exist.</summary>
    public const uint UiAlpha = 69;
}

/// <summary>A GUID identifying one of Streamline's structure types.</summary>
/// <remarks>
/// Streamline validates the type and version of every structure handed to it, so these are not
/// decoration: a zeroed one is rejected. The trailing eight bytes are carried as a single
/// <see cref="ulong"/> rather than a fixed buffer because that keeps the mirror blittable, and the
/// packing order is the array order read little-endian.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct SlStructType(uint data1, ushort data2, ushort data3, ulong data4)
{
    /// <summary>The first GUID field.</summary>
    public readonly uint Data1 = data1;

    /// <summary>The second GUID field.</summary>
    public readonly ushort Data2 = data2;

    /// <summary>The third GUID field.</summary>
    public readonly ushort Data3 = data3;

    /// <summary>The trailing eight GUID bytes, packed little-endian.</summary>
    public readonly ulong Data4 = data4;

    /// <summary>Packs the eight trailing GUID bytes in the order the header writes them.</summary>
    /// <param name="b0">Byte zero.</param>
    /// <param name="b1">Byte one.</param>
    /// <param name="b2">Byte two.</param>
    /// <param name="b3">Byte three.</param>
    /// <param name="b4">Byte four.</param>
    /// <param name="b5">Byte five.</param>
    /// <param name="b6">Byte six.</param>
    /// <param name="b7">Byte seven.</param>
    /// <returns>The packed value.</returns>
    public static ulong Pack(byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte b7) =>
        b0
        | ((ulong)b1 << 8)
        | ((ulong)b2 << 16)
        | ((ulong)b3 << 24)
        | ((ulong)b4 << 32)
        | ((ulong)b5 << 40)
        | ((ulong)b6 << 48)
        | ((ulong)b7 << 56);
}

/// <summary>The chainable, versioned header every Streamline structure begins with.</summary>
/// <remarks>
/// <c>StructVersion</c> is a <c>size_t</c>. Declaring it as a four-byte integer is the single most
/// likely way to get this wrong, and it silently shifts every field that follows.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlStructHeader
{
    /// <summary>The next structure in the optional-parameter chain, or zero.</summary>
    public nint Next;

    /// <summary>Which structure this is.</summary>
    public SlStructType Type;

    /// <summary>Which revision of that structure this is.</summary>
    public nuint StructVersion;

    /// <summary>Builds a header for a structure type and version.</summary>
    /// <param name="type">The structure's GUID.</param>
    /// <param name="version">The structure's version, as declared by its header macro.</param>
    /// <returns>The header.</returns>
    public static SlStructHeader For(SlStructType type, uint version) =>
        new() { Next = 0, Type = type, StructVersion = version };
}

/// <summary>A pair of floats.</summary>
/// <param name="x">The first component.</param>
/// <param name="y">The second component.</param>
[StructLayout(LayoutKind.Sequential)]
internal struct SlFloat2(float x, float y)
{
    /// <summary>The first component.</summary>
    public float X = x;

    /// <summary>The second component.</summary>
    public float Y = y;
}

/// <summary>A triple of floats.</summary>
/// <param name="x">The first component.</param>
/// <param name="y">The second component.</param>
/// <param name="z">The third component.</param>
[StructLayout(LayoutKind.Sequential)]
internal struct SlFloat3(float x, float y, float z)
{
    /// <summary>The first component.</summary>
    public float X = x;

    /// <summary>The second component.</summary>
    public float Y = y;

    /// <summary>The third component.</summary>
    public float Z = z;
}

/// <summary>A four-by-four matrix, stored row-major as Streamline requires.</summary>
/// <remarks>
/// Streamline's matrices act on row vectors (<c>v * M</c>); the engine's act on column vectors
/// (<c>M * v</c>). The same transform is therefore each other's transpose, and everything handed over
/// is written through <see cref="Set"/> from the engine's own matrix with the indices swapped.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlFloat4x4
{
    /// <summary>The sixteen elements, four rows of four.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1051:Do not declare visible instance fields",
        Justification = "A P/Invoke mirror has to be a field with an exact layout.")]
    public unsafe fixed float M[16];

    /// <summary>Writes one element.</summary>
    /// <param name="row">The row, in Streamline's convention.</param>
    /// <param name="column">The column, in Streamline's convention.</param>
    /// <param name="value">The value.</param>
    public unsafe void Set(int row, int column, float value) => M[(row * 4) + column] = value;

    /// <summary>Reads one element.</summary>
    /// <param name="row">The row, in Streamline's convention.</param>
    /// <param name="column">The column, in Streamline's convention.</param>
    /// <returns>The value.</returns>
    public readonly unsafe float Get(int row, int column)
    {
        fixed (float* m = M)
        {
            return m[(row * 4) + column];
        }
    }
}

/// <summary>A rectangle within a tagged resource.</summary>
/// <remarks>
/// Note the field order: top before left. Left zeroed, Streamline uses the whole resource.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlExtent
{
    /// <summary>The topmost row.</summary>
    public uint Top;

    /// <summary>The leftmost column.</summary>
    public uint Left;

    /// <summary>The width in pixels.</summary>
    public uint Width;

    /// <summary>The height in pixels.</summary>
    public uint Height;
}

/// <summary>A version triple.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlVersion
{
    /// <summary>The major component.</summary>
    public uint Major;

    /// <summary>The minor component.</summary>
    public uint Minor;

    /// <summary>The build component.</summary>
    public uint Build;

    /// <inheritdoc/>
    public override readonly string ToString() => $"{Major}.{Minor}.{Build}";
}

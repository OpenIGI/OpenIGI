using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>The flat C entry points <c>sl.interposer.dll</c> exports, bound by address.</summary>
/// <remarks>
/// <para>
/// Bound with <see cref="NativeLibrary.GetExport"/> from a handle loaded by absolute path rather than
/// declared with <c>DllImport</c>: the interposer lives in a folder the launcher chose, never beside the
/// executable, and an import resolver can be registered only once per assembly — a second Vulkan device
/// after an F10 switch would have had nowhere to put its own.
/// </para>
/// <para>
/// Every parameter is a raw pointer. Streamline's signatures take C++ references, which are pointers,
/// and being explicit about that removes any question of what the marshaller decided to do — these calls
/// sit on the render thread and the structures they point at have to stay put for the duration.
/// </para>
/// </remarks>
internal sealed unsafe class SlApi
{
    private SlApi(nint library)
    {
        Init = (delegate* unmanaged[Cdecl]<SlPreferences*, ulong, SlResult>)Export(library, "slInit");
        Shutdown = (delegate* unmanaged[Cdecl]<SlResult>)Export(library, "slShutdown");
        IsFeatureSupported =
            (delegate* unmanaged[Cdecl]<uint, SlAdapterInfo*, SlResult>)Export(library, "slIsFeatureSupported");
        IsFeatureLoaded = (delegate* unmanaged[Cdecl]<uint, byte*, SlResult>)Export(library, "slIsFeatureLoaded");
        SetFeatureLoaded = (delegate* unmanaged[Cdecl]<uint, byte, SlResult>)Export(library, "slSetFeatureLoaded");
        GetFeatureRequirements =
            (delegate* unmanaged[Cdecl]<uint, SlFeatureRequirements*, SlResult>)Export(library, "slGetFeatureRequirements");
        GetFeatureVersion =
            (delegate* unmanaged[Cdecl]<uint, SlFeatureVersion*, SlResult>)Export(library, "slGetFeatureVersion");
        GetFeatureFunction =
            (delegate* unmanaged[Cdecl]<uint, byte*, nint*, SlResult>)Export(library, "slGetFeatureFunction");
        GetNewFrameToken = (delegate* unmanaged[Cdecl]<nint*, uint*, SlResult>)Export(library, "slGetNewFrameToken");
        SetConstants =
            (delegate* unmanaged[Cdecl]<SlConstants*, nint, SlViewportHandle*, SlResult>)Export(library, "slSetConstants");
        SetTagForFrame =
            (delegate* unmanaged[Cdecl]<nint, SlViewportHandle*, SlResourceTag*, uint, nint, SlResult>)Export(
                library, "slSetTagForFrame");
        EvaluateFeature =
            (delegate* unmanaged[Cdecl]<uint, nint, nint*, uint, nint, SlResult>)Export(library, "slEvaluateFeature");
        AllocateResources =
            (delegate* unmanaged[Cdecl]<nint, uint, SlViewportHandle*, SlResult>)Export(library, "slAllocateResources");
        FreeResources =
            (delegate* unmanaged[Cdecl]<uint, SlViewportHandle*, SlResult>)Export(library, "slFreeResources");
    }

    /// <summary>Gets <c>slInit</c>: starts Streamline. Must precede the Vulkan instance.</summary>
    public delegate* unmanaged[Cdecl]<SlPreferences*, ulong, SlResult> Init { get; }

    /// <summary>Gets <c>slShutdown</c>: stops Streamline. Must precede <c>vkDestroyDevice</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlResult> Shutdown { get; }

    /// <summary>Gets <c>slIsFeatureSupported</c>.</summary>
    public delegate* unmanaged[Cdecl]<uint, SlAdapterInfo*, SlResult> IsFeatureSupported { get; }

    /// <summary>Gets <c>slIsFeatureLoaded</c>. Its out-parameter is a C++ <c>bool</c>.</summary>
    public delegate* unmanaged[Cdecl]<uint, byte*, SlResult> IsFeatureLoaded { get; }

    /// <summary>Gets <c>slSetFeatureLoaded</c>. Its parameter is a C++ <c>bool</c>.</summary>
    public delegate* unmanaged[Cdecl]<uint, byte, SlResult> SetFeatureLoaded { get; }

    /// <summary>Gets <c>slGetFeatureRequirements</c>.</summary>
    public delegate* unmanaged[Cdecl]<uint, SlFeatureRequirements*, SlResult> GetFeatureRequirements { get; }

    /// <summary>Gets <c>slGetFeatureVersion</c>.</summary>
    public delegate* unmanaged[Cdecl]<uint, SlFeatureVersion*, SlResult> GetFeatureVersion { get; }

    /// <summary>Gets <c>slGetFeatureFunction</c>. Valid only once the device exists.</summary>
    public delegate* unmanaged[Cdecl]<uint, byte*, nint*, SlResult> GetFeatureFunction { get; }

    /// <summary>Gets <c>slGetNewFrameToken</c>. The token it returns is owned by Streamline.</summary>
    public delegate* unmanaged[Cdecl]<nint*, uint*, SlResult> GetNewFrameToken { get; }

    /// <summary>Gets <c>slSetConstants</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlConstants*, nint, SlViewportHandle*, SlResult> SetConstants { get; }

    /// <summary>Gets <c>slSetTagForFrame</c>.</summary>
    public delegate* unmanaged[Cdecl]<nint, SlViewportHandle*, SlResourceTag*, uint, nint, SlResult> SetTagForFrame { get; }

    /// <summary>Gets <c>slEvaluateFeature</c>. The host restores command-buffer state afterwards.</summary>
    public delegate* unmanaged[Cdecl]<uint, nint, nint*, uint, nint, SlResult> EvaluateFeature { get; }

    /// <summary>Gets <c>slAllocateResources</c>.</summary>
    public delegate* unmanaged[Cdecl]<nint, uint, SlViewportHandle*, SlResult> AllocateResources { get; }

    /// <summary>Gets <c>slFreeResources</c>. Any pending evaluation must have been submitted first.</summary>
    public delegate* unmanaged[Cdecl]<uint, SlViewportHandle*, SlResult> FreeResources { get; }

    /// <summary>Binds every entry point, or reports which one the library lacks.</summary>
    /// <param name="library">The interposer's handle.</param>
    /// <param name="api">The bound table.</param>
    /// <param name="missing">The first entry point not found, when binding failed.</param>
    /// <returns><see langword="true"/> when every entry point was found.</returns>
    public static bool TryBind(nint library, out SlApi? api, out string? missing)
    {
        try
        {
            api = new SlApi(library);
            missing = null;
            return true;
        }
        catch (EntryPointNotFoundException exception)
        {
            api = null;
            missing = exception.Message;
            return false;
        }
    }

    private static nint Export(nint library, string name) =>
        NativeLibrary.TryGetExport(library, name, out nint address)
            ? address
            : throw new EntryPointNotFoundException(name);
}

/// <summary>The feature-specific entry points, fetched by name once the device exists.</summary>
/// <remarks>
/// Streamline does not export these: <c>slDLSSSetOptions</c> and friends are resolved through
/// <c>slGetFeatureFunction</c>, which only answers once the device the plugins attach to has been
/// created. A feature that did not load leaves its entries zero, and every caller checks before calling.
/// </remarks>
internal sealed unsafe class SlFeatureApi
{
    /// <summary>Gets <c>slDLSSGetOptimalSettings</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlDlssOptions*, SlDlssOptimalSettings*, SlResult> DlssGetOptimalSettings { get; init; }

    /// <summary>Gets <c>slDLSSSetOptions</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssOptions*, SlResult> DlssSetOptions { get; init; }

    /// <summary>Gets <c>slDLSSGetState</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssState*, SlResult> DlssGetState { get; init; }

    /// <summary>Gets <c>slDLSSDGetOptimalSettings</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlDlssDOptions*, SlDlssDOptimalSettings*, SlResult> DlssdGetOptimalSettings { get; init; }

    /// <summary>Gets <c>slDLSSDSetOptions</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssDOptions*, SlResult> DlssdSetOptions { get; init; }

    /// <summary>Gets <c>slDLSSDGetState</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssDState*, SlResult> DlssdGetState { get; init; }

    /// <summary>Gets <c>slDLSSGSetOptions</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssGOptions*, SlResult> DlssgSetOptions { get; init; }

    /// <summary>Gets <c>slDLSSGGetState</c>. Its options pointer may be null.</summary>
    public delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssGState*, SlDlssGOptions*, SlResult> DlssgGetState { get; init; }

    /// <summary>Gets <c>slReflexSetOptions</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlReflexOptions*, SlResult> ReflexSetOptions { get; init; }

    /// <summary>Gets <c>slReflexGetState</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlReflexState*, SlResult> ReflexGetState { get; init; }

    /// <summary>Gets <c>slReflexSleep</c>. Its parameter is the frame token.</summary>
    public delegate* unmanaged[Cdecl]<nint, SlResult> ReflexSleep { get; init; }

    /// <summary>Gets <c>slPCLSetMarker</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlPclMarker, nint, SlResult> PclSetMarker { get; init; }

    /// <summary>Gets <c>slPCLSetOptions</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlPclOptions*, SlResult> PclSetOptions { get; init; }

    /// <summary>Gets <c>slPCLGetState</c>.</summary>
    public delegate* unmanaged[Cdecl]<SlPclState*, SlResult> PclGetState { get; init; }
}

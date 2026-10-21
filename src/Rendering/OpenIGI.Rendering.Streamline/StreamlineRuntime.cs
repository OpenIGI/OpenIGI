using System.Runtime.InteropServices;
using System.Text;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Rendering.Streamline;

/// <summary>Owns one Streamline session: from <c>slInit</c> before the device to <c>slShutdown</c> before the device dies.</summary>
/// <remarks>
/// <para>
/// This must be constructed <em>before</em> the device. On Vulkan Streamline's documentation is explicit
/// that the instance and device have to be created after <c>slInit</c>, and the reason is visible in what
/// it does with them: in proxy mode its own <c>vkCreateInstance</c> and <c>vkCreateDevice</c> add the
/// extensions, feature bits and extra queues its plugins need, so a device created first is one it
/// cannot use, and the failure shows up later as a feature that simply reports itself unsupported. On
/// Direct3D 12 the device comes from the Agility SDK's device factory, which the interposer cannot stand
/// in for, so the session runs with manual hooking: the backend hands the device over with
/// <c>slSetD3DDevice</c> and upgrades its DXGI factory so the swapchain is Streamline's to hook.
/// </para>
/// <para>
/// And it must be shut down before the device is destroyed: shutting down tears NGX down against that
/// device, and doing it afterwards faults inside the driver with nothing in the log.
/// </para>
/// <para>
/// Every failure here is survivable. Each is reported once and leaves the caller with no runtime, and
/// the backend runs its own temporal resolve exactly as it would have without one.
/// </para>
/// </remarks>
internal sealed unsafe class StreamlineRuntime : IDisposable
{
    /// <summary>The category every Streamline log line is written under.</summary>
    public const string LogCategory = "Streamline";

    private const string InterposerFileName = "sl.interposer.dll";

    /// <summary>This project's identifier, as NGX requires it.</summary>
    /// <remarks>
    /// It has to be a GUID-shaped string, and NGX rejects anything else — which fails as a bare "could
    /// not initialise NGX, all features requiring it are disabled", with nothing pointing at the
    /// identifier. A readable name here costs DLSS entirely. Generated once and fixed: NVIDIA keys
    /// per-application state off it.
    /// </remarks>
    private const string ProjectId = "cdc444b8-42e2-44d4-9805-0a5a99aec578";

    // Streamline hands log lines to a plain function pointer with no user-data parameter, so the sink has
    // to be reachable statically. One session at a time, so this is not a limitation in practice.
    private static IOpenIgiLogger? _logSink;

    // The interposer stays loaded for the life of the process once loaded. Its plugins keep state of their
    // own and unloading it underneath them is not a supported operation; a second session after an F10
    // switch reuses the same handle.
    private static readonly Lock LoadLock = new();
    private static readonly Dictionary<string, nint> Loaded = new(StringComparer.OrdinalIgnoreCase);

    private readonly IOpenIgiLogger _logger;
    private readonly SlApi _api;
    private readonly uint[] _features;
    private readonly string _interposerPath;
    private SlFeatureApi? _featureApi;
    private Func<uint, bool>? _usable;
    private bool _disposed;

    // Whether the frame the current token names has reached the screen. Until it has, the token is still
    // this frame's, however many times the host's loop comes round.
    private bool _framePresented = true;

    private StreamlineRuntime(IOpenIgiLogger logger, SlApi api, string directory, uint[] features, SlRenderApi renderApi)
    {
        _logger = logger;
        RenderApi = renderApi;
        _api = api;
        Directory = directory;
        _features = features;
        _interposerPath = Path.Combine(directory, InterposerFileName);
    }

    /// <summary>Gets the folder the plugins were loaded from.</summary>
    public string Directory { get; }

    /// <summary>Gets a value indicating whether the session is open.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Gets the features this session asked Streamline to load.</summary>
    public IReadOnlyList<uint> Features => _features;

    /// <summary>Gets the entry points of the features, once <see cref="BindFeatureFunctions"/> has run.</summary>
    public SlFeatureApi? FeatureApi => _featureApi;

    /// <summary>Gets the core entry points.</summary>
    public SlApi Api => _api;

    /// <summary>Gets this frame's token, or zero before <see cref="BeginFrame"/> has minted one.</summary>
    /// <remarks>
    /// One token per frame, shared by everything. Streamline requires that the frame handed to
    /// <c>slSetConstants</c>, <c>slSetTagForFrame</c>, <c>slEvaluateFeature</c>, the Reflex sleep and the
    /// latency markers all match — two features each minting their own would describe two frames.
    /// </remarks>
    public nint CurrentFrame { get; private set; }

    /// <summary>Gets how many frames have been minted, which is the index the current one carries.</summary>
    public uint FrameIndex { get; private set; }

    /// <summary>Starts Streamline, or reports why it could not be started.</summary>
    /// <param name="options">Where the runtime is.</param>
    /// <param name="features">The feature identifiers to load, from <see cref="SlFeature"/>.</param>
    /// <param name="logger">Where to report what happened.</param>
    /// <param name="renderApi">The graphics API the host drives.</param>
    /// <returns>The runtime when it started, otherwise <see langword="null"/>.</returns>
    public static StreamlineRuntime? TryCreate(
        StreamlineOptions options,
        ReadOnlySpan<uint> features,
        IOpenIgiLogger logger,
        SlRenderApi renderApi)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (!OperatingSystem.IsWindows())
        {
            logger.Debug(LogCategory, "Not available on this platform.");
            return null;
        }

        string directory = Path.GetFullPath(options.EffectiveDirectory);
        if (!System.IO.Directory.Exists(directory))
        {
            logger.Information(LogCategory, $"'{directory}' does not exist; running without it.");
            return null;
        }

        string interposer = Path.Combine(directory, InterposerFileName);
        if (!File.Exists(interposer))
        {
            logger.Warning(LogCategory, $"'{directory}' holds no {InterposerFileName}; running without it.");
            return null;
        }

        // Refusing is the whole point: Streamline loads its plugins by name from this folder, so one
        // substituted file anywhere in it runs inside the game with the game's privileges.
        if (!SignedModule.IsFolderTrusted(directory, out string? refused))
        {
            logger.Warning(LogCategory, $"Refusing to load from '{directory}': {refused}.");
            return null;
        }

        if (!TryLoad(interposer, logger, out nint library))
        {
            return null;
        }

        if (!SlApi.TryBind(library, out SlApi? api, out string? missing))
        {
            logger.Warning(LogCategory, $"'{interposer}' lacks the entry point {missing}; it is not a Streamline interposer this build understands.");
            return null;
        }

        StreamlineRuntime runtime = new(logger, api!, directory, features.ToArray(), renderApi);
        if (!runtime.Initialise(options))
        {
            runtime.Dispose();
            return null;
        }

        return runtime;
    }

    /// <summary>Gets the interposer's full path, which a Vulkan host loads its API through in proxy mode.</summary>
    public string InterposerPath => _interposerPath;

    /// <summary>Gets the graphics API this session was started for.</summary>
    public SlRenderApi RenderApi { get; }

    /// <summary>Writes what each loaded feature asks of the machine and the device into the log.</summary>
    /// <remarks>
    /// Nothing acts on these: in proxy mode Streamline adds its own extensions, features and queues when
    /// it forwards instance and device creation. They are logged because when a feature reports itself
    /// unsupported later, this is the record of what it wanted and what the machine had.
    /// </remarks>
    public void LogFeatureRequirements()
    {
        if (!IsAvailable || !_logger.IsEnabled(LogLevel.Debug, LogCategory))
        {
            return;
        }

        foreach (uint feature in _features)
        {
            StreamlineFeatureRequirements? requirements = QueryRequirements(feature);
            if (requirements is null)
            {
                continue;
            }

            _logger.Debug(
                LogCategory,
                $"{Describe(feature)} wants instance extensions [{string.Join(", ", requirements.InstanceExtensions)}], "
                    + $"device extensions [{string.Join(", ", requirements.DeviceExtensions)}], "
                    + $"1.2 features [{string.Join(", ", requirements.Features12)}], "
                    + $"1.3 features [{string.Join(", ", requirements.Features13)}], "
                    + $"queues: {requirements.GraphicsQueues} graphics, {requirements.ComputeQueues} compute, "
                    + $"{requirements.OpticalFlowQueues} optical flow. "
                    + $"Driver {requirements.DriverDetected}, needs {requirements.DriverRequired}.");
        }
    }

    /// <summary>Reads what a feature needs of the machine, the driver and the device.</summary>
    /// <param name="feature">The feature identifier.</param>
    /// <returns>The requirements, or <see langword="null"/> when the feature could not be asked.</returns>
    public StreamlineFeatureRequirements? QueryRequirements(uint feature)
    {
        if (!IsAvailable)
        {
            return null;
        }

        SlFeatureRequirements requirements = SlFeatureRequirements.Create();
        SlResult result = _api.GetFeatureRequirements(feature, &requirements);
        return result == SlResult.Ok ? StreamlineFeatureRequirements.From(in requirements) : null;
    }

    /// <summary>Asks whether a feature can run on an adapter.</summary>
    /// <param name="feature">The feature identifier.</param>
    /// <param name="adapter">The adapter, as the backend names it: its <c>VkPhysicalDevice</c> or its LUID.</param>
    /// <returns>The result, which names the reason when the answer is no.</returns>
    public SlResult IsFeatureSupported(uint feature, in SlAdapterInfo adapter)
    {
        if (!IsAvailable)
        {
            return SlResult.ErrorInitNotCalled;
        }

        SlAdapterInfo copy = adapter;
        return _api.IsFeatureSupported(feature, &copy);
    }

    /// <summary>Hands a Direct3D 12 device to a session started with manual hooking.</summary>
    /// <param name="device">The <c>ID3D12Device</c>.</param>
    /// <returns>The result.</returns>
    /// <remarks>Before anything else asks Streamline about the device: its plugins attach to it here.</remarks>
    public SlResult SetD3DDevice(nint device) =>
        IsAvailable ? _api.SetD3DDevice(device) : SlResult.ErrorInitNotCalled;

    /// <summary>Swaps a DXGI interface for Streamline's proxy of it, so what it creates is Streamline's to hook.</summary>
    /// <param name="native">The interface; on success it is replaced by the proxy, which holds it.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// The factory a swapchain is made from: Reflex's present markers, PCL and frame generation all work
    /// through the hooked present, and with manual hooking nothing is hooked unless the host asks.
    /// </remarks>
    public SlResult UpgradeInterface(ref nint native)
    {
        if (!IsAvailable)
        {
            return SlResult.ErrorInitNotCalled;
        }

        nint value = native;
        SlResult result = _api.UpgradeInterface(&value);
        if (result == SlResult.Ok)
        {
            native = value;
        }

        return result;
    }

    /// <summary>Asks whether a feature's plugin is loaded.</summary>
    /// <param name="feature">The feature identifier.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsFeatureLoaded(uint feature)
    {
        if (!IsAvailable)
        {
            return false;
        }

        byte loaded = 0;
        return _api.IsFeatureLoaded(feature, &loaded) == SlResult.Ok && loaded != 0;
    }

    /// <summary>Loads or unloads a feature's plugin.</summary>
    /// <param name="feature">The feature identifier.</param>
    /// <param name="loaded">Whether it should be loaded.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// Frame generation is the reason this exists: while its plugin is loaded the host renders off-screen
    /// and pays a copy every frame even with the feature off, so it is unloaded whenever it is not wanted
    /// — and the swapchain has to be rebuilt around the change.
    /// </remarks>
    public SlResult SetFeatureLoaded(uint feature, bool loaded) =>
        IsAvailable ? _api.SetFeatureLoaded(feature, loaded ? (byte)1 : (byte)0) : SlResult.ErrorInitNotCalled;

    /// <summary>Reads which Streamline plugin and which trained model are behind a feature.</summary>
    /// <param name="feature">The feature identifier.</param>
    /// <returns>A description for the log, or <see langword="null"/> when it could not be read.</returns>
    public string? DescribeVersion(uint feature)
    {
        if (!IsAvailable)
        {
            return null;
        }

        SlFeatureVersion version = SlFeatureVersion.Create();
        return _api.GetFeatureVersion(feature, &version) == SlResult.Ok
            ? $"plugin {version.VersionSl}, model {version.VersionNgx}"
            : null;
    }

    /// <summary>Resolves every feature's own entry points. Valid only once the device exists.</summary>
    /// <param name="usable">Which features to resolve: asking a plugin that never loaded logs an error per name.</param>
    /// <returns>The entry points; a feature left out leaves its entries zero.</returns>
    public SlFeatureApi BindFeatureFunctions(Func<uint, bool> usable)
    {
        ArgumentNullException.ThrowIfNull(usable);
        _usable = usable;

        if (_featureApi is not null)
        {
            return _featureApi;
        }

        _featureApi = new SlFeatureApi
        {
            DlssGetOptimalSettings =
                (delegate* unmanaged[Cdecl]<SlDlssOptions*, SlDlssOptimalSettings*, SlResult>)Function(SlFeature.Dlss, "slDLSSGetOptimalSettings"),
            DlssSetOptions =
                (delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssOptions*, SlResult>)Function(SlFeature.Dlss, "slDLSSSetOptions"),
            DlssGetState =
                (delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssState*, SlResult>)Function(SlFeature.Dlss, "slDLSSGetState"),
            DlssdGetOptimalSettings =
                (delegate* unmanaged[Cdecl]<SlDlssDOptions*, SlDlssDOptimalSettings*, SlResult>)Function(SlFeature.DlssRr, "slDLSSDGetOptimalSettings"),
            DlssdSetOptions =
                (delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssDOptions*, SlResult>)Function(SlFeature.DlssRr, "slDLSSDSetOptions"),
            DlssdGetState =
                (delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssDState*, SlResult>)Function(SlFeature.DlssRr, "slDLSSDGetState"),
            DlssgSetOptions =
                (delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssGOptions*, SlResult>)Function(SlFeature.DlssG, "slDLSSGSetOptions"),
            DlssgGetState =
                (delegate* unmanaged[Cdecl]<SlViewportHandle*, SlDlssGState*, SlDlssGOptions*, SlResult>)Function(SlFeature.DlssG, "slDLSSGGetState"),
            ReflexSetOptions =
                (delegate* unmanaged[Cdecl]<SlReflexOptions*, SlResult>)Function(SlFeature.Reflex, "slReflexSetOptions"),
            ReflexGetState =
                (delegate* unmanaged[Cdecl]<SlReflexState*, SlResult>)Function(SlFeature.Reflex, "slReflexGetState"),
            ReflexSleep = (delegate* unmanaged[Cdecl]<nint, SlResult>)Function(SlFeature.Reflex, "slReflexSleep"),
            PclSetMarker = (delegate* unmanaged[Cdecl]<SlPclMarker, nint, SlResult>)Function(SlFeature.Pcl, "slPCLSetMarker"),
            PclSetOptions = (delegate* unmanaged[Cdecl]<SlPclOptions*, SlResult>)Function(SlFeature.Pcl, "slPCLSetOptions"),
            PclGetState = (delegate* unmanaged[Cdecl]<SlPclState*, SlResult>)Function(SlFeature.Pcl, "slPCLGetState"),
        };

        return _featureApi;
    }

    /// <summary>Mints the token this frame's Streamline calls will all carry, if a new frame has begun.</summary>
    /// <returns><see langword="true"/> when this call started a new frame.</returns>
    /// <remarks>
    /// <para>
    /// A frame here means a frame that reaches the screen, which is not the same as a pass of the host's
    /// loop. That loop comes round more often than it draws — it pumps events, collects finished bakes and
    /// runs simulation ticks with nothing to render — so a token minted per pass would run several frames
    /// ahead of the presents, and everything keyed off the frame would be describing one that never existed.
    /// </para>
    /// <para>
    /// It cost a real regression once: Reflex sleeps to pace the frame it is given, so a sleep per pass
    /// paced several times per presented frame and took the frame's processor time from about one
    /// millisecond to eight. Hence the guard, rather than trusting the call site.
    /// </para>
    /// </remarks>
    public bool BeginFrame()
    {
        if (!IsAvailable)
        {
            return false;
        }

        if (!_framePresented && CurrentFrame != 0)
        {
            return false;
        }

        nint frame = 0;
        uint index = FrameIndex + 1;
        SlResult result = _api.GetNewFrameToken(&frame, &index);
        if (result != SlResult.Ok)
        {
            _logger.Warning(LogCategory, $"No frame token: {Describe(result)}.");
            CurrentFrame = 0;
            return false;
        }

        FrameIndex = index;
        CurrentFrame = frame;
        _framePresented = false;
        return true;
    }

    /// <summary>Reports that the frame the current token names has been presented.</summary>
    /// <remarks>
    /// What lets the next pass of the host's loop mint the next token. Called from the backend's own
    /// present, because that is the only place that knows a frame actually reached the screen.
    /// </remarks>
    public void EndFrame() => _framePresented = true;

    /// <summary>Gets a value indicating whether a token has been minted and not yet presented.</summary>
    public bool FrameOpen => !_framePresented && CurrentFrame != 0;

    /// <summary>Turns a Streamline result into something worth reading in a log.</summary>
    /// <param name="result">The result code.</param>
    /// <returns>The description.</returns>
    public static string Describe(SlResult result) => result switch
    {
        SlResult.Ok => "ok",
        SlResult.ErrorDriverOutOfDate => "the graphics driver is too old",
        SlResult.ErrorOSOutOfDate => "Windows is too old",
        SlResult.ErrorOSDisabledHWS => "hardware-accelerated GPU scheduling is turned off",
        SlResult.ErrorDeviceNotCreated => "the device was not created through Streamline",
        SlResult.ErrorNoSupportedAdapterFound => "this GPU does not support it",
        SlResult.ErrorAdapterNotSupported => "this adapter is not supported",
        SlResult.ErrorNoPlugins => "no plugins were found",
        SlResult.ErrorNGXFailed => "NGX could not start",
        SlResult.ErrorMissingConstants => "this frame's constants were not set",
        SlResult.ErrorMissingInputParameter => "a required input is missing",
        SlResult.ErrorFeatureMissing => "its plugin is missing",
        SlResult.ErrorFeatureNotSupported => "it is not supported here",
        SlResult.ErrorFeatureFailedToLoad => "its plugin failed to load",
        SlResult.ErrorFeatureMissingDependency => "a plugin it depends on is missing",
        SlResult.ErrorInvalidParameter => "a parameter was invalid",
        SlResult.ErrorInvalidState => "Streamline is not in a state that allows it",
        SlResult.WarnOutOfVRAM => "video memory is running out",
        _ => $"{result} ({(uint)result})",
    };

    /// <summary>Names a feature for a log line.</summary>
    /// <param name="feature">The feature identifier.</param>
    /// <returns>The name.</returns>
    public static string Describe(uint feature) => feature switch
    {
        SlFeature.Dlss => "DLSS",
        SlFeature.Reflex => "Reflex",
        SlFeature.Pcl => "PC latency markers",
        SlFeature.DlssG => "DLSS frame generation",
        SlFeature.DlssRr => "DLSS ray reconstruction",
        _ => $"feature {feature}",
    };

    /// <summary>Ends the session. Must run before the Vulkan device is destroyed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (IsAvailable)
        {
            IsAvailable = false;
            SlResult result = _api.Shutdown();
            if (result != SlResult.Ok)
            {
                _logger.Warning(LogCategory, $"Shut down with {Describe(result)}.");
            }
        }

        if (ReferenceEquals(_logSink, _logger))
        {
            _logSink = null;
        }
    }

    private static bool TryLoad(string path, IOpenIgiLogger logger, out nint library)
    {
        lock (LoadLock)
        {
            if (Loaded.TryGetValue(path, out library))
            {
                return true;
            }

            if (!NativeLibrary.TryLoad(path, out library))
            {
                logger.Warning(LogCategory, $"Could not load '{path}'.");
                return false;
            }

            Loaded[path] = library;
            return true;
        }
    }

    private nint Function(uint feature, string name)
    {
        if (!_features.Contains(feature) || _usable?.Invoke(feature) == false)
        {
            return 0;
        }

        Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetByteCount(name) + 1];
        Encoding.UTF8.GetBytes(name, utf8);
        utf8[^1] = 0;

        nint function = 0;
        fixed (byte* namePtr = utf8)
        {
            SlResult result = _api.GetFeatureFunction(feature, namePtr, &function);
            if (result != SlResult.Ok)
            {
                _logger.Debug(LogCategory, $"{name} is unavailable: {Describe(result)}.");
                return 0;
            }
        }

        return function;
    }

    private bool Initialise(StreamlineOptions options)
    {
        _logSink = _logger;

        string logDirectory = Path.GetFullPath(options.LogDirectory);
        try
        {
            System.IO.Directory.CreateDirectory(logDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(LogCategory, $"Could not create '{logDirectory}'; Streamline will not keep a log.", exception);
            logDirectory = string.Empty;
        }

        // Every pointer handed to slInit has to stay alive for the duration of the call, which is what the
        // fixed blocks and the copies below are for. Streamline copies what it needs.
        uint[] featureIds = _features.Length > 0 ? _features : [SlFeature.Dlss];
        byte[] logPath = Encoding.Unicode.GetBytes(logDirectory + '\0');
        byte[] projectId = Encoding.UTF8.GetBytes(ProjectId + '\0');
        byte[] engineVersion = Encoding.UTF8.GetBytes(EngineVersionString() + '\0');

        fixed (uint* featurePtr = featureIds)
        fixed (byte* logPathPtr = logPath)
        fixed (byte* projectIdPtr = projectId)
        fixed (byte* engineVersionPtr = engineVersion)
        {
            SlPreferences preferences = SlPreferences.Create(RenderApi);
            preferences.LogLevel = _logger.IsEnabled(LogLevel.Debug, LogCategory) ? SlLogLevel.Verbose : SlLogLevel.Default;
            preferences.LogMessageCallback = &OnStreamlineLog;
            preferences.PathToLogsAndData = logDirectory.Length > 0 ? (nint)logPathPtr : 0;

            // No pathsToPlugins: Streamline searches the interposer's own folder already, and naming it
            // again has every plugin discovered twice and the second copy discarded with a warning.
            preferences.FeaturesToLoad = (nint)featurePtr;
            preferences.NumFeaturesToLoad = (uint)_features.Length;
            preferences.ProjectId = (nint)projectIdPtr;
            preferences.EngineVersion = (nint)engineVersionPtr;

            // Frame-based tagging is not optional: slSetTagForFrame refuses to work without it.
            preferences.Flags |= SlPreferenceFlags.UseFrameBasedResourceTagging;

            // Direct3D 12's device comes from the Agility SDK's device factory, which the interposer's
            // D3D12CreateDevice cannot stand in for: the backend hands the device over and upgrades its DXGI
            // factory itself. Vulkan stays in proxy mode, its API loaded through the interposer.
            if (RenderApi == SlRenderApi.D3D12)
            {
                preferences.Flags |= SlPreferenceFlags.UseManualHooking;
            }

            if (!options.Development)
            {
                preferences.Flags |= SlPreferenceFlags.DisableDebugText;
            }

            SlResult result = _api.Init(&preferences, SlAbi.SdkVersion);
            if (result != SlResult.Ok)
            {
                _logger.Information(LogCategory, $"Could not start: {Describe(result)}.");
                return false;
            }
        }

        IsAvailable = true;
        _logger.Information(
            LogCategory,
            $"Started{(options.Development ? " (development runtime: overlays on, never ship this)" : string.Empty)}, "
                + $"plugins from '{Directory}', log in '{logDirectory}'.");
        return true;
    }

    private static string EngineVersionString() =>
        typeof(StreamlineRuntime).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Whether a Streamline warning is one every start on this platform gives, and no fault of ours.</summary>
    /// <param name="text">The warning.</param>
    /// <returns><see langword="true"/> for the warnings that are expected and harmless here.</returns>
    /// <remarks>
    /// <para>
    /// The common plugin asks to hook vkCmdBindPipeline, vkCmdBindDescriptorSets and vkBeginCommandBuffer, which
    /// Streamline 2.14's Vulkan interposer has no hooks for (its plugin manager maps only the present, swapchain,
    /// surface and device-wait-idle calls), so every Vulkan application logs the three. Those hooks exist to
    /// restore command-buffer state after an evaluation, which with command-list state tracking off -- now
    /// Streamline's default, and set here -- the application does itself; this one rebinds everything per pass.
    /// </para>
    /// <para>
    /// The frame-generation plugin, on a GPU that cannot run it, says so three ways as it declines to load. The
    /// renderer reports the same fact once, in its own words, as frame generation's reason.
    /// </para>
    /// <para>
    /// Kept in the log, at information, rather than dropped: they say what Streamline did.
    /// </para>
    /// </remarks>
    private static bool IsExpectedWarning(string text) =>
        (text.Contains("processPluginHooks", StringComparison.Ordinal)
            && text.Contains("Hook sl.common:Vulkan:", StringComparison.Ordinal)
            && text.Contains("is NOT supported", StringComparison.Ordinal))
        || text.Contains("Failed to obtain DLSS-G min spec requirements", StringComparison.Ordinal)
        || text.Contains("Disabling DLSS-G since it is not supported on current hardware", StringComparison.Ordinal)
        || text.Contains("Ignoring plugin 'sl.dlss_g' since it is not supported", StringComparison.Ordinal);

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnStreamlineLog(SlLogType type, nint message)
    {
        // Called from Streamline's own threads, and an exception escaping into native code terminates the
        // process. Nothing in here is allowed to throw.
        try
        {
            IOpenIgiLogger? logger = _logSink;
            if (logger is null || message == 0)
            {
                return;
            }

            string? text = Marshal.PtrToStringUTF8(message)?.TrimEnd('\r', '\n');
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            switch (type)
            {
                case SlLogType.Error:
                    logger.Error(LogCategory, text);
                    break;
                case SlLogType.Warn when IsExpectedWarning(text):
                    logger.Information(LogCategory, text);
                    break;
                case SlLogType.Warn:
                    logger.Warning(LogCategory, text);
                    break;
                default:
                    logger.Debug(LogCategory, text);
                    break;
            }
        }
        catch (Exception)
        {
            // Deliberately swallowed. There is nowhere to report a logging failure to.
        }
    }
}

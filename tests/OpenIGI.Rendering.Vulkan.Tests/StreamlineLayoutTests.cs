using System.Runtime.InteropServices;
using OpenIGI.Rendering.Vulkan.Streamline;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Pins the Streamline structure layouts and enum values against what its own compiler reports.
/// </summary>
/// <remarks>
/// <para>
/// The interop mirrors a C++ ABI without a native shim in between, which is only safe while the
/// mirrors are exact. A wrong offset does not throw: it hands a plugin a pointer built from the wrong
/// bytes, and the failure surfaces as a corrupt frame or a device loss somewhere else entirely.
/// </para>
/// <para>
/// Every number below came from a throwaway program compiled with MSVC against the Streamline 2.14.1
/// headers, printing <c>sizeof</c>, <c>offsetof</c>, each structure's declared version and the enum
/// values that matter. They are the compiler's answer, not a derivation, which is the point — this
/// test is worthless if the expectations are worked out the same way the code was. Regenerate them the
/// same way when the SDK is upgraded, and move <see cref="SlAbi.SdkVersion"/> with them.
/// </para>
/// </remarks>
public sealed unsafe class StreamlineLayoutTests
{
    [Fact]
    public void Sdk_version_matches_the_headers()
    {
        Assert.Equal(0x0002_000E_0001_FEDCUL, SlAbi.SdkVersion);
    }

    [Fact]
    public void Header_matches_the_sdk()
    {
        Assert.Equal(16, sizeof(SlStructType));
        Assert.Equal(32, sizeof(SlStructHeader));
        Assert.Equal(0, Offset<SlStructHeader>(nameof(SlStructHeader.Next)));
        Assert.Equal(8, Offset<SlStructHeader>(nameof(SlStructHeader.Type)));

        // The one that would silently shift every field in every structure: size_t, not uint32_t.
        Assert.Equal(24, Offset<SlStructHeader>(nameof(SlStructHeader.StructVersion)));
    }

    [Fact]
    public void Primitives_match_the_sdk()
    {
        Assert.Equal(16, sizeof(SlExtent));
        Assert.Equal(8, sizeof(SlFloat2));
        Assert.Equal(12, sizeof(SlFloat3));
        Assert.Equal(64, sizeof(SlFloat4x4));
        Assert.Equal(12, sizeof(SlVersion));

        // enum Boolean : char and enum class ResourceType : char -- one byte each, not four.
        Assert.Equal(1, sizeof(SlBoolean));
        Assert.Equal(1, sizeof(SlResourceType));
        Assert.Equal(4, sizeof(SlResourceLifecycle));
    }

    [Fact]
    public void Resource_matches_the_sdk()
    {
        Assert.Equal(112, sizeof(SlResource));
        AssertOffsets<SlResource>(
            (nameof(SlResource.Type), 32),
            (nameof(SlResource.Native), 40),
            (nameof(SlResource.Memory), 48),
            (nameof(SlResource.View), 56),
            (nameof(SlResource.State), 64),
            (nameof(SlResource.Width), 68),
            (nameof(SlResource.Height), 72),
            (nameof(SlResource.NativeFormat), 76),
            (nameof(SlResource.MipLevels), 80),
            (nameof(SlResource.ArrayLayers), 84),
            (nameof(SlResource.GpuVirtualAddress), 88),
            (nameof(SlResource.Flags), 96),
            (nameof(SlResource.Usage), 100),
            (nameof(SlResource.InternalFlags), 104),
            (nameof(SlResource.Reserved), 106));
    }

    [Fact]
    public void ResourceTag_matches_the_sdk()
    {
        Assert.Equal(64, sizeof(SlResourceTag));
        AssertOffsets<SlResourceTag>(
            (nameof(SlResourceTag.Resource), 32),
            (nameof(SlResourceTag.Type), 40),
            (nameof(SlResourceTag.Lifecycle), 44),
            (nameof(SlResourceTag.Extent), 48));
    }

    [Fact]
    public void Constants_match_the_sdk()
    {
        Assert.Equal(456, sizeof(SlConstants));
        Assert.Equal(2u, (uint)SlConstants.Create().Header.StructVersion);
        AssertOffsets<SlConstants>(
            (nameof(SlConstants.CameraViewToClip), 32),
            (nameof(SlConstants.ClipToCameraView), 96),
            (nameof(SlConstants.ClipToLensClip), 160),
            (nameof(SlConstants.ClipToPrevClip), 224),
            (nameof(SlConstants.PrevClipToClip), 288),
            (nameof(SlConstants.JitterOffset), 352),
            (nameof(SlConstants.MvecScale), 360),
            (nameof(SlConstants.CameraPinholeOffset), 368),
            (nameof(SlConstants.CameraPos), 376),
            (nameof(SlConstants.CameraUp), 388),
            (nameof(SlConstants.CameraRight), 400),
            (nameof(SlConstants.CameraFwd), 412),
            (nameof(SlConstants.CameraNear), 424),
            (nameof(SlConstants.CameraFar), 428),
            (nameof(SlConstants.CameraFov), 432),
            (nameof(SlConstants.CameraAspectRatio), 436),
            (nameof(SlConstants.MotionVectorsInvalidValue), 440),
            (nameof(SlConstants.DepthInverted), 444),
            (nameof(SlConstants.CameraMotionIncluded), 445),
            (nameof(SlConstants.MotionVectors3D), 446),
            (nameof(SlConstants.Reset), 447),
            (nameof(SlConstants.OrthographicProjection), 448),
            (nameof(SlConstants.MotionVectorsDilated), 449),
            (nameof(SlConstants.MotionVectorsJittered), 450),
            (nameof(SlConstants.MinRelativeLinearDepthObjectSeparation), 452));
    }

    [Fact]
    public void Preferences_match_the_sdk()
    {
        Assert.Equal(144, sizeof(SlPreferences));
        AssertOffsets<SlPreferences>(
            (nameof(SlPreferences.ShowConsole), 32),
            (nameof(SlPreferences.LogLevel), 36),
            (nameof(SlPreferences.PathsToPlugins), 40),
            (nameof(SlPreferences.NumPathsToPlugins), 48),
            (nameof(SlPreferences.PathToLogsAndData), 56),
            (nameof(SlPreferences.AllocateCallback), 64),
            (nameof(SlPreferences.ReleaseCallback), 72),
            (nameof(SlPreferences.LogMessageCallback), 80),
            (nameof(SlPreferences.Flags), 88),
            (nameof(SlPreferences.FeaturesToLoad), 96),
            (nameof(SlPreferences.NumFeaturesToLoad), 104),
            (nameof(SlPreferences.ApplicationId), 108),
            (nameof(SlPreferences.Engine), 112),
            (nameof(SlPreferences.EngineVersion), 120),
            (nameof(SlPreferences.ProjectId), 128),
            (nameof(SlPreferences.RenderApi), 136));
    }

    [Fact]
    public void Preferences_never_ask_for_over_the_air_plugins()
    {
        SlPreferences preferences = SlPreferences.Create();

        // A downloaded plugin is one the signature check before slInit never saw.
        Assert.False(preferences.Flags.HasFlag(SlPreferenceFlags.AllowOta));
        Assert.False(preferences.Flags.HasFlag(SlPreferenceFlags.LoadDownloadedPlugins));
        Assert.Equal(SlRenderApi.Vulkan, preferences.RenderApi);
    }

    [Fact]
    public void Adapter_requirements_version_and_viewport_match_the_sdk()
    {
        Assert.Equal(56, sizeof(SlAdapterInfo));
        AssertOffsets<SlAdapterInfo>(
            (nameof(SlAdapterInfo.DeviceLuid), 32),
            (nameof(SlAdapterInfo.DeviceLuidSizeInBytes), 40),
            (nameof(SlAdapterInfo.VkPhysicalDevice), 48));

        Assert.Equal(184, sizeof(SlFeatureRequirements));
        Assert.Equal(2u, (uint)SlFeatureRequirements.Create().Header.StructVersion);
        AssertOffsets<SlFeatureRequirements>(
            (nameof(SlFeatureRequirements.Flags), 32),
            (nameof(SlFeatureRequirements.MaxNumCpuThreads), 36),
            (nameof(SlFeatureRequirements.MaxNumViewports), 40),
            (nameof(SlFeatureRequirements.NumRequiredTags), 44),
            (nameof(SlFeatureRequirements.RequiredTags), 48),
            (nameof(SlFeatureRequirements.OsVersionDetected), 56),
            (nameof(SlFeatureRequirements.OsVersionRequired), 68),
            (nameof(SlFeatureRequirements.DriverVersionDetected), 80),
            (nameof(SlFeatureRequirements.DriverVersionRequired), 92),
            (nameof(SlFeatureRequirements.VkNumComputeQueuesRequired), 104),
            (nameof(SlFeatureRequirements.VkNumGraphicsQueuesRequired), 108),
            (nameof(SlFeatureRequirements.VkNumDeviceExtensions), 112),
            (nameof(SlFeatureRequirements.VkDeviceExtensions), 120),
            (nameof(SlFeatureRequirements.VkNumInstanceExtensions), 128),
            (nameof(SlFeatureRequirements.VkInstanceExtensions), 136),
            (nameof(SlFeatureRequirements.VkNumFeatures12), 144),
            (nameof(SlFeatureRequirements.VkFeatures12), 152),
            (nameof(SlFeatureRequirements.VkNumFeatures13), 160),
            (nameof(SlFeatureRequirements.VkFeatures13), 168),
            (nameof(SlFeatureRequirements.VkNumOpticalFlowQueuesRequired), 176));

        Assert.Equal(56, sizeof(SlFeatureVersion));
        AssertOffsets<SlFeatureVersion>(
            (nameof(SlFeatureVersion.VersionSl), 32),
            (nameof(SlFeatureVersion.VersionNgx), 44));

        Assert.Equal(40, sizeof(SlViewportHandle));
    }

    [Fact]
    public void Dlss_structures_match_the_sdk()
    {
        Assert.Equal(88, sizeof(SlDlssOptions));
        Assert.Equal(3u, (uint)SlDlssOptions.Create().Header.StructVersion);
        AssertOffsets<SlDlssOptions>(
            (nameof(SlDlssOptions.Mode), 32),
            (nameof(SlDlssOptions.OutputWidth), 36),
            (nameof(SlDlssOptions.OutputHeight), 40),
            (nameof(SlDlssOptions.Sharpness), 44),
            (nameof(SlDlssOptions.PreExposure), 48),
            (nameof(SlDlssOptions.ExposureScale), 52),
            (nameof(SlDlssOptions.ColorBuffersHdr), 56),
            (nameof(SlDlssOptions.IndicatorInvertAxisX), 57),
            (nameof(SlDlssOptions.IndicatorInvertAxisY), 58),
            (nameof(SlDlssOptions.DlaaPreset), 60),
            (nameof(SlDlssOptions.QualityPreset), 64),
            (nameof(SlDlssOptions.BalancedPreset), 68),
            (nameof(SlDlssOptions.PerformancePreset), 72),
            (nameof(SlDlssOptions.UltraPerformancePreset), 76),
            (nameof(SlDlssOptions.UltraQualityPreset), 80),
            (nameof(SlDlssOptions.UseAutoExposure), 84),
            (nameof(SlDlssOptions.AlphaUpscalingEnabled), 85));

        Assert.Equal(64, sizeof(SlDlssOptimalSettings));
        AssertOffsets<SlDlssOptimalSettings>(
            (nameof(SlDlssOptimalSettings.OptimalRenderWidth), 32),
            (nameof(SlDlssOptimalSettings.OptimalRenderHeight), 36),
            (nameof(SlDlssOptimalSettings.OptimalSharpness), 40),
            (nameof(SlDlssOptimalSettings.RenderWidthMin), 44),
            (nameof(SlDlssOptimalSettings.RenderHeightMin), 48),
            (nameof(SlDlssOptimalSettings.RenderWidthMax), 52),
            (nameof(SlDlssOptimalSettings.RenderHeightMax), 56));

        Assert.Equal(40, sizeof(SlDlssState));
    }

    [Fact]
    public void Ray_reconstruction_structures_match_the_sdk()
    {
        Assert.Equal(224, sizeof(SlDlssDOptions));
        Assert.Equal(3u, (uint)SlDlssDOptions.Create().Header.StructVersion);
        AssertOffsets<SlDlssDOptions>(
            (nameof(SlDlssDOptions.Mode), 32),
            (nameof(SlDlssDOptions.OutputWidth), 36),
            (nameof(SlDlssDOptions.OutputHeight), 40),
            (nameof(SlDlssDOptions.Sharpness), 44),
            (nameof(SlDlssDOptions.PreExposure), 48),
            (nameof(SlDlssDOptions.ExposureScale), 52),
            (nameof(SlDlssDOptions.ColorBuffersHdr), 56),
            (nameof(SlDlssDOptions.IndicatorInvertAxisX), 57),
            (nameof(SlDlssDOptions.IndicatorInvertAxisY), 58),
            (nameof(SlDlssDOptions.NormalRoughnessMode), 60),
            (nameof(SlDlssDOptions.WorldToCameraView), 64),
            (nameof(SlDlssDOptions.CameraViewToWorld), 128),
            (nameof(SlDlssDOptions.AlphaUpscalingEnabled), 192),
            (nameof(SlDlssDOptions.DlaaPreset), 196),
            (nameof(SlDlssDOptions.QualityPreset), 200),
            (nameof(SlDlssDOptions.BalancedPreset), 204),
            (nameof(SlDlssDOptions.PerformancePreset), 208),
            (nameof(SlDlssDOptions.UltraPerformancePreset), 212),
            (nameof(SlDlssDOptions.UltraQualityPreset), 216));

        Assert.Equal(64, sizeof(SlDlssDOptimalSettings));
        Assert.Equal(40, sizeof(SlDlssDState));
        Assert.Equal(6u, (uint)SlDlssDPreset.PresetF);
    }

    [Fact]
    public void Frame_generation_structures_match_the_sdk()
    {
        Assert.Equal(120, sizeof(SlDlssGOptions));
        Assert.Equal(5u, (uint)SlDlssGOptions.Create().Header.StructVersion);
        AssertOffsets<SlDlssGOptions>(
            (nameof(SlDlssGOptions.Mode), 32),
            (nameof(SlDlssGOptions.NumFramesToGenerate), 36),
            (nameof(SlDlssGOptions.Flags), 40),
            (nameof(SlDlssGOptions.DynamicResWidth), 44),
            (nameof(SlDlssGOptions.DynamicResHeight), 48),
            (nameof(SlDlssGOptions.NumBackBuffers), 52),
            (nameof(SlDlssGOptions.MvecDepthWidth), 56),
            (nameof(SlDlssGOptions.MvecDepthHeight), 60),
            (nameof(SlDlssGOptions.ColorWidth), 64),
            (nameof(SlDlssGOptions.ColorHeight), 68),
            (nameof(SlDlssGOptions.ColorBufferFormat), 72),
            (nameof(SlDlssGOptions.MvecBufferFormat), 76),
            (nameof(SlDlssGOptions.DepthBufferFormat), 80),
            (nameof(SlDlssGOptions.HudLessBufferFormat), 84),
            (nameof(SlDlssGOptions.UiBufferFormat), 88),
            (nameof(SlDlssGOptions.OnErrorCallback), 96),
            (nameof(SlDlssGOptions.Reserved15), 104),
            (nameof(SlDlssGOptions.QueueParallelismMode), 108),
            (nameof(SlDlssGOptions.EnableUserInterfaceRecomposition), 112),
            (nameof(SlDlssGOptions.DynamicTargetFrameRate), 116));

        Assert.Equal(88, sizeof(SlDlssGState));
        Assert.Equal(4u, (uint)SlDlssGState.Create().Header.StructVersion);
        AssertOffsets<SlDlssGState>(
            (nameof(SlDlssGState.EstimatedVramUsageInBytes), 32),
            (nameof(SlDlssGState.Status), 40),
            (nameof(SlDlssGState.MinWidthOrHeight), 44),
            (nameof(SlDlssGState.NumFramesActuallyPresented), 48),
            (nameof(SlDlssGState.NumFramesToGenerateMax), 52),
            (nameof(SlDlssGState.Reserved4), 56),
            (nameof(SlDlssGState.IsVsyncSupportAvailable), 57),
            (nameof(SlDlssGState.InputsProcessingCompletionFence), 64),
            (nameof(SlDlssGState.LastPresentInputsProcessingCompletionFenceValue), 72),
            (nameof(SlDlssGState.IsDynamicMfgSupported), 80));

        Assert.Equal(16u, (uint)SlDlssGFlags.EnableFullscreenMenuDetection);
        Assert.Equal(3u, (uint)SlDlssGMode.Dynamic);
    }

    [Fact]
    public void Reflex_and_latency_structures_match_the_sdk()
    {
        Assert.Equal(48, sizeof(SlReflexOptions));
        AssertOffsets<SlReflexOptions>(
            (nameof(SlReflexOptions.Mode), 32),
            (nameof(SlReflexOptions.FrameLimitUs), 36),
            (nameof(SlReflexOptions.UseMarkersToOptimize), 40),
            (nameof(SlReflexOptions.VirtualKey), 42),
            (nameof(SlReflexOptions.IdThread), 44));

        Assert.Equal(152, sizeof(SlReflexReport));
        Assert.Equal(12848, sizeof(SlReflexState));
        Assert.Equal(2u, (uint)SlReflexState.Create().Header.StructVersion);
        AssertOffsets<SlReflexState>(
            (nameof(SlReflexState.LowLatencyAvailable), 32),
            (nameof(SlReflexState.LatencyReportAvailable), 33),
            (nameof(SlReflexState.StatsWindowMessage), 36),
            (nameof(SlReflexState.FrameReports), 40),
            (nameof(SlReflexState.FlashIndicatorDriverControlled), 9768),
            (nameof(SlReflexState.FrameReports2), 9776));

        Assert.Equal(40, sizeof(SlPclOptions));
        AssertOffsets<SlPclOptions>(
            (nameof(SlPclOptions.VirtualKey), 32),
            (nameof(SlPclOptions.IdThread), 36));
        Assert.Equal(40, sizeof(SlPclState));

        Assert.Equal(0u, (uint)SlPclMarker.SimulationStart);
        Assert.Equal(5u, (uint)SlPclMarker.PresentEnd);
        Assert.Equal(7u, (uint)SlPclMarker.TriggerFlash);
        Assert.Equal(8u, (uint)SlPclMarker.PcLatencyPing);
    }

    [Fact]
    public void Dynamic_vibrance_structures_match_the_sdk()
    {
        Assert.Equal(48, sizeof(SlDeepDvcOptions));
        AssertOffsets<SlDeepDvcOptions>(
            (nameof(SlDeepDvcOptions.Mode), 32),
            (nameof(SlDeepDvcOptions.Intensity), 36),
            (nameof(SlDeepDvcOptions.SaturationBoost), 40));
        Assert.Equal(40, sizeof(SlDeepDvcState));
    }

    [Fact]
    public void Identifiers_match_the_sdk()
    {
        Assert.Equal(2u, (uint)SlResult.ErrorDriverOutOfDate);
        Assert.Equal(6u, (uint)SlResult.ErrorNoSupportedAdapterFound);
        Assert.Equal(31u, (uint)SlResult.ErrorFeatureMissing);
        Assert.Equal(32u, (uint)SlResult.ErrorFeatureNotSupported);
        Assert.Equal(38u, (uint)SlResult.ErrorInvalidState);
        Assert.Equal(39u, (uint)SlResult.WarnOutOfVRAM);
        Assert.Equal(6u, (uint)SlDlssMode.Dlaa);
        Assert.Equal(13u, (uint)SlDlssPreset.PresetM);

        Assert.Equal(5u, SlFeature.DeepDvc);
        Assert.Equal(1000u, SlFeature.DlssG);
        Assert.Equal(1001u, SlFeature.DlssRr);

        Assert.Equal(2u, SlBufferType.HudLessColor);
        Assert.Equal(13u, SlBufferType.Exposure);
        Assert.Equal(23u, SlBufferType.UiColorAndAlpha);
        Assert.Equal(29u, SlBufferType.BiasCurrentColorHint);
        Assert.Equal(40u, SlBufferType.ColorBeforeTransparency);
        Assert.Equal(42u, SlBufferType.SpecularHitDistance);
        Assert.Equal(49u, SlBufferType.LinearDepth);
        Assert.Equal(69u, SlBufferType.UiAlpha);
    }

    [Fact]
    public void Struct_type_packs_in_header_order()
    {
        // {6AC826E4-4C61-4101-A92D-638D421057B8}: byte 0 of Data4 is 0xa9, the lowest byte.
        Assert.Equal(0xb8_57_10_42_8d_63_2d_a9UL, SlDlssOptions.TypeId.Data4);
    }

    [Fact]
    public void Matrix_set_writes_row_major()
    {
        SlFloat4x4 matrix = default;
        matrix.Set(1, 2, 7f);
        Assert.Equal(7f, matrix.M[6]);
        Assert.Equal(7f, matrix.Get(1, 2));
    }

    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    private static void AssertOffsets<T>(params (string Field, int Offset)[] expected)
    {
        foreach ((string field, int offset) in expected)
        {
            Assert.True(offset == Offset<T>(field), $"{typeof(T).Name}.{field}: expected {offset}, found {Offset<T>(field)}");
        }
    }
}

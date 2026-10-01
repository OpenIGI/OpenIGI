using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// What an adapter says it can do, in the renderer's terms.
/// </summary>
/// <remarks>
/// Read from the adapter rather than assumed, and separated from the device so it can be tested with a
/// set of features and a limits structure filled in by hand — which is the only way to check the reading
/// at all, since the interesting cases are adapters this machine does not have.
/// </remarks>
public static class WebGpuCapabilities
{
    /// <summary>The features a set of WebGPU feature names amounts to.</summary>
    /// <param name="supported">What the adapter reports.</param>
    /// <param name="rayQuery">Whether the native ray-query feature is among them.</param>
    /// <returns>The renderer's features.</returns>
    public static RhiFeatures Features(ReadOnlySpan<WGPUFeatureName> supported, bool rayQuery)
    {
        bool timestamp = false;
        bool float32Filterable = false;
        bool depthClamp = false;
        bool indirectFirstInstance = false;
        bool compressionBc = false;

        foreach (WGPUFeatureName feature in supported)
        {
            switch (feature)
            {
                case WGPUFeatureName.TimestampQuery:
                    timestamp = true;
                    break;

                case WGPUFeatureName.Float32Filterable:
                    float32Filterable = true;
                    break;

                // WebGPU's name for it is depth-clip-control, and it turns clipping OFF rather than
                // clamping on; the effect a shadow caster behind the near plane needs is the same.
                case WGPUFeatureName.DepthClipControl:
                    depthClamp = true;
                    break;

                case WGPUFeatureName.IndirectFirstInstance:
                    indirectFirstInstance = true;
                    break;

                case WGPUFeatureName.TextureCompressionBC:
                    compressionBc = true;
                    break;

                default:
                    break;
            }
        }

        return new RhiFeatures(
            RayQuery: rayQuery,
            TimestampQuery: timestamp,
            Float32Filterable: float32Filterable,
            DepthClamp: depthClamp,
            IndirectFirstInstance: indirectFirstInstance,
            TextureCompressionBc: compressionBc);
    }

    /// <summary>Whether a set of features includes the extended storage formats.</summary>
    /// <param name="supported">What the adapter reports.</param>
    /// <returns><see langword="true"/> when a narrow format may carry a storage binding.</returns>
    /// <remarks>
    /// Its own question rather than part of <see cref="RhiFeatures"/>, because nothing above the backend
    /// should be deciding what a format is allocated as. See <see cref="WebGpuFormatPolicy"/>.
    /// </remarks>
    public static bool HasExtendedStorageFormats(ReadOnlySpan<WGPUFeatureName> supported)
    {
        foreach (WGPUFeatureName feature in supported)
        {
            if (feature is WGPUFeatureName.TextureFormatsTier1 or WGPUFeatureName.TextureFormatsTier2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The limits a WebGPU limits structure amounts to.</summary>
    /// <param name="limits">What the adapter reports.</param>
    /// <param name="maxSampleCount">
    /// The highest multisample count to offer. WebGPU does not report one — it requires four and permits
    /// no more without a feature — so it is stated rather than read.
    /// </param>
    /// <returns>The renderer's limits.</returns>
    /// <remarks>
    /// <para>
    /// Two are not a straight copy. A dispatch's largest extent is reported per axis and the renderer
    /// keeps one number, so the smallest of the three is taken: a pass that fits that fits on every axis.
    /// </para>
    /// <para>
    /// Push data has no WebGPU equivalent at all. What is reported here is what the implementation will
    /// spend on the uniform ring it emulates them with, which is the number a pipeline layout is checked
    /// against.
    /// </para>
    /// </remarks>
    public static RhiLimits Limits(in WGPULimits limits, int maxSampleCount = 4) => new(
        MaxTextureDimension2D: (int)limits.maxTextureDimension2D,
        MaxTextureDimension3D: (int)limits.maxTextureDimension3D,
        MaxTextureArrayLayers: (int)limits.maxTextureArrayLayers,
        MaxBindGroups: (int)limits.maxBindGroups,
        MaxSamplersPerStage: (int)limits.maxSamplersPerShaderStage,
        MaxSampledTexturesPerStage: (int)limits.maxSampledTexturesPerShaderStage,
        MaxStorageBuffersPerStage: (int)limits.maxStorageBuffersPerShaderStage,
        MaxStorageTexturesPerStage: (int)limits.maxStorageTexturesPerShaderStage,
        MaxUniformBufferBindingSize: limits.maxUniformBufferBindingSize,
        MaxStorageBufferBindingSize: limits.maxStorageBufferBindingSize,
        MaxPushDataBytes: PushDataBytes,
        MaxComputeWorkgroupStorageBytes: (int)limits.maxComputeWorkgroupStorageSize,
        MaxComputeInvocationsPerWorkgroup: (int)limits.maxComputeInvocationsPerWorkgroup,
        MaxComputeWorkgroupsPerDimension: (int)limits.maxComputeWorkgroupsPerDimension,
        MaxColorAttachments: (int)limits.maxColorAttachments,
        MaxSampleCount: maxSampleCount);

    /// <summary>How many bytes of push data this backend emulates.</summary>
    /// <remarks>
    /// WebGPU has no push constants. The implementation binds a slice of a per-frame uniform ring by
    /// dynamic offset instead, and a slice has to be aligned to 256 bytes, so 256 is both the smallest
    /// useful size and the one that costs nothing extra.
    /// </remarks>
    public const int PushDataBytes = 256;
}

using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Sky;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;
using Xunit.Abstractions;
using PipelineSky = OpenIGI.Rendering.Pipeline.Sky.SkyAtmosphere;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// The atmosphere marched on both backends, arriving at the same sky.
/// </summary>
/// <remarks>
/// Four compute passes in a chain, each reading what the one before wrote, with no barrier stated
/// anywhere: the whole point of the RHI's promise is that the backend places them, and a backend that
/// placed one wrongly would produce a sky built from a half-written table. What is compared is the
/// harmonics the last pass reduces the sky to — twenty-eight numbers that every surface in a level
/// reads its ambient light from, so a disagreement there is a disagreement about every frame.
/// </remarks>
public sealed class SkyAgreementTests(ITestOutputHelper output)
{
    [Fact]
    public void Both_backends_march_the_same_atmosphere()
    {
        float[]? vulkan = TryOnVulkan(out string? vulkanReason);
        float[]? webgpu = TryOnWebGpu(out string? webgpuReason);

        if (vulkan is null || webgpu is null)
        {
            Assert.Fail(
                "Both backends are needed to compare them, and one was missing: "
                + (vulkan is null ? vulkanReason : webgpuReason));

            return;
        }

        float worst = 0f;

        for (int i = 0; i < vulkan.Length; i++)
        {
            Assert.False(float.IsNaN(vulkan[i]) || float.IsNaN(webgpu[i]), $"Coefficient {i} is NaN.");
            worst = Math.Max(worst, Math.Abs(vulkan[i] - webgpu[i]));
        }

        output.WriteLine($"worst coefficient difference {worst}");
        output.WriteLine($"vulkan {vulkan[0]}, {vulkan[1]}, {vulkan[2]}");
        output.WriteLine($"webgpu {webgpu[0]}, {webgpu[1]}, {webgpu[2]}");

        // A ray-march of a hundred-odd steps, so the two are not obliged to agree to the last bit of a
        // float; they are obliged to agree about the sky.
        Assert.True(worst <= 1e-4f, $"The two backends march to different skies, worst by {worst}.");

        // And that a sky was marched at all. Twenty-eight zeros agree perfectly.
        Assert.True(
            vulkan.Any(value => Math.Abs(value) > 1e-6f),
            "Every coefficient came back nought, so the two agreed about an atmosphere nothing marched.");
    }

    [Fact]
    public void A_sky_that_has_not_moved_is_not_marched_again()
    {
        // The march is the expensive half and its answer is fixed for a mission. Left to run every frame
        // it is a hundred-step ray-march of a sky nobody has changed.
        SkyConditions conditions = Conditions();

        Assert.True(conditions.LooksLike(conditions));
        Assert.False(conditions.LooksLike(conditions with { ViewHeightKm = conditions.ViewHeightKm + 1f }));

        // And a difference far below what the eye could read does not count as a move.
        Assert.True(conditions.LooksLike(conditions with { SkyLightIntensity = conditions.SkyLightIntensity + 1e-6f }));
    }

    private static SkyConditions Conditions() => new(
        new Vector3f(0.3f, 0.9f, 0.31f),
        new Vector3f(10f, 10f, 10f),
        SkyConditions.ViewHeightFor(altitudeMetres: 2f),
        SkyLightIntensity: 1f);

    private static float[]? TryOnVulkan(out string? reason)
    {
        reason = null;

        try
        {
            using VulkanRhi vulkan = VulkanRhi.CreateOffscreen(16, 16, RhiTextureFormat.Rgba8UnormSrgb);
            return Run(vulkan.Device);
        }
        catch (Exception error)
        {
            reason = $"No Vulkan device here: {error.Message}";
            return null;
        }
    }

    private static float[]? TryOnWebGpu(out string? reason)
    {
        reason = null;

        try
        {
            using WebGpuRhiDevice device = WebGpuRhiDevice.Create();
            return Run(device);
        }
        catch (Exception error)
        {
            reason = $"No WebGPU device here: {error.Message}";
            return null;
        }
    }

    private static float[] Run(IRhiDevice device)
    {
        using PipelineSky sky = new(device, framesInFlight: 1);

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("sky"))
        {
            using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(Label: "sky")))
            {
                Assert.True(sky.Record(pass, frameInFlight: 0, Conditions()), "The first frame did not march.");

                // The same conditions again: the tables hold their answer rather than marching twice.
                Assert.False(sky.Record(pass, frameInFlight: 0, Conditions()), "A still sky marched again.");
            }

            sky.RecordReadback(encoder, frameInFlight: 0);
            device.Submit([encoder.Finish()]);
        }

        float[] harmonics = new float[PipelineSky.IrradianceFloats];

        // Polled rather than waited on, because that is the shape a browser leaves: the readback is a
        // future either way and the desktop's simply completes at once.
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (sky.TryReadIrradiance(0, harmonics))
            {
                return harmonics;
            }

            Thread.Sleep(10);
        }

        Assert.Fail("The harmonics never came back.");
        return harmonics;
    }
}

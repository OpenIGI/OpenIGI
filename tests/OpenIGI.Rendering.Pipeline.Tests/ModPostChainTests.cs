using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>A mod's pass over the picture runs as one dispatch over the frame, and one that breaks the contract is refused.</summary>
public sealed class ModPostChainTests
{
    [Fact]
    public void A_mods_module_runs_over_the_frame_and_hands_on_what_it_wrote()
    {
        using RecordingRhiDevice device = new();
        using RetiredResources retired = new(2);
        using ModPostChain chain = new(device, 2, retired, NullLogger.Instance);
        ShaderModuleId greyscale = ShaderCatalog.Register(ExternalShaderModule.Read(SampleShaders(), "Greyscale"));

        chain.Set([new PostPassModule("sample.Greyscale", greyscale)]);
        chain.SetValues("sample.Greyscale", [0.5f]);
        IRhiTexture source = Texture(device, "scene");
        IRhiTexture depth = Texture(device, "depth");
        IRhiTextureView? result;
        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("frame"))
        {
            using (IRhiComputePassEncoder pass = encoder.BeginComputePass())
            {
                // The depth the scene's, at half the picture's size, as after the scene is upscaled.
                result = chain.Record(pass, source.View, depth.View, 16, 16, (0.1f, 1000f), (8, 8), 0);
            }

            device.Submit([encoder.Finish()]);
        }

        Assert.Equal(["sample.Greyscale"], chain.Names);
        Assert.NotNull(result);
        Assert.NotSame(source.View, result);
        Assert.Equal(new RecordedRhiCommand.Dispatch(2, 2, 1), Assert.Single(device.CommandsOf<RecordedRhiCommand.Dispatch>()));

        // Its block: the size, the time, the sixteen values and the depth's size and scale, which the contract names.
        Assert.Contains(device.CommandsOf<RecordedRhiCommand.WriteBuffer>(), static write => write.SizeInBytes == 28 * sizeof(float));
    }

    [Fact]
    public void A_module_that_does_not_bind_the_contract_is_left_out_and_said_so()
    {
        using RecordingRhiDevice device = new();
        using RetiredResources retired = new(2);
        Recording log = new();
        using ModPostChain chain = new(device, 2, retired, log);

        chain.Set([new PostPassModule("broken", ShaderModuleId.UpscaleRejectionDilate)]);

        Assert.True(chain.IsEmpty);
        Assert.Contains(log.Lines, static line => line.Contains("broken", StringComparison.Ordinal) && line.Contains("uSource", StringComparison.Ordinal));
    }

    private static IRhiTexture Texture(RecordingRhiDevice device, string label) => device.CreateTexture(new RhiTextureDescriptor(
        16, 16, RhiTextureFormat.Rgba16Float, RhiTextureUsage.Sampled | RhiTextureUsage.Storage, Label: label));

    // The sample mod's compiled shaders, beside its sources in the repository.
    private static string SampleShaders()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "OpenIGI.Modding.SampleMod", "shaders", "Generated");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The sample mod's compiled shaders are not in the repository.");
    }

    private sealed class Recording : IOpenIgiLogger
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogLevel level, string category) => true;

        public void Log(in LogRecord record) => Lines.Add(record.Message);
    }
}

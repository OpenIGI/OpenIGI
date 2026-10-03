using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using Xunit;

namespace OpenIGI.Audio.Tests;

/// <summary>
/// The seam that keeps a host free of an audio backend: it holds an <see cref="IAudioDevice"/> and asks
/// a factory for it, so only an app composition root ever names a concrete backend.
/// </summary>
public sealed class AudioDeviceFactoryTests
{
    /// <summary>A host with no backend still gets a working device, so no call site tests for one.</summary>
    [Fact]
    public void Null_factory_opens_a_silent_device()
    {
        using IAudioDevice device = OpenThrough(NullAudioDeviceFactory.Instance);

        Assert.IsType<NullAudioDevice>(device);
    }

    /// <summary>
    /// Each open hands back a fresh device. The host disposes the device it was given before asking for
    /// the next one — voice count and output rate are fixed at open time — so returning the shared
    /// <see cref="NullAudioDevice.Instance"/> would leave the replacement already disposed.
    /// </summary>
    [Fact]
    public void Null_factory_does_not_hand_out_a_shared_device()
    {
        using IAudioDevice first = OpenThrough(NullAudioDeviceFactory.Instance);
        using IAudioDevice second = OpenThrough(NullAudioDeviceFactory.Instance);

        Assert.NotSame(first, second);
        Assert.NotSame(NullAudioDevice.Instance, first);
    }

    /// <summary>The options reach the device rather than being replaced by the factory's own defaults.</summary>
    [Fact]
    public void Open_passes_the_requested_options_through()
    {
        AudioDeviceOptions options = AudioDeviceOptions.Vanilla with { VoiceCount = 7 };
        RecordingAudioDeviceFactory factory = new();

        OpenThrough(factory, options).Dispose();

        Assert.Equal(7, factory.LastOptions.VoiceCount);
        Assert.False(factory.LastOptions.Spatial);
    }

    /// <summary>Opens through the interface, which is the dispatch these tests are about.</summary>
    private static IAudioDevice OpenThrough(IAudioDeviceFactory factory) =>
        OpenThrough(factory, AudioDeviceOptions.Default);

    private static IAudioDevice OpenThrough(IAudioDeviceFactory factory, AudioDeviceOptions options) =>
        factory.Open(options, NullLogger.Instance);

    private sealed class RecordingAudioDeviceFactory : IAudioDeviceFactory
    {
        public AudioDeviceOptions LastOptions { get; private set; }

        public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger)
        {
            LastOptions = options;
            return new NullAudioDevice();
        }
    }
}

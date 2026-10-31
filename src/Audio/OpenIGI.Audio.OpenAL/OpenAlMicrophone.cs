using Silk.NET.OpenAL;
using Silk.NET.OpenAL.Extensions.EXT;

namespace OpenIGI.Audio.OpenAL;

/// <summary>The default capture device through OpenAL's capture extension, mono 16-bit.</summary>
internal sealed unsafe class OpenAlMicrophone : IMicrophone
{
    private readonly Capture _capture;
    private readonly Device* _device;

    private OpenAlMicrophone(Capture capture, Device* device, int sampleRate)
    {
        _capture = capture;
        _device = device;
        SampleRate = sampleRate;
    }

    /// <inheritdoc/>
    public int SampleRate { get; }

    /// <summary>Opens the default microphone, or none where there is no capture or no device.</summary>
    /// <param name="alc">The context API.</param>
    /// <param name="sampleRate">The rate.</param>
    /// <returns>The microphone, or <see langword="null"/>.</returns>
    public static OpenAlMicrophone? Open(ALContext alc, int sampleRate)
    {
        if (!alc.TryGetExtension(null, out Capture capture))
        {
            return null;
        }

        // Half a second of room: read every frame, it never fills.
        Device* device = capture.CaptureOpenDevice(null, (uint)sampleRate, BufferFormat.Mono16, sampleRate / 2);
        if (device is null)
        {
            return null;
        }

        capture.CaptureStart(device);
        return new OpenAlMicrophone(capture, device, sampleRate);
    }

    /// <inheritdoc/>
    public int Read(Span<short> destination)
    {
        int available = Math.Min(_capture.GetAvailableSamples(_device), destination.Length);
        if (available <= 0)
        {
            return 0;
        }

        fixed (short* samples = destination)
        {
            _capture.CaptureSamples(_device, samples, available);
        }

        return available;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _capture.CaptureStop(_device);
        _capture.CaptureCloseDevice(_device);
    }
}

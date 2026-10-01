using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace OpenIGI.Audio.Web;

/// <summary>The page's audio graph, through the functions audio.js gives the runtime.</summary>
/// <remarks>
/// The page registers audio.js's imports under <see cref="ModuleName"/> before the game starts
/// (<c>runtime.setModuleImports</c>), so nothing here loads a module or waits for one. The PCM crosses
/// as a view of wasm memory that the page copies out of during the call, which is the one place a span
/// is handed over rather than numbers.
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed partial class WebAudioPage : IWebAudioPage
{
    /// <summary>The name the page registers audio.js's imports under.</summary>
    public const string ModuleName = "openigi-audio";

    /// <inheritdoc/>
    public int Open(int sampleRate, int voiceCount) => OpenPage(sampleRate, voiceCount);

    /// <inheritdoc/>
    public void Close() => ClosePage();

    /// <inheritdoc/>
    public void SetListener(
        double x, double y, double z,
        float forwardX, float forwardY, float forwardZ,
        float upX, float upY, float upZ) =>
        SetPageListener(x, y, z, forwardX, forwardY, forwardZ, upX, upY, upZ);

    /// <inheritdoc/>
    public void SetBusGain(int category, float gain) => SetPageBusGain(category, gain);

    /// <inheritdoc/>
    public int CreateBuffer(int sampleRate, int channels, int bitsPerSample, ReadOnlySpan<byte> pcm) =>
        CreatePageBuffer(sampleRate, channels, bitsPerSample, Writable(pcm));

    /// <inheritdoc/>
    public void DestroyBuffer(int buffer) => DestroyPageBuffer(buffer);

    /// <inheritdoc/>
    public void SetVoice(
        int voice, int category, float gain, float pitch,
        bool positional, float pan,
        double x, double y, double z, float minimumDistance, float maximumDistance) =>
        SetPageVoice(voice, category, gain, pitch, positional, pan, x, y, z, minimumDistance, maximumDistance);

    /// <inheritdoc/>
    public void Play(int voice, int buffer, bool looping) => PlayPageVoice(voice, buffer, looping);

    /// <inheritdoc/>
    public void StartStream(int voice, int sampleRate, int channels) => StartPageStream(voice, sampleRate, channels);

    /// <inheritdoc/>
    public void QueueStream(int voice, ReadOnlySpan<byte> pcm) => QueuePageStream(voice, Writable(pcm));

    /// <inheritdoc/>
    public int QueuedStreamChunks(int voice) => QueuedPageStreamChunks(voice);

    /// <inheritdoc/>
    public bool HasEnded(int voice) => PageVoiceHasEnded(voice);

    /// <inheritdoc/>
    public void StopVoice(int voice) => StopPageVoice(voice);

    /// <summary>Hands a read-only span over as the writable view the interop marshals.</summary>
    /// <remarks>
    /// The runtime marshals only <see cref="Span{T}"/> as a memory view, and the page reads the bytes
    /// during the call and never writes them, so the span's read-only promise is kept; this only
    /// avoids copying every buffer once more on the way in.
    /// </remarks>
    private static Span<byte> Writable(ReadOnlySpan<byte> pcm) =>
        MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(pcm), pcm.Length);

    [JSImport("open", ModuleName)]
    private static partial int OpenPage(int sampleRate, int voiceCount);

    [JSImport("close", ModuleName)]
    private static partial void ClosePage();

    [JSImport("setListener", ModuleName)]
    private static partial void SetPageListener(
        double x, double y, double z,
        float forwardX, float forwardY, float forwardZ,
        float upX, float upY, float upZ);

    [JSImport("setBusGain", ModuleName)]
    private static partial void SetPageBusGain(int category, float gain);

    [JSImport("createBuffer", ModuleName)]
    private static partial int CreatePageBuffer(
        int sampleRate, int channels, int bitsPerSample, [JSMarshalAs<JSType.MemoryView>] Span<byte> pcm);

    [JSImport("destroyBuffer", ModuleName)]
    private static partial void DestroyPageBuffer(int buffer);

    [JSImport("setVoice", ModuleName)]
    private static partial void SetPageVoice(
        int voice, int category, float gain, float pitch,
        bool positional, float pan,
        double x, double y, double z, float minimumDistance, float maximumDistance);

    [JSImport("play", ModuleName)]
    private static partial void PlayPageVoice(int voice, int buffer, bool looping);

    [JSImport("startStream", ModuleName)]
    private static partial void StartPageStream(int voice, int sampleRate, int channels);

    [JSImport("queueStream", ModuleName)]
    private static partial void QueuePageStream(int voice, [JSMarshalAs<JSType.MemoryView>] Span<byte> pcm);

    [JSImport("queuedStreamChunks", ModuleName)]
    private static partial int QueuedPageStreamChunks(int voice);

    [JSImport("hasEnded", ModuleName)]
    private static partial bool PageVoiceHasEnded(int voice);

    [JSImport("stop", ModuleName)]
    private static partial void StopPageVoice(int voice);
}

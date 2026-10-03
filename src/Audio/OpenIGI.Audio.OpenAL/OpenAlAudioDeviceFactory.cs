using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Audio.OpenAL;

/// <summary>Opens <see cref="OpenAlAudioDevice"/> instances.</summary>
/// <remarks>
/// The desktop backend's half of the <see cref="IAudioDeviceFactory"/> seam. It adds nothing to the
/// device's own construction — it exists so that the host can name the interface instead of the
/// backend, and so the composition root stays the only place that decides which backend runs.
/// </remarks>
public sealed class OpenAlAudioDeviceFactory : IAudioDeviceFactory
{
    /// <summary>The shared instance.</summary>
    public static readonly OpenAlAudioDeviceFactory Instance = new();

    /// <inheritdoc/>
    /// <remarks><see cref="OpenAlAudioDevice"/> degrades to silence on its own when it cannot open.</remarks>
    public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => new OpenAlAudioDevice(options, logger);
}

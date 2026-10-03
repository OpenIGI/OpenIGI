using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Audio;

/// <summary>A factory that opens silent devices.</summary>
/// <remarks>
/// The default a host is given when no backend is wired in — headless tests, continuous integration,
/// and the <c>-nosound</c> path. Each call returns a fresh <see cref="NullAudioDevice"/> rather than
/// the shared instance, so a host that disposes the device it was handed does not silence a later one.
/// </remarks>
public sealed class NullAudioDeviceFactory : IAudioDeviceFactory
{
    /// <summary>The shared instance.</summary>
    public static readonly NullAudioDeviceFactory Instance = new();

    /// <inheritdoc/>
    public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => new NullAudioDevice();
}

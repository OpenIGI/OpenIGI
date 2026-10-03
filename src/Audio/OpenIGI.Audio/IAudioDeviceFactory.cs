using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Audio;

/// <summary>Opens the audio backend a host runs against.</summary>
/// <remarks>
/// <para>
/// A factory rather than a device, because the device is re-opened whenever the voice count or the
/// output rate changes and both are fixed at open time: the host disposes the previous device and asks
/// for a new one with the current options.
/// </para>
/// <para>
/// This is the seam that keeps the host free of a backend. The host holds an
/// <see cref="IAudioDevice"/> and asks a factory for it; only the app composition root names a concrete
/// backend. A host with none is given <see cref="NullAudioDeviceFactory"/>, which is what a headless
/// test and a machine with no sound hardware both want — and is why no caller has to handle a device
/// that would not open.
/// </para>
/// </remarks>
public interface IAudioDeviceFactory
{
    /// <summary>Opens a device with the given options.</summary>
    /// <param name="options">The voice count, output rate and spatialisation to open with.</param>
    /// <param name="logger">Where the backend reports what it opened, or why it could not.</param>
    /// <returns>The device, which the caller disposes.</returns>
    /// <remarks>
    /// A backend that cannot open returns a silent device rather than throwing, because the game runs
    /// either way and a missing sound card is not a reason to refuse to start.
    /// </remarks>
    IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger);
}

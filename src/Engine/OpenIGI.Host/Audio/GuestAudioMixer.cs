using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Host.Services;

namespace OpenIGI.Host.Audio;

/// <summary>Another game's sounds through the host's own device: a sound system and bank of their own over that game's files.</summary>
/// <param name="output">The host's audio, whose device they play on.</param>
/// <param name="logger">Where a bank that cannot be read is reported.</param>
internal sealed class GuestAudioMixer(AudioOutput output, IOpenIgiLogger logger) : ITitleAudioMixer
{
    /// <inheritdoc/>
    public ITitleAudio? Open(VirtualFileSystem files, ISoundDefinitionLookup definitions, IReadOnlyList<string> bankDirectories)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(bankDirectories);

        // The host's device, a silent one where it has no sound: the listener is the device's, so both mixes hear from one ear.
        SoundBank bank = new(files, logger);
        // A sound the guest's banks lack is borrowed from the host's, which defines it under the same name where the two games
        // share their clips.
        TitleAudio audio = new(new SoundSystem(output.Device, bank, files, logger), bank, () => output.Sounds);
        audio.UseDefinitions(definitions, bankDirectories);
        return audio;
    }
}

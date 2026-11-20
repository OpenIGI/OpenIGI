using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Formats.Sound;
using OpenIGI.Game.Multiplayer.Modes;

namespace OpenIGI.Host.Audio;

/// <summary>The bomb's sounds, from the kit built into the game (<see cref="BombKit"/>), under the names they play by.</summary>
public static class BombSounds
{
    /// <summary>The name the plant plays by.</summary>
    public const string Plant = "bomb." + BombKit.PlantSound;

    /// <summary>The name the planted bomb's tick plays by.</summary>
    public const string Tick = "bomb." + BombKit.TickSound;

    /// <summary>The name the warning beep near the end plays by.</summary>
    public const string Warning = "bomb." + BombKit.LastTickSound;

    /// <summary>The name the defuse plays by.</summary>
    public const string Defuse = "bomb." + BombKit.DefuseSound;

    /// <summary>The name the blast plays by, near.</summary>
    public const string Explosion = "bomb." + BombKit.ExplosionSound;

    /// <summary>The name the blast plays by, far off.</summary>
    public const string DistantExplosion = "bomb." + BombKit.DistantExplosionSound;

    /// <summary>The name the blast's deep thump plays by, far off and under the rest (<see cref="BombSoundDesign.FarThump"/>).</summary>
    public const string FarThump = "bomb.far_thump";

    /// <summary>The name the front's rush and strike plays by, at the player it reaches (<see cref="BombSoundDesign.FrontRush"/>).</summary>
    public const string FrontRush = "bomb.front_rush";

    /// <summary>Decodes every sound, and layers the bomb's own two out of its explosions, for registering.</summary>
    /// <param name="logger">Where a sound that does not decode is told.</param>
    /// <returns>Each sound's name, format and PCM.</returns>
    public static IEnumerable<(string Name, AudioFormat Format, byte[] Pcm)> All(IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        (string Name, AudioFormat Format, byte[] Pcm)? near = null;
        (string Name, AudioFormat Format, byte[] Pcm)? distant = null;
        foreach ((string Name, AudioFormat Format, byte[] Pcm) sound in Kit(logger))
        {
            near = sound.Name == Explosion ? sound : near;
            distant = sound.Name == DistantExplosion ? sound : distant;
            yield return sound;
        }

        if (distant is { } far)
        {
            byte[] thump = BombSoundDesign.FarThump(far.Format, far.Pcm);
            if (thump.Length > 0)
            {
                yield return (FarThump, far.Format, thump);
            }

            byte[] rush = near is { } blast && blast.Format == far.Format
                ? BombSoundDesign.FrontRush(far.Format, far.Pcm, blast.Pcm, (float)BombWave.CueLeadSeconds)
                : [];
            if (rush.Length > 0)
            {
                yield return (FrontRush, far.Format, rush);
            }
        }
    }

    // The kit's own sounds, decoded.
    private static IEnumerable<(string Name, AudioFormat Format, byte[] Pcm)> Kit(IOpenIgiLogger logger)
    {
        foreach (string sound in (string[])[BombKit.PlantSound, BombKit.TickSound, BombKit.LastTickSound, BombKit.DefuseSound, BombKit.ExplosionSound, BombKit.DistantExplosionSound])
        {
            IlsfFile? read = null;
            try
            {
                read = BombKit.Sound(sound) is { } bytes ? IlsfFile.Read(bytes) : null;
            }
            catch (IlsfFormatException error)
            {
                logger.Warning("Audio", $"The bomb's sound '{sound}' did not decode: {error.Message}");
            }

            if (read is not null)
            {
                yield return ("bomb." + sound, new AudioFormat(read.SampleRate, read.Channels, read.BitsPerSample), read.DecodePcm().ToArray());
            }
        }
    }
}

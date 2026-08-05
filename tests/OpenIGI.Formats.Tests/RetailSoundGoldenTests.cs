using System.Globalization;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sound;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Reads every sound a real installation ships — the loose <c>.wav</c> files and the bodies inside the
/// sound archives — which is the only way to know the reader survives the shapes the shipped data uses.
/// </summary>
/// <remarks>Skipped when <c>IGI_GAME_PATH</c> is unset.</remarks>
public sealed class RetailSoundGoldenTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="RetailSoundGoldenTests"/> class.</summary>
    /// <param name="output">The xunit output sink.</param>
    public RetailSoundGoldenTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void EveryShippedSoundParsesAndDecodes()
    {
        List<string> failures = [];
        Dictionary<IlsfFormat, int> formats = [];
        Dictionary<int, int> rates = [];
        Dictionary<int, int> channels = [];
        Dictionary<int, int> widths = [];
        int decoded = 0;
        long frames = 0;

        foreach (RetailSound sound in EnumerateSounds())
        {
            try
            {
                IlsfFile file = IlsfFile.Read(sound.Data);
                Count(formats, file.Format);
                Count(rates, file.SampleRate);
                Count(channels, file.Channels);
                Count(widths, file.BitsPerSample);
                frames += file.FrameCount;

                // Decoding is what actually exercises the ADPCM tables; a wrong table produces the
                // right byte count, so the count check below is necessary but not sufficient.
                ReadOnlyMemory<byte> pcm = file.DecodePcm();
                Assert.Equal(file.DecodedByteCount, pcm.Length);
                decoded++;
            }
            catch (Exception error) when (error is IlsfFormatException or IlffFormatException or IOException)
            {
                failures.Add($"{sound.Origin}: {error.Message}");
            }
        }

        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{decoded} sounds, {frames} frames."));
        _output.WriteLine("formats: " + Describe(formats));
        _output.WriteLine("rates: " + Describe(rates));
        _output.WriteLine("channels: " + Describe(channels));
        _output.WriteLine("widths: " + Describe(widths));

        Assert.Empty(failures);
        Assert.True(decoded > 0, "No sounds were found in the installation.");

        // All four storage codes are exercised by the shipped data, which is what makes the two-bit
        // reading of the format field testable rather than merely plausible.
        Assert.Equal(4, formats.Count);

        // Every retail sound is 16-bit, but the rate is *not* uniform: the census finds 11025 and 44100
        // beside the dominant 22050. The original's primary buffer runs at 22050 (0x4952F0) and lets
        // DirectSound resample the rest, so a port must not assume one rate either.
        Assert.Equal([16], widths.Keys);
        Assert.True(rates.Count > 1, "Expected more than one sample rate among the shipped sounds.");
    }

    [RequiresGameDataFact]
    public void TheStreamedFormsAreTheLooseFilesAndTheResidentFormsAreTheArchivedOnes()
    {
        // This is the check that pins the two-bit reading of the format code. The original keeps only a
        // file name for the streamed codes (0x495F70, record +40), so a streamed sound must exist as a
        // file the virtual file system can reopen — it cannot live inside an archive.
        List<string> misplaced = [];

        foreach (RetailSound sound in EnumerateSounds())
        {
            IlsfFile file = IlsfFile.Read(sound.Data);
            if (file.IsStreamed && sound.IsArchived)
            {
                misplaced.Add($"{sound.Origin}: streamed {file.Format} inside an archive.");
            }
        }

        Assert.Empty(misplaced);
    }

    [RequiresGameDataFact]
    public void TheMissionMusicIsAStreamedAdpcmTrack()
    {
        string path = GameData.Resolve(Path.Combine("missions", "location0", "level1", "sounds", "game_music.wav"));
        Assert.True(File.Exists(path), $"Expected '{path}' in the installation.");

        IlsfFile music = IlsfFile.Read(File.ReadAllBytes(path));

        // Music is retail data streamed off disk, not CD audio: the original's mciSendCommandA path is
        // not what plays a mission's score.
        Assert.Equal(IlsfFormat.StreamedAdpcm, music.Format);
        Assert.Equal(2, music.Channels);
        Assert.Equal(IlsfHeader.RetailSampleRate, music.SampleRate);
        Assert.True(music.Duration > TimeSpan.FromMinutes(2), $"Expected a full track, got {music.Duration}.");

        // Four-bit stereo is exactly one byte per frame, which is what makes the count field a frame
        // count rather than a byte count.
        Assert.Equal(music.FrameCount, music.Payload.Length);
    }

    [RequiresGameDataFact]
    public void AnAmbienceBedIsStreamedPcmWhosePayloadIsExactlyItsFrames()
    {
        string path = GameData.Resolve(Path.Combine("missions", "location0", "level1", "sounds", "m1_ambience.wav"));
        Assert.True(File.Exists(path), $"Expected '{path}' in the installation.");

        IlsfFile ambience = IlsfFile.Read(File.ReadAllBytes(path));

        Assert.Equal(IlsfFormat.StreamedPcm, ambience.Format);
        Assert.Equal(ambience.FrameCount * ambience.BlockAlign, ambience.Payload.Length);
    }

    [RequiresGameDataFact]
    public void TheSoundArchivesHoldTheEffectBank()
    {
        foreach (string relative in (string[])
                 [
                     Path.Combine("common", "sounds", "sounds.res"),
                     Path.Combine("menusystem", "sound", "sounds.res"),
                 ])
        {
            string path = GameData.Resolve(relative);
            Assert.True(File.Exists(path), $"Expected '{path}' in the installation.");

            IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));
            int sounds = archive.Entries.Count(entry => IlsfFile.IsIlsf(entry.Data.Span));

            Assert.True(sounds > 0, $"'{relative}' holds no ILSF sounds.");
            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{relative}: {sounds} of {archive.Entries.Count} entries are sounds."));
        }
    }

    private static IEnumerable<RetailSound> EnumerateSounds()
    {
        foreach (string path in GameData.EnumerateFiles("*.wav"))
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (IlsfFile.IsIlsf(bytes))
            {
                yield return new RetailSound(path, bytes, IsArchived: false);
            }
        }

        foreach (string path in GameData.EnumerateFiles("*.res"))
        {
            IresArchive archive;
            try
            {
                archive = IresArchive.Read(File.ReadAllBytes(path));
            }
            catch (IlffFormatException)
            {
                continue;
            }

            foreach (IresEntry entry in archive.Entries)
            {
                if (IlsfFile.IsIlsf(entry.Data.Span))
                {
                    yield return new RetailSound($"{path}!{entry.Name}", entry.Data, IsArchived: true);
                }
            }
        }
    }

    private static void Count<TKey>(Dictionary<TKey, int> tally, TKey key)
        where TKey : notnull =>
        tally[key] = tally.TryGetValue(key, out int existing) ? existing + 1 : 1;

    private static string Describe<TKey>(Dictionary<TKey, int> tally)
        where TKey : notnull =>
        string.Join(", ", tally.OrderBy(pair => pair.Key?.ToString(), StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));

    private readonly record struct RetailSound(string Origin, ReadOnlyMemory<byte> Data, bool IsArchived);
}

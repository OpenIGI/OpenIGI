using System.Globalization;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sound;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Reads I.G.I. 2's music out of a real installation — the one retail sound in either game that is not
/// <c>ILSF</c>.
/// </summary>
/// <remarks>
/// <para>
/// The sequel's menu theme lives inside <c>menusystem/sound/sounds.res</c> as
/// <c>menu_music.mp3</c>, and its mission scores are loose <c>*_music.mp3</c> files beside each level.
/// Retail decodes them through Miles, whose <c>mss32.dll</c> ships in the installation's <c>pc</c>
/// directory; the port decodes them here.
/// </para>
/// <para>Skipped when <c>IGI2_GAME_PATH</c> is unset.</para>
/// </remarks>
public sealed class RetailMpegAudioTests
{
    private const string MenuArchive = "menusystem/sound/sounds.res";

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="RetailMpegAudioTests"/> class.</summary>
    /// <param name="output">The xunit output sink.</param>
    public RetailMpegAudioTests(ITestOutputHelper output) => _output = output;

    [RequiresTitleDataFact("igi2")]
    public void TheMenuThemeIsAnMpegStreamWhereTheFirstGamesIsIlsf()
    {
        ReadOnlyMemory<byte> track = MenuTheme();

        // The fact the whole fix turns on: the two games file the same key under different formats, so
        // nothing may assume that a sound named menu_music is ILSF.
        Assert.False(IlsfHeader.IsIlsf(track.Span));
        Assert.True(MpegAudioHeader.IsMpegAudio(track.Span));

        MpegAudioHeader header = MpegAudioHeader.Read(track.Span);
        Assert.Equal(MpegAudioVersion.Version1, header.Version);
        Assert.Equal(3, header.Layer);
        Assert.Equal(44100, header.SampleRate);
        Assert.Equal(2, header.Channels);
        Assert.Equal(320_000, header.BitRate);

        // Three and a half minutes: long enough that expanding it would cost about thirty-six megabytes,
        // which is why it is decoded as it plays.
        TimeSpan duration = header.EstimateDuration(track.Length);
        Assert.InRange(duration, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4));

        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{header} — {track.Length} bytes, about {duration:mm\\:ss}"));
    }

    [RequiresTitleDataFact("igi2")]
    public void TheMenuThemeDecodesToAudibleSamples()
    {
        using MpegAudioStreamReader reader = Open(MenuTheme());

        Assert.Equal(44100, reader.SampleRate);
        Assert.Equal(2, reader.Channels);
        Assert.True(reader.CanRestart);

        short[] buffer = new short[4096 * 2];
        long frames = 0;
        int peak = 0;

        // About ten seconds, which is more than enough to leave the encoder's silent lead-in behind.
        while (frames < 44100 * 10)
        {
            int got = reader.ReadFrames(buffer);
            if (got == 0)
            {
                break;
            }

            frames += got;
            for (int i = 0; i < got * reader.Channels; i++)
            {
                peak = Math.Max(peak, Math.Abs((int)buffer[i]));
            }
        }

        Assert.Equal(frames, reader.FramesRead);
        Assert.True(frames >= 44100 * 10, $"Only {frames} frame(s) decoded.");

        // A decoder that produced the right sample count and silence would pass a length check, so the
        // assertion that matters is that something is actually there. A quarter of full scale is far
        // above anything a broken filterbank would produce and far below the track's own level.
        Assert.True(peak > 8000, $"The theme decoded to near-silence; peak was {peak}.");

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{frames} frames, peak {peak}"));
    }

    [RequiresTitleDataFact("igi2")]
    public void RestartingRewindsToTheFirstFrameSoTheThemeCanLoop()
    {
        // The menu theme loops, so this is not a nicety: a source that cannot rewind plays once and
        // leaves the menu silent from then on.
        using MpegAudioStreamReader reader = Open(MenuTheme());

        short[] first = new short[2048 * 2];
        short[] again = new short[2048 * 2];

        Assert.True(reader.ReadFrames(first) > 0);
        for (int i = 0; i < 20; i++)
        {
            reader.ReadFrames(again);
        }

        reader.Restart();
        Assert.Equal(0, reader.FramesRead);
        Assert.True(reader.ReadFrames(again) > 0);
        Assert.Equal(first, again);
    }

    [RequiresTitleDataFact("igi2")]
    public void ABufferSmallerThanADecodedGranuleIsStillFilled()
    {
        // The device fills a 3072-frame buffer by reading until it is full, and treats a zero as the end
        // of the sound -- so a reader that answered a short tail with zero would have a looping track
        // rewind, read zero again and rewind again. That is a hang, not a glitch, and it is what this
        // pins: every read is either full or the stream is genuinely over.
        using MpegAudioStreamReader reader = Open(MenuTheme());

        short[] buffer = new short[3072 * 2];
        int filled = 0;

        for (int i = 0; i < 24; i++)
        {
            int got = reader.ReadFrames(buffer);
            Assert.Equal(3072, got);
            filled++;
        }

        Assert.Equal(24, filled);

        // And down to a single frame, which is what a decoder that only answers whole granules cannot
        // do without keeping what it decoded past the end of the ask.
        short[] one = new short[2];
        for (int i = 0; i < 4096; i++)
        {
            Assert.Equal(1, reader.ReadFrames(one));
        }

        Assert.Equal((24 * 3072) + 4096, reader.FramesRead);
    }

    [RequiresTitleDataFact("igi2")]
    public void EveryShippedMissionScoreParsesAndDecodes()
    {
        List<string> failures = [];
        int tracks = 0;

        foreach (string path in Directory.EnumerateFiles(
            GameDataSource.Igi2.RequireRoot(),
            "*.mp3",
            SearchOption.AllDirectories))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Assert.True(MpegAudioHeader.IsMpegAudio(bytes), $"{path} is not recognised as MPEG audio.");

                using MpegAudioStreamReader reader = Open(bytes);
                short[] buffer = new short[4096 * 2];

                // A frame at the front and one about a second in: enough to catch a stream whose header
                // the probe accepted but whose frames the decoder cannot follow.
                Assert.True(reader.ReadFrames(buffer) > 0, $"{path} decoded nothing.");
                for (int i = 0; i < 10; i++)
                {
                    reader.ReadFrames(buffer);
                }

                tracks++;
            }
            catch (Exception error) when (error is MpegAudioFormatException or IOException)
            {
                failures.Add($"{path}: {error.Message}");
            }
        }

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{tracks} loose track(s)."));
        Assert.Empty(failures);
        Assert.True(tracks > 0, "I.G.I. 2 ships a score per level; none were found.");
    }

    [Fact]
    public void TheProbeRejectsWhatIsNotMpegAudio()
    {
        // The probe decides which decoder an archive member gets, so a loose sync word must not be
        // enough. Eleven set bits followed by a reserved layer, a free-format bitrate or a reserved
        // sample rate is not a frame header.
        Assert.False(MpegAudioHeader.IsMpegAudio([]));
        Assert.False(MpegAudioHeader.IsMpegAudio([0xFF, 0xFB]));
        Assert.False(MpegAudioHeader.IsMpegAudio("ILSF"u8));
        Assert.False(MpegAudioHeader.IsMpegAudio([0xFF, 0xF9, 0xE0, 0x04]), "Reserved layer.");
        Assert.False(MpegAudioHeader.IsMpegAudio([0xFF, 0xFB, 0x00, 0x04]), "Free-format bitrate.");
        Assert.False(MpegAudioHeader.IsMpegAudio([0xFF, 0xFB, 0xF0, 0x04]), "Invalid bitrate index.");
        Assert.False(MpegAudioHeader.IsMpegAudio([0xFF, 0xFB, 0xEC, 0x04]), "Reserved sample rate.");
        Assert.False(MpegAudioHeader.IsMpegAudio([0xFF, 0xEB, 0xE0, 0x04]), "Reserved version.");

        // And the shape the sequel actually ships: MPEG-1 Layer III, 320 kbps, 44.1 kHz, stereo.
        Assert.True(MpegAudioHeader.IsMpegAudio([0xFF, 0xFB, 0xE0, 0x04]));
    }

    [Fact]
    public void AMonoMpeg2StreamIsReadAsOneChannelAtItsOwnRate()
    {
        // Neither game ships one, but the header can say it, and a decoder that silently read it as
        // stereo at twice the rate would produce a sound at half speed rather than an error.
        // 0xFF 0xF3: MPEG-2, Layer III. 0x40: bitrate index 4 (32 kbit/s), rate index 0 (22050 Hz).
        // 0xC0: channel mode 11, mono.
        MpegAudioHeader header = MpegAudioHeader.Read([0xFF, 0xF3, 0x40, 0xC0]);

        Assert.Equal(MpegAudioVersion.Version2, header.Version);
        Assert.Equal(3, header.Layer);
        Assert.Equal(22050, header.SampleRate);
        Assert.Equal(1, header.Channels);
        Assert.Equal(32_000, header.BitRate);
        Assert.Equal(2, header.BlockAlign);
    }

    private static MpegAudioStreamReader Open(ReadOnlyMemory<byte> bytes) =>
        MpegAudioStreamReader.Open(new MemoryStream(bytes.ToArray(), writable: false));

    private static ReadOnlyMemory<byte> MenuTheme()
    {
        string path = Path.Combine(GameDataSource.Igi2.RequireRoot(), MenuArchive.Replace('/', Path.DirectorySeparatorChar));
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));

        foreach (IresEntry entry in archive.Entries)
        {
            if (entry.Name.EndsWith("menu_music.mp3", StringComparison.OrdinalIgnoreCase))
            {
                return entry.Data;
            }
        }

        throw new InvalidOperationException($"'{MenuArchive}' holds no menu_music.mp3.");
    }
}

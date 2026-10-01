namespace OpenIGI.Formats.Sound;

/// <summary>Which MPEG audio standard a stream follows.</summary>
/// <remarks>
/// The values are row indices into <see cref="MpegAudioHeader"/>'s sample-rate table rather than the
/// version field's own encoding, which is why <see cref="Version25"/> comes first: the table is ordered
/// by rate, eight kilohertz upwards, and reading it wants one index rather than a switch. Both games
/// ship <see cref="Version1"/> exclusively; the other two are here because the header can say them and
/// a decoder that silently mis-read one would be worse than one that names it.
/// </remarks>
public enum MpegAudioVersion
{
    /// <summary>MPEG-2.5, the unofficial extension to 8, 11.025 and 12 kHz.</summary>
    Version25 = 0,

    /// <summary>MPEG-1, ISO/IEC 11172-3. Both games' music.</summary>
    Version1 = 1,

    /// <summary>MPEG-2, ISO/IEC 13818-3: MPEG-1's rates halved.</summary>
    Version2 = 2,
}

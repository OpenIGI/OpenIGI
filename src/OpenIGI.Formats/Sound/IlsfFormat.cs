namespace OpenIGI.Formats.Sound;

/// <summary>How an <c>ILSF</c> sound stores its samples, and whether the original kept it resident.</summary>
/// <remarks>
/// <para>
/// The four codes are the arms of the switch in the original loader <c>0x495F70</c>, and they form a
/// two-bit pair: bit 0 selects resident over streamed, bit 1 selects ADPCM over raw PCM. The loader
/// treats them as follows — a streamed sound keeps only its file name (record <c>+40</c>) and never
/// allocates or fills a sound buffer, a resident PCM sound points straight at the payload in place, and
/// a resident ADPCM sound allocates the decoded size and expands through <c>0x496280</c> before the
/// buffer is filled and the temporary is freed.
/// </para>
/// <para>
/// The distinction is a runtime policy rather than a difference in the bytes: both streamed codes
/// decode exactly like their resident counterparts. All four codes ship, and a census of a retail
/// installation shows the split is exactly where the reading predicts — no streamed sound lives inside
/// an archive, because a streamed sound is only a file name and must stay reopenable.
/// </para>
/// </remarks>
public enum IlsfFormat
{
    /// <summary>Raw PCM, streamed from its file. The retail ambience beds use this.</summary>
    StreamedPcm = 0,

    /// <summary>Raw PCM, held in memory. The ordinary sound-effect form inside <c>sounds.res</c>.</summary>
    ResidentPcm = 1,

    /// <summary>IMA ADPCM, streamed from its file. The retail music tracks use this.</summary>
    StreamedAdpcm = 2,

    /// <summary>IMA ADPCM, expanded to PCM at load time and held in memory.</summary>
    ResidentAdpcm = 3,
}

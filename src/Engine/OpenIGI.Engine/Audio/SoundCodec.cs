namespace OpenIGI.Engine.Audio;

/// <summary>How a banked sound stores its samples, and therefore which reader decodes it.</summary>
/// <remarks>
/// Decided from the bytes rather than from the file name, because the name is not reliable evidence:
/// I.G.I. 2's menu theme is filed in its archive as <c>menu_music.mp3</c> while the first game's is
/// <c>menu_music.wav</c>, and both answer to the same key once the extension is stripped. The original
/// has no equivalent distinction — every sound it loads is ILSF — so this is the port's own, and the
/// probe is content-based so a replacement pack cannot mislead it.
/// </remarks>
public enum SoundCodec
{
    /// <summary>An <c>ILSF</c> sound: raw PCM or IMA ADPCM. Everything in I.G.I. 1 and almost all of I.G.I. 2.</summary>
    Ilsf = 0,

    /// <summary>An MPEG audio stream. I.G.I. 2's menu theme and its mission scores.</summary>
    Mpeg = 1,
}

namespace OpenIGI.Audio;

/// <summary>A named set of audio settings, the audio counterpart of a graphics preset.</summary>
public enum AudioProfile
{
    /// <summary>The settings do not correspond to any named profile.</summary>
    Custom,

    /// <summary>
    /// The original's behaviour: 32 voices, a 22050 Hz mix, and the flat hand-computed pan of
    /// <c>0x496E60</c> instead of real positioning.
    /// </summary>
    Vanilla,

    /// <summary>Positioned sound, a larger voice pool and a modern output rate.</summary>
    Enhanced,
}

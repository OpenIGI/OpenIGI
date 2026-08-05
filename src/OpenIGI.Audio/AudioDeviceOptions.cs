namespace OpenIGI.Audio;

/// <summary>How a backend should open itself.</summary>
/// <remarks>
/// Built in the composition root from <see cref="AudioCVars"/>, so the backend never reads the console
/// registry itself and can be constructed directly in a test.
/// </remarks>
public readonly record struct AudioDeviceOptions
{
    /// <summary>Gets the number of voices to allocate.</summary>
    public int VoiceCount { get; init; }

    /// <summary>Gets the output sample rate to request, or zero to accept whatever the backend prefers.</summary>
    public int SampleRate { get; init; }

    /// <summary>Gets a value indicating whether sounds are positioned in three dimensions.</summary>
    /// <remarks>
    /// <see langword="false"/> selects the original's behaviour: a linear distance fade and a lateral
    /// pan computed by <see cref="VanillaSpatialiser"/> rather than by the backend.
    /// </remarks>
    public bool Spatial { get; init; }

    /// <summary>Gets a value indicating whether the output channels are swapped.</summary>
    public bool ReverseStereo { get; init; }

    /// <summary>Gets the options the original would imply: 32 voices, 22050 Hz and no real 3D.</summary>
    public static AudioDeviceOptions Vanilla => new()
    {
        VoiceCount = 32,
        SampleRate = 22050,
        Spatial = false,
        ReverseStereo = false,
    };

    /// <summary>Gets the defaults for a normal launch.</summary>
    public static AudioDeviceOptions Default => new()
    {
        VoiceCount = 64,
        SampleRate = 48000,
        Spatial = true,
        ReverseStereo = false,
    };

    /// <summary>Reads the options currently held by the console variables.</summary>
    /// <returns>The options.</returns>
    public static AudioDeviceOptions FromCVars() => new()
    {
        VoiceCount = AudioCVars.Voices.Value,
        SampleRate = AudioCVars.SampleRate.Value,
        Spatial = AudioCVars.Spatial.Value,
        ReverseStereo = AudioCVars.ReverseStereo.Value,
    };
}

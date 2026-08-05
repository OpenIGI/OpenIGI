using OpenIGI.Core.Numerics;

namespace OpenIGI.Audio;

/// <summary>
/// The original's positional audio, exactly as it computed it: a linear distance rolloff and a pan,
/// folded into the gain and balance of an otherwise flat stereo voice.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from the voice update <c>0x496E60</c>. <b>The original is not DirectSound3D.</b> Its
/// primary buffer is created with flags <c>0x9</c> and its sample buffers with <c>0x100E8</c> — neither
/// carries <c>DSBCAPS_CTRL3D</c>. Every sound in the game is a mono or stereo buffer whose apparent
/// position is hand-computed into volume, pan and playback frequency, three times a tick. This class is
/// that computation, and it is what the vanilla profile uses instead of letting OpenAL position
/// anything.
/// </para>
/// <para>
/// Two details of it are worth stating because they are not what a modern engine would do:
/// </para>
/// <list type="number">
/// <item><description>
/// The distance rolloff is <b>linear</b> between the two distances, not inverse-square and not
/// inverse-clamped. A source is at full gain inside the minimum distance, fades on a straight line to
/// silence at the maximum, and is cut entirely beyond it.
/// </description></item>
/// <item><description>
/// The pan is <c>localX / (minimum + maximum)</c> — the lateral offset over the <em>sum</em> of the two
/// distances. Distance cancels out of it entirely, so a source ten metres to the left pans exactly as
/// hard whether it is ten metres away or fifty. It is not a direction; it is a lateral offset measured
/// against a fixed scale.
/// </description></item>
/// </list>
/// </remarks>
public static class VanillaSpatialiser
{
    /// <summary>The gain and pan the original would give a positional voice.</summary>
    /// <param name="parameters">The voice, whose <see cref="VoiceParameters.Gain"/> is the starting gain.</param>
    /// <param name="listener">Where the listener is and which way it faces.</param>
    /// <returns>The gain after distance, and the pan, both already clamped.</returns>
    public static (float Gain, float Pan) Spatialise(in VoiceParameters parameters, in AudioListenerState listener)
    {
        if (!parameters.IsPositional)
        {
            return (Math.Clamp(parameters.Gain, 0f, 1f), Math.Clamp(parameters.Pan, -1f, 1f));
        }

        Vector3d delta = parameters.Position - listener.Position;
        double distance = delta.Length;

        return (DistanceGain(parameters.Gain, distance, parameters.MinimumDistance, parameters.MaximumDistance),
                Pan(delta, distance, parameters.MinimumDistance, parameters.MaximumDistance, listener));
    }

    /// <summary>Applies the original's linear distance rolloff to a gain.</summary>
    /// <param name="gain">The voice's own gain.</param>
    /// <param name="distance">The distance from listener to source, in metres.</param>
    /// <param name="minimumDistance">The distance within which gain is untouched.</param>
    /// <param name="maximumDistance">The distance beyond which the voice is silent.</param>
    /// <returns>The attenuated gain, clamped to zero-to-one.</returns>
    public static float DistanceGain(float gain, double distance, float minimumDistance, float maximumDistance)
    {
        if (distance > maximumDistance)
        {
            return 0f;
        }

        if (distance < minimumDistance)
        {
            // Inside the minimum distance the original leaves the gain completely alone -- it does not
            // even clamp it, which is why the clamp below is only on the faded path.
            return gain;
        }

        double span = maximumDistance - minimumDistance;
        if (span <= 0.0)
        {
            // Degenerate range. The original would divide by zero here and produce an infinity that
            // fails its own >= 0 test, landing on silence; this reaches the same answer without the
            // trap representation.
            return 0f;
        }

        float faded = (float)(1.0 - ((distance - minimumDistance) / span)) * gain;
        return faded < 0f ? 0f : Math.Min(faded, 1f);
    }

    /// <summary>Computes the original's pan for a source at <paramref name="delta"/> from the listener.</summary>
    /// <param name="delta">The source position minus the listener position, in metres.</param>
    /// <param name="distance">The length of <paramref name="delta"/>.</param>
    /// <param name="minimumDistance">The voice's minimum distance.</param>
    /// <param name="maximumDistance">The voice's maximum distance.</param>
    /// <param name="listener">The listener, whose orientation defines the lateral axis.</param>
    /// <returns>The pan, −1 hard left to +1 hard right.</returns>
    public static float Pan(
        Vector3d delta,
        double distance,
        float minimumDistance,
        float maximumDistance,
        in AudioListenerState listener)
    {
        // A source exactly on the listener has no direction to speak of, and the original centres it
        // rather than dividing by zero.
        if (distance <= 0.0)
        {
            return 0f;
        }

        float scale = minimumDistance + maximumDistance;
        if (scale <= 0f)
        {
            return 0f;
        }

        // The engine's v * M convention: the row-major basis is applied with the vector on the left,
        // which is what Matrix3f.Transform does. The other convention transposes the rotation and
        // mirrors the stereo image -- the same trap the camera and the bone palette both hit.
        Vector3d local = listener.Orientation.Transform(delta);

        return Math.Clamp((float)(local.X / scale), -1f, 1f);
    }

    /// <summary>Clamps a pitch to the range the original's frequency control allows.</summary>
    /// <param name="pitch">The requested rate multiplier.</param>
    /// <returns>The pitch, clamped to 0.5 – 2.0.</returns>
    /// <remarks>
    /// The bounds are literals in <c>0x496E60</c>, applied just before the rate is multiplied by the
    /// sound's own sample rate and handed to <c>SetFrequency</c>.
    /// </remarks>
    public static float ClampPitch(float pitch) =>
        float.IsNaN(pitch) ? 1f : Math.Clamp(pitch, 0.5f, 2f);
}

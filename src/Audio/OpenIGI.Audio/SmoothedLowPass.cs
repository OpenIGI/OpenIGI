namespace OpenIGI.Audio;

/// <summary>
/// A low-pass a sound is heard through, its cutoff moved by the game while it sounds and eased there rather than stepped:
/// what a wall or a roof does to what comes through it. Two one-pole sections in series -- twelve decibels an octave, close
/// enough to what a wall does and, unlike a biquad, unable to ring or go unstable however fast the cutoff is moved.
/// </summary>
public sealed class SmoothedLowPass
{
    /// <summary>The cutoff that lets everything through, in hertz.</summary>
    public const float OpenCutoffHz = 24000f;

    /// <summary>The lowest cutoff, in hertz.</summary>
    public const float MinimumCutoffHz = 60f;

    private readonly int _sampleRate;
    private readonly float _approach;
    private float _coefficient;
    private float _targetCoefficient;
    private float _poleA;
    private float _poleB;

    /// <summary>Initializes a new instance of the <see cref="SmoothedLowPass"/> class.</summary>
    /// <param name="sampleRate">The rate of what passes through it.</param>
    /// <param name="cutoffHz">The cutoff it starts at.</param>
    /// <param name="smoothingSeconds">How long a move of the cutoff takes to get most of the way, in seconds.</param>
    public SmoothedLowPass(int sampleRate, float cutoffHz = OpenCutoffHz, float smoothingSeconds = 0.2f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(smoothingSeconds);
        _sampleRate = sampleRate;

        // The fraction of the remaining distance to close each sample, chosen so that a step reaches
        // 1 - 1/e of the way in smoothingSeconds. One exponential, evaluated once.
        _approach = 1f - MathF.Exp(-1f / (smoothingSeconds * sampleRate));
        _coefficient = CoefficientFor(cutoffHz, sampleRate);
        _targetCoefficient = _coefficient;
    }

    /// <summary>Gets or sets the cutoff the filter eases towards, in hertz; safe to set from another thread.</summary>
    public float CutoffHz
    {
        get => CutoffFor(Volatile.Read(ref _targetCoefficient), _sampleRate);
        set => Volatile.Write(ref _targetCoefficient, CoefficientFor(value, _sampleRate));
    }

    /// <summary>Puts the cutoff where asked at once, with no easing: for the first moment a sound is heard.</summary>
    /// <param name="cutoffHz">The cutoff.</param>
    public void Snap(float cutoffHz)
    {
        float coefficient = CoefficientFor(cutoffHz, _sampleRate);
        Volatile.Write(ref _targetCoefficient, coefficient);
        _coefficient = coefficient;
    }

    /// <summary>Passes one sample through, easing the cutoff a sample's worth on the way.</summary>
    /// <param name="sample">The sample.</param>
    /// <returns>The sample as it comes out.</returns>
    public float Process(float sample)
    {
        _coefficient += (Volatile.Read(ref _targetCoefficient) - _coefficient) * _approach;
        _poleA += (sample - _poleA) * (1f - _coefficient);
        _poleB += (_poleA - _poleB) * (1f - _coefficient);
        return _poleB;
    }

    /// <summary>The one-pole coefficient for a cutoff at a rate: nought lets everything through.</summary>
    /// <param name="cutoffHz">The cutoff.</param>
    /// <param name="sampleRate">The rate.</param>
    /// <returns>The coefficient.</returns>
    public static float CoefficientFor(float cutoffHz, int sampleRate)
    {
        float clamped = Math.Clamp(cutoffHz, MinimumCutoffHz, OpenCutoffHz);
        return clamped >= sampleRate / 2f
            ? 0f
            : MathF.Exp(-2f * MathF.PI * clamped / sampleRate);
    }

    /// <summary>The cutoff a one-pole coefficient stands for at a rate.</summary>
    /// <param name="coefficient">The coefficient.</param>
    /// <param name="sampleRate">The rate.</param>
    /// <returns>The cutoff, in hertz.</returns>
    public static float CutoffFor(float coefficient, int sampleRate) =>
        coefficient <= 0f
            ? OpenCutoffHz
            : Math.Clamp(
                -MathF.Log(coefficient) * sampleRate / (2f * MathF.PI),
                MinimumCutoffHz,
                OpenCutoffHz);
}

namespace OpenIGI.Presentation.Rendering;

/// <summary>How hot a body reads to the remastered thermal sensor: fully while alive, cooling once dead.</summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original's infrared view draws the dead as hot as the living. A body here cools as a body does, by
/// Newton's law: its heat above the cold it settles at falls off exponentially, most of it gone in the first minute, so a fresh
/// kill still reads warm and an old one reads as a cool shape on the ground -- still a shape, never gone.
/// </para>
/// <para>
/// Presentation state, timed on the title's own tick clock rather than the wall's, so a paused game does not cool its dead and a
/// lockstep capture cools them the same every run. A body is remembered from the first moment it is seen dead; one that is seen
/// alive again -- revived, or a slot reused -- is forgotten.
/// </para>
/// </remarks>
public sealed class BodyCooling
{
    /// <summary>A living body's heat.</summary>
    public const float LivingHeat = 1f;

    /// <summary>The heat a dead body settles at: cooler than the living, warmer than the ground it lies on.</summary>
    public const float SettledHeat = 0.3f;

    /// <summary>How long a body takes to lose all but a twentieth of its heat above <see cref="SettledHeat"/>, in seconds.</summary>
    public const float CoolingSeconds = 60f;

    // Three time constants in the cooling time: e^-3 is the twentieth left.
    private const float TimeConstants = 3f;

    private readonly Dictionary<object, double> _diedAt = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets how hot a body reads now.</summary>
    /// <param name="body">The body, the same object from frame to frame.</param>
    /// <param name="dead">Whether it is dead.</param>
    /// <param name="seconds">The title's clock, in seconds.</param>
    /// <returns>Its heat, from <see cref="SettledHeat"/> to <see cref="LivingHeat"/>.</returns>
    public float HeatOf(object body, bool dead, double seconds)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!dead)
        {
            _diedAt.Remove(body);
            return LivingHeat;
        }

        if (!_diedAt.TryGetValue(body, out double diedAt))
        {
            _diedAt[body] = seconds;
            diedAt = seconds;
        }

        return Cooled(seconds - diedAt);
    }

    /// <summary>Gets how hot a body reads this long after it died.</summary>
    /// <param name="secondsDead">How long it has been dead, in seconds; nought or less is the moment of death.</param>
    /// <returns>Its heat.</returns>
    public static float Cooled(double secondsDead)
    {
        float elapsed = (float)Math.Max(secondsDead, 0.0);
        return SettledHeat + ((LivingHeat - SettledHeat) * MathF.Exp(-TimeConstants * elapsed / CoolingSeconds));
    }

    /// <summary>Forgets every body, for a level that ends.</summary>
    public void Forget() => _diedAt.Clear();
}

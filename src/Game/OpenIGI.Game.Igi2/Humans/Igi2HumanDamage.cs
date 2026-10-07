namespace OpenIGI.Game.Igi2.Humans;

/// <summary>How hurt a human is, <c>HumanDamage_t</c>: a total against a limit, and the share each zone took.</summary>
public sealed class Igi2HumanDamage
{
    private readonly float[] _parts = new float[PartCount];
    private readonly float[] _multipliers = new float[PartCount];

    /// <summary>The number of damage zones.</summary>
    public const int PartCount = 17;

    /// <summary>Gets or sets the damage taken, <c>vDamage</c>.</summary>
    public float Damage { get; set; }

    /// <summary>Gets or sets the damage that kills, <c>vMaxDamage</c>.</summary>
    public float MaxDamage { get; set; }

    /// <summary>Gets or sets the zone the last damage landed on, <c>eDamagePart</c>.</summary>
    public Igi2DamagePart LastPart { get; set; }

    /// <summary>Gets a value indicating whether the damage has reached the limit.</summary>
    public bool IsFatal => Damage >= MaxDamage;

    /// <summary>Gets the damage one zone has taken, <c>atDamagePart[part].vDamage</c>.</summary>
    /// <param name="part">The zone.</param>
    /// <returns>The zone's damage.</returns>
    public float PartDamage(Igi2DamagePart part) => _parts[(int)part];

    /// <summary>Gets the damage multiplier a zone took when the damage was last cleared, <c>atDamagePart[part].vMultiplier</c>.</summary>
    /// <param name="part">The zone.</param>
    /// <returns>The multiplier.</returns>
    public float Multiplier(Igi2DamagePart part) => _multipliers[(int)part];

    /// <summary>
    /// Heals everything and reloads each zone's multiplier from the damage table, <c>HumanDamage_ClearDamage</c>
    /// (<c>igi2 0x469B40</c>).
    /// </summary>
    /// <param name="multipliers">The table's multipliers, <see cref="PartCount"/> of them.</param>
    /// <exception cref="ArgumentException">The table is not <see cref="PartCount"/> long.</exception>
    public void Clear(IReadOnlyList<float> multipliers)
    {
        ArgumentNullException.ThrowIfNull(multipliers);
        if (multipliers.Count != PartCount)
        {
            throw new ArgumentException($"A damage table has {PartCount} zones, not {multipliers.Count}.", nameof(multipliers));
        }

        Damage = 0f;
        for (int i = 0; i < PartCount; i++)
        {
            _parts[i] = 0f;
            _multipliers[i] = multipliers[i];
        }
    }

    /// <summary>Adds damage to a zone and to the total, the zone first, as the original adds them.</summary>
    /// <param name="part">The zone.</param>
    /// <param name="amount">The damage.</param>
    public void Add(Igi2DamagePart part, float amount)
    {
        _parts[(int)part] = ((Fpu)amount + _parts[(int)part]).F;
        Damage = ((Fpu)amount + Damage).F;
    }
}

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>How hurt a human is, <c>HumanDamage_t</c>: a total against a limit, and the share each zone took.</summary>
public sealed class Igi2HumanDamage
{
    private readonly float[] _parts = new float[PartCount];

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

    /// <summary>Adds damage to a zone and to the total, the zone first, as the original adds them.</summary>
    /// <param name="part">The zone.</param>
    /// <param name="amount">The damage.</param>
    public void Add(Igi2DamagePart part, float amount)
    {
        _parts[(int)part] = amount + _parts[(int)part];
        Damage = amount + Damage;
    }
}

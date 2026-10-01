namespace OpenIGI.Game.Weapons;

/// <summary>
/// One entry of the ammunition table, as <c>DefineAmmoType</c> (<c>0x47CAD0</c>) fills it.
/// </summary>
/// <remarks>
/// <para>
/// Built exactly like the weapon table and by the same shape of code: <c>0x47CAB0</c> resets the count
/// and walks <c>LOCAL:ammo</c>, and <c>AmmoType_Open</c> (<c>0x47CA40</c>) runs
/// <c>&lt;dir&gt;/ammo.qsc</c> for each sub-directory. Fourteen 360-byte records at <c>0x5BE3E0</c>.
/// Unlike the weapon call this one is a fixed fifteen arguments — no task classes are named, so there
/// is no tail.
/// </para>
/// <para>
/// Two lookups exist: by <see cref="AmmoId"/> (<c>0x47CD40</c>) and by <see cref="CalibreId"/>
/// (<c>0x47CD70</c>). The second is the join a weapon uses — the gun's init at <c>0x4789C0</c> takes
/// its weapon record's calibre and caches the ammo record beside it.
/// </para>
/// </remarks>
public sealed class AmmoType
{
    /// <summary>Gets the numeric id, which is also the inventory slot this ammunition occupies.</summary>
    /// <remarks>
    /// Argument 0. The fourteen shipped types take 0 to 13 with no gaps, and picking ammunition up
    /// (<c>0x46DC40</c>) indexes an inventory array with it directly.
    /// </remarks>
    public int AmmoId { get; init; }

    /// <summary>Gets the script symbol this type publishes its id under.</summary>
    /// <remarks>
    /// Argument 1. The loader registers this against <see cref="AmmoId"/> as it reads it, which is what
    /// gives the pattern-registered <c>AMMO_ID_*</c> symbols (<c>0x47CF20</c>) their values.
    /// </remarks>
    public string ScriptId { get; init; } = string.Empty;

    /// <summary>Gets the display name, as a language key.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the description, as a language key.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the calibre this ammunition is, which is what a weapon names to find it.</summary>
    /// <remarks>
    /// The real calibres use their own numbers — 12, 44, 127, 357, 556, 762, 919, 1000. The six special
    /// types are numbered <b>10000 upwards</b> in the order grenade, proximity mine, M203, flashbang,
    /// Dragunov, medipack, which is the mapping the weapon roster's directory names do not give.
    /// </remarks>
    public int CalibreId { get; init; }

    /// <summary>Gets the mass of one round in grams.</summary>
    /// <remarks>
    /// Argument 5, the only floating-point one. No consumer of this field was found — every special
    /// type carries the same 2, and only the real calibres vary (.357 is 9.7, 7.62 is 12).
    /// </remarks>
    public float MassInGrams { get; init; }

    /// <summary>Gets the tracer's colour at the muzzle end, as 0–255 components.</summary>
    public TracerColour NearColour { get; init; }

    /// <summary>Gets the tracer's colour at the far end, as 0–255 components.</summary>
    public TracerColour FarColour { get; init; }

    /// <summary>Gets whether a tracer is drawn for this ammunition at all.</summary>
    /// <remarks>
    /// Argument 12, and the first thing <c>0x47ABD0</c> tests. True for the rifle and pistol calibres;
    /// false for shotgun shells, the 1000 calibre and every special type — none of which fires a
    /// visible round.
    /// </remarks>
    public bool DrawsTracer { get; init; }

    /// <summary>Gets the model a round of this ammunition is drawn with.</summary>
    /// <remarks>Empty for the 127 calibre, which names none.</remarks>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the sound bank entry the round uses.</summary>
    public string Sound { get; init; } = string.Empty;

    /// <summary>Gets the position in load order, which is the original's array index.</summary>
    public int Index { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{ScriptId} (#{AmmoId}, calibre {CalibreId})";
}

/// <summary>A tracer colour, held the way the ammunition scripts write it: 0–255 per component.</summary>
/// <param name="Red">The red component.</param>
/// <param name="Green">The green component.</param>
/// <param name="Blue">The blue component.</param>
public readonly record struct TracerColour(int Red, int Green, int Blue)
{
    /// <summary>The reciprocal of 255, as <c>0x47ABD0</c> spells it.</summary>
    public const float ComponentScale = 0.0039215689f;

    /// <summary>Gets the red component scaled to 0–1.</summary>
    public float RedFraction => Red * ComponentScale;

    /// <summary>Gets the green component scaled to 0–1.</summary>
    public float GreenFraction => Green * ComponentScale;

    /// <summary>Gets the blue component scaled to 0–1.</summary>
    public float BlueFraction => Blue * ComponentScale;
}

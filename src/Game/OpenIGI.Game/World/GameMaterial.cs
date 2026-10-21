namespace OpenIGI.Game.World;

/// <summary>
/// What one <c>GunShotEffect</c> a material declares does to the surface it is on.
/// </summary>
/// <param name="SpawnsParticles">The first flag the class reads.</param>
/// <param name="LeavesMark">The second flag.</param>
/// <param name="EntryTexture">The mark a round entering the surface leaves, or empty for none.</param>
/// <param name="ExitTexture">The mark a round leaving it leaves, or empty for none.</param>
/// <param name="SizeInMetres">How wide the mark is, as authored.</param>
/// <param name="Model">The impact effect's model.</param>
/// <remarks>
/// <para>
/// Read from <c>0x4387B0</c>, the <c>GunShotEffect</c> class's parameter reader. It consumes
/// <b>six</b> arguments and stores the two texture names as <b>24-character</b> fields, the size
/// multiplied by 4096 into world units, and the model as 32 characters. It then resolves each texture
/// name and leaves a handle at <c>+88</c> and <c>+92</c> — <b>zero when the name is empty</b>.
/// </para>
/// <para>
/// That zero is load-bearing: the shot resolution refuses to record an impact at all unless the
/// struck material's block has a non-null <c>+88</c>. A material naming no texture leaves no mark and
/// costs nothing, which is how <c>Air</c> and <c>Water</c> work.
/// </para>
/// </remarks>
public readonly record struct GunShotEffect(
    bool SpawnsParticles,
    bool LeavesMark,
    string EntryTexture,
    string ExitTexture,
    float SizeInMetres,
    string Model)
{
    /// <summary>World units in a metre, which the reader multiplies the size by.</summary>
    public const float UnitsPerMetre = 4096f;

    /// <summary>Gets the mark's width in world units — the size as the original stores it.</summary>
    public float SizeInUnits => SizeInMetres * UnitsPerMetre;

    /// <summary>Gets whether this effect leaves any mark at all.</summary>
    /// <remarks>
    /// The original's test is on the resolved texture handle rather than the name, but the handle is
    /// only resolved when the name is non-empty, so the two agree.
    /// </remarks>
    public bool HasMark => !string.IsNullOrEmpty(EntryTexture) || !string.IsNullOrEmpty(ExitTexture);

    /// <summary>Picks the texture for one impact.</summary>
    /// <param name="isEntry">Whether the round was entering the surface.</param>
    /// <returns>The texture name, or empty when this effect marks nothing.</returns>
    /// <remarks>
    /// <c>0x438440</c> takes the entry texture only when the impact is flagged as an entry <i>and</i>
    /// that texture resolved; otherwise it falls back to the exit one. So a material that names only an
    /// exit mark still marks an entry hit.
    /// </remarks>
    public string TextureFor(bool isEntry) =>
        isEntry && !string.IsNullOrEmpty(EntryTexture) ? EntryTexture : ExitTexture;
}

/// <summary>
/// One row of the game material table — what a surface is made of, and what happens when it is hit.
/// </summary>
/// <remarks>
/// <para>
/// <c>DefineGameMaterial</c> (<c>0x407F50</c>) fills a <b>616-byte</b> record per material, and
/// <c>SetActiveMaterial</c> chooses which row is being filled. So <b>a material's index is its row</b>,
/// which is what makes a collision face's fourth field usable: it selects one of these directly.
/// </para>
/// <para>
/// The call is variable length for the same reason the weapon roster's is — it names two task classes
/// and each consumes its own parameters. Retail names <c>TASKTYPE_GUNSHOTEFFECT</c> for both, so every
/// row is 36 arguments.
/// </para>
/// </remarks>
public sealed class GameMaterial
{
    /// <summary>Whether a line of sight passes through this global material row.</summary>
    /// <param name="globalMaterial">A row of the retail material table.</param>
    /// <returns>
    /// <see langword="true"/> for the see-through surfaces a sight ray must ignore: Water (2), Glass
    /// (14), Fence (18), MetalLadder (22) and MetalFence (23).
    /// </returns>
    /// <remarks>
    /// Retail's binocular detector culls contacts by sector/portal visibility, which glass panes and
    /// chain-link fences do not bound, so a guard behind them stays visible. This reimplementation
    /// approximates that with a sight raycast; those materials must therefore not count as occluders.
    /// Air (0) is deliberately excluded — the terrain trace reports material 0 by default, and treating
    /// it as transparent would let contacts be seen straight through a hill.
    /// </remarks>
    public static bool IsSightTransparent(int globalMaterial) =>
        globalMaterial is 2 or 14 or 18 or 22 or 23;

    /// <summary>Gets the row this material occupies, which is what a collision face names.</summary>
    public int Index { get; init; }

    /// <summary>Gets the material's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets what the surface resists a round with — argument 1, at <c>+32</c>.</summary>
    /// <remarks>
    /// The shot resolution subtracts this from the round's own penetration power to decide whether the
    /// round goes through: <c>0x47BD88</c> computes <c>shot+60 - material+64</c> (that <c>+64</c> is
    /// this field, counted from the row start rather than from the name), penetrates outright when the
    /// margin is at least one, and rolls against <c>0.6</c> when it is exactly zero. It is the
    /// <c>surfaceResistance</c> <see cref="Weapons.GunShotBallistics.Penetrates"/> asks for and nothing
    /// yet supplies. <c>Wood</c> declares <c>1</c>.
    /// </remarks>
    public float BulletResistance { get; init; }

    /// <summary>Gets the widest angle a round may deflect off this surface — argument 2, at <c>+36</c>.</summary>
    /// <remarks>
    /// In DEGREES: <c>0x47BB96</c> reads <c>material+68</c> and multiplies by <c>0.017453292</c>
    /// (π/180) before comparing it against the incidence angle, and a zero here means the surface never
    /// deflects. <c>Wood</c> declares <c>10</c>.
    /// </remarks>
    public float RicochetAngleDegrees { get; init; }

    /// <summary>Gets the third unnamed number — argument 3, at <c>+40</c>.</summary>
    /// <remarks>No consumer traced. <c>Wood</c> declares <c>0.01</c>.</remarks>
    public float Value3 { get; init; }

    /// <summary>Gets the flag at argument 4.</summary>
    /// <remarks>
    /// Read as an integer into <c>+44</c>. <c>Air</c> and <c>Water</c> declare <see langword="true"/>
    /// and <c>Wood</c> <see langword="false"/>, which reads as "you can move through this" — but that
    /// is inference from three rows, not a consumer, so it is left unnamed.
    /// </remarks>
    public bool Flag4 { get; init; }

    /// <summary>Gets the number at argument 5.</summary>
    public float Value5 { get; init; }

    /// <summary>Gets the six footstep sounds, in script order.</summary>
    public IReadOnlyList<string> WalkSounds { get; init; } = [];

    /// <summary>Gets the sound a landing makes.</summary>
    public string LandSound { get; init; } = string.Empty;

    /// <summary>Gets the sound rain makes on it.</summary>
    public string RainSound { get; init; } = string.Empty;

    /// <summary>Gets the six sounds a bullet striking it makes, in script order.</summary>
    /// <remarks>
    /// The shot resolution picks one of six at random and rate-limits the whole set globally, so a
    /// burst into a wall does not stack six samples.
    /// </remarks>
    public IReadOnlyList<string> BulletSounds { get; init; } = [];

    /// <summary>Gets the two remaining sounds, which no traced consumer reads.</summary>
    public IReadOnlyList<string> ExtraSounds { get; init; } = [];

    /// <summary>Gets what a round entering the surface does.</summary>
    public GunShotEffect Entry { get; init; }

    /// <summary>Gets what a round leaving it does.</summary>
    public GunShotEffect Exit { get; init; }

    /// <summary>Gets whether a hit on this material is recorded at all.</summary>
    /// <remarks>
    /// The shot loop's own guard. A material whose entry effect names no texture is <b>skipped
    /// entirely</b> — not drawn faintly, not drawn with a default: no impact is recorded for it.
    /// </remarks>
    public bool RecordsImpacts => Entry.HasMark;

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Index}: {Name}{(RecordsImpacts ? $" ({Entry.EntryTexture})" : " (no mark)")}";
}

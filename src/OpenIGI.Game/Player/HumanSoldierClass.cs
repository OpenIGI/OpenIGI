namespace OpenIGI.Game.Player;

/// <summary>
/// What distinguishes one AI-driven <c>Human</c> class from another — <c>HumanSoldier</c>,
/// <c>HumanSoldierFemale</c> and <c>HumanSoldierRPG</c>.
/// </summary>
/// <remarks>
/// <para>
/// The original registers the three as siblings under <c>Human</c> (<c>0x45CAB0</c>, <c>0x454970</c>,
/// <c>0x456AD0</c>), each with its own handler set. The handlers are the same code shape over different
/// data: the create defaults, the stand fallback, the hit-zone table, the body machine and its clip table,
/// and the death rule. The port runs one soldier pipeline and asks the soldier's class for that data,
/// rather than carrying three copies of the pipeline.
/// </para>
/// <para>
/// <c>HumanSoldierPistol</c> (<c>0x459880</c>) is a fourth sibling. No retail level declares it and its
/// machine (<c>off_53E8B8</c>) has not been read, so it is not modelled.
/// </para>
/// </remarks>
public sealed class HumanSoldierClass
{
    private readonly Func<HumanBodyMachine> _createBody;

    private HumanSoldierClass(
        string typeName,
        int defaultBoneHierarchy,
        int defaultStandAnimation,
        int fallbackStandAnimation,
        HumanBodyStateTable states,
        int walkAnimation,
        int runAnimation,
        IReadOnlyList<HumanHitZone> hitZones,
        SoldierDeathClips? deathClips,
        bool scalesEkkRootMotion,
        int? grenadeThrowState,
        Func<HumanBodyMachine> createBody)
    {
        TypeName = typeName;
        DefaultBoneHierarchy = defaultBoneHierarchy;
        DefaultStandAnimation = defaultStandAnimation;
        FallbackStandAnimation = fallbackStandAnimation;
        States = states;
        WalkAnimation = walkAnimation;
        RunAnimation = runAnimation;
        HitZones = hitZones;
        DeathClips = deathClips;
        ScalesEkkRootMotion = scalesEkkRootMotion;
        GrenadeThrowState = grenadeThrowState;
        _createBody = createBody;
    }

    /// <summary>Gets the ordinary soldier — <c>0x45CAB0</c>.</summary>
    /// <remarks>
    /// Its walk is the player's walk clip, which is what the port has always drawn a patrolling soldier
    /// with; its machine has no forward-walk state of its own.
    /// </remarks>
    public static HumanSoldierClass Soldier { get; } = new(
        PlayerTaskTypes.HumanSoldier,
        defaultBoneHierarchy: 1,
        defaultStandAnimation: -1,
        fallbackStandAnimation: 2,
        HumanSoldierStates.Table,
        walkAnimation: HumanLocomotionStates.For(1).AnimationId,
        runAnimation: HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.RunState),
        HumanHitZoneTable.Soldier,
        SoldierDeathClips.Soldier,
        scalesEkkRootMotion: false,
        grenadeThrowState: HumanSoldierStates.GrenadeThrowState,
        static () => new HumanSoldierBodyMachine());

    /// <summary>Gets the female soldier — <c>0x454970</c>, the class of Level 14's Anya and Ekk.</summary>
    public static HumanSoldierClass FemaleSoldier { get; } = new(
        PlayerTaskTypes.HumanSoldierFemale,
        defaultBoneHierarchy: 6,
        defaultStandAnimation: -1,
        fallbackStandAnimation: 6,
        HumanSoldierFemaleStates.Table,
        walkAnimation: HumanSoldierFemaleStates.Table.AnimationOf(HumanSoldierFemaleStates.WalkState),
        runAnimation: HumanSoldierFemaleStates.Table.AnimationOf(HumanSoldierFemaleStates.RunState),
        HumanHitZoneTable.FemaleSoldier,
        deathClips: null,
        scalesEkkRootMotion: true,
        grenadeThrowState: null,
        static () => new HumanSoldierFemaleBodyMachine());

    /// <summary>Gets the RPG soldier — <c>0x456AD0</c>, Level 10's launcher.</summary>
    public static HumanSoldierClass RpgSoldier { get; } = new(
        PlayerTaskTypes.HumanSoldierRpg,
        defaultBoneHierarchy: 2,
        defaultStandAnimation: 2,
        fallbackStandAnimation: 2,
        HumanSoldierRpgStates.Table,
        walkAnimation: HumanSoldierRpgStates.Table.AnimationOf(HumanSoldierRpgStates.WalkState),
        runAnimation: HumanSoldierRpgStates.Table.AnimationOf(HumanSoldierRpgStates.RunState),
        HumanHitZoneTable.RpgSoldier,
        SoldierDeathClips.RpgSoldier,
        scalesEkkRootMotion: false,
        grenadeThrowState: null,
        static () => new HumanSoldierRpgBodyMachine());

    /// <summary>Gets the script name the level data instantiates the class under.</summary>
    public string TypeName { get; }

    /// <summary>Gets the <c>Bone Heirachy</c> the constructor writes before the level's value — <c>+3320</c>.</summary>
    public int DefaultBoneHierarchy { get; }

    /// <summary>Gets the <c>Stand Animation</c> the constructor writes before the level's value — <c>+3324</c>.</summary>
    public int DefaultStandAnimation { get; }

    /// <summary>Gets the clip a missing or unset stand animation falls back to at init.</summary>
    /// <remarks>The literal the class's init hands <c>0x489EA0</c>: 2 in <c>0x456E50</c>, 6 in <c>0x454F10</c>.</remarks>
    public int FallbackStandAnimation { get; }

    /// <summary>Gets the class's body-machine clip table — its <c>Human+824</c>.</summary>
    public HumanBodyStateTable States { get; }

    /// <summary>Gets the clip a moving body walks in.</summary>
    public int WalkAnimation { get; }

    /// <summary>Gets the clip a moving body runs in.</summary>
    public int RunAnimation { get; }

    /// <summary>Gets the class's hit-zone table, as its constructor hands it to <c>0x489C30</c>.</summary>
    public IReadOnlyList<HumanHitZone> HitZones { get; }

    /// <summary>Gets the clips the class's death chooses between, or <see langword="null"/> for a zone-only death.</summary>
    public SoldierDeathClips? DeathClips { get; }

    /// <summary>
    /// Gets whether the class scales an Ekk's root motion by the player's delta-translation scale.
    /// </summary>
    /// <remarks>
    /// The female soldier's update (<c>0x4550B0</c>) multiplies the animation's delta translation by
    /// <c>flt_56E1E8</c> — <c>humanplayer.qvm</c> argument 0 — when <c>Human+1324</c> is clear and the
    /// brain's type is <c>AITYPE_EKK</c>, which is what the player's own update does unconditionally.
    /// </remarks>
    public bool ScalesEkkRootMotion { get; }

    /// <summary>Gets the state whose end releases a thrown grenade, or <see langword="null"/> for a class that throws none.</summary>
    public int? GrenadeThrowState { get; }

    /// <summary>Gets every modelled class.</summary>
    public static IReadOnlyList<HumanSoldierClass> All { get; } = [Soldier, FemaleSoldier, RpgSoldier];

    /// <summary>Builds a new body machine of this class, standing.</summary>
    /// <returns>The machine.</returns>
    public HumanBodyMachine CreateBody() => _createBody();

    /// <summary>Picks the clip the class's death state plays.</summary>
    /// <param name="mode">The mode the damage handler recorded.</param>
    /// <param name="zone">The zone the killing round struck.</param>
    /// <param name="shotFromTheFront">What the turn to the shot reported, when the death made one.</param>
    /// <param name="coinFlip">The engine's <c>rand(2)</c>.</param>
    /// <param name="scriptOverride"><c>HumanAI+15056</c>, or <c>-1</c>.</param>
    /// <returns>The animation id, or <c>-1</c> when nothing names one.</returns>
    public int ChooseDeathAnimation(
        SoldierDeathMode mode,
        int zone,
        bool shotFromTheFront,
        int coinFlip,
        int scriptOverride = -1) =>
        DeathClips is { } clips
            ? SoldierDeath.Choose(clips, mode, zone, HitZones, shotFromTheFront, coinFlip, scriptOverride)
            : SoldierDeath.ChooseByZone(zone, HitZones, scriptOverride);

    /// <summary>Whether the class's death turns the body to the round for this mode.</summary>
    /// <param name="mode">The mode the damage handler recorded.</param>
    /// <returns><see langword="true"/> when the death needs front from back.</returns>
    public bool TurnsToTheShot(SoldierDeathMode mode) => DeathClips is not null && SoldierDeath.TurnsToTheShot(mode);

    /// <inheritdoc/>
    public override string ToString() => TypeName;
}

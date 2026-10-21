using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>
/// The base every animated character derives from — the original's <c>Human</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered by the original at <c>0x45E450</c> with an instance size of 3320 and a pool of 128. It
/// declares no parameters of its own; its subclasses do, and every one of them starts with the same
/// four (<see cref="HumanSoldierTask"/> and <see cref="HumanPlayerTask"/> both open
/// <c>Position, Gamma, Model, Team</c>).
/// </para>
/// </remarks>
public abstract class HumanTask
    : ContainerTask, IWeaponCollisionTarget, IWeaponHitTarget, IExplosionHitTarget, IHumanAiEventTarget, IAiHuman, IAiDetectable
{
    /// <summary>
    /// Gets or sets the live body the human's game keeps for it: where it stands, its eyes and its stance; <see langword="null"/>
    /// before the game stands one, when the human is where its parameters put it, standing.
    /// </summary>
    /// <remarks>
    /// The original keeps these on the <c>Human</c> itself (<c>+32</c> the body's origin, <c>+829</c> crouched, <c>+3318</c> active);
    /// the port's games keep the body in their own rigs, so they give their humans this, as they give them <see cref="Events"/>.
    /// </remarks>
    public IHumanBodyState? Body { get; set; }

    /// <summary>
    /// Gets or sets the game whose body stands for the human when it is not the game whose level it is in, or
    /// <see langword="null"/> when the level's own game drives it.
    /// </summary>
    /// <remarks>
    /// A human another game drives is that game's: its brain, its body, its drawing and its collision. The level's own game
    /// leaves it alone and keeps only the task, which the level's scripts read (<c>isDead</c>, <c>eTeam</c>) and which the other
    /// game's body keeps current.
    /// </remarks>
    public GameTitle? Driver { get; set; }

    /// <inheritdoc/>
    Vector3d IAiHuman.Position => Body?.Feet ?? Position;

    /// <inheritdoc/>
    /// <remarks>Without a body, its eyes standing where its parameters put it.</remarks>
    public Vector3d OriginalViewPosition => Body?.Eyes ?? new Vector3d(Position.X, Position.Y, Position.Z + PlayerBody.StandingEyeHeight);

    /// <inheritdoc/>
    Vector3d IAiDetectable.DetectablePosition => Body?.Feet ?? Position;

    /// <inheritdoc/>
    float IAiDetectable.DetectableRadius => (float)HumanSeparation.RadiusInUnits;

    /// <summary>Gets or sets how far off another game's detectors see the human, as a share of a cone's length.</summary>
    /// <remarks>
    /// Project I.G.I. keeps no measure of how lit a human stands, so its own humans are seen as well as in the light and its
    /// detectors never read this. Content that brings another game's detectors may measure it as that game does.
    /// </remarks>
    public float VisibilityFactor { get; set; } = 1f;

    /// <inheritdoc/>
    float? IAiDetectable.VisibilityFactor => VisibilityFactor;

    /// <inheritdoc/>
    bool IAiDetectable.IsGone => IsDeleted;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human</c>'s answer (<c>0x45FE30</c>): a human of its side, alive unless its death latch is set, active as its body says,
    /// seen at one point, its eyes (<c>0x465820</c>: its origin lifted 2826.24 standing, 1433.6 crouched).
    /// </remarks>
    public AiDetectionInfo DetectionInfo() =>
        AiDetectionInfo.OnePoint(AiDetectableKind.Human, OriginalViewPosition, Team, !IsDead, Body?.IsActive ?? true);

    /// <summary>Gets or sets the handlers the human's game gives its hit events; <see langword="null"/> takes every bullet and does nothing.</summary>
    /// <remarks>
    /// Every type the shot or a blast can strike takes the engine's hit events itself (<c>Human</c>, <c>0x45E450</c>, links
    /// <c>WeaponCollisionEvaluate</c>, <c>WeaponHit</c> and <c>ExplosionHit</c>; <c>HumanPlayer</c> and each soldier class
    /// override the last two). What the handlers read -- the health, the brain, the view -- the game keeps, so the game
    /// gives its humans these.
    /// </remarks>
    public IHumanEvents? Events { get; set; }

    /// <inheritdoc/>
    public bool EvaluateWeaponCollision(in WeaponCollisionQuery query) => Events?.EvaluateWeaponCollision(this, query) ?? true;

    /// <inheritdoc/>
    public void WeaponHit(in WeaponHit hit) => Events?.WeaponHit(this, hit);

    /// <inheritdoc/>
    public void ExplosionHit(in ExplosionHit hit) => Events?.ExplosionHit(this, hit);

    /// <inheritdoc/>
    public void SendEvent(in HumanAiEvent aiEvent) => Events?.SendEvent(this, aiEvent);

    /// <summary>Gets or sets the world position, in engine units.</summary>
    /// <remarks>The original keeps this as three <see cref="double"/> at <c>Human+32</c>.</remarks>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the heading about the vertical axis, in radians.</summary>
    public float Gamma { get; set; }

    /// <summary>Gets or sets the pack instance name the character's model comes from.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the team, which decides who shoots at whom.</summary>
    /// <remarks>
    /// The original registers <c>HUMAN_TEAM_*</c> script constants alongside the class
    /// (<c>0x45E450</c>'s tail loop over <c>off_53F348</c>), so a script may name these rather than
    /// numbering them.
    /// </remarks>
    public int Team { get; set; }

    /// <summary>Gets or sets whether this character has died — the original's <c>Human+3317</c>.</summary>
    /// <remarks>
    /// A permanent latch, not a health test: the byte is cleared only by the <c>Human</c> reset
    /// (<c>0x45ECD0</c>) and set to 1 by every death path there is (<c>0x408447</c>, <c>0x45551C</c>,
    /// <c>0x45574F</c>, <c>0x4573DB</c>, <c>0x45A376</c>, <c>0x45D51D</c>, …). The host owns it, the
    /// same way the original's death handlers own the byte.
    /// </remarks>
    public bool IsDead { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>One export for every character, on the class they share.</b> The original installs its
    /// ExportVars/UnexportVars pair on <c>Human</c> itself (<c>sub_45E450</c> registers
    /// <c>sub_460850</c>/<c>sub_4608D0</c>), and neither <c>HumanPlayer</c> (<c>sub_40FE30</c>) nor
    /// <c>HumanSoldier</c> (<c>sub_45CAB0</c>) registers an exporter of its own — both pass
    /// <c>sub_460BE0()</c>, the <c>Human</c> type word, as their parent. So the player and every
    /// soldier are exported by the same code, and a subclass added later is exported without being
    /// asked about.
    /// </para>
    /// <para>
    /// <c>sub_460850</c> binds exactly these two: <c>isDead</c> to the byte at <c>Human+3317</c> and
    /// <c>eTeam</c> to the int at <c>Human+252</c>. Both are bound writable because the original binds
    /// the raw storage — a script assigning to either changes the character.
    /// </para>
    /// </remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        variables.BindBool(this, "isDead", () => IsDead, value => IsDead = value);
        variables.BindInt(this, "eTeam", () => Team, value => Team = value);
    }
}

/// <summary>The live body a game keeps for one of its humans, as any game's AI perceives it.</summary>
public interface IHumanBodyState
{
    /// <summary>Gets where the human's feet are, in world units.</summary>
    Vector3d Feet { get; }

    /// <summary>Gets where its eyes are by its stance.</summary>
    Vector3d Eyes { get; }

    /// <summary>Gets a value indicating whether it keeps low, settled in a crouch or lying flat.</summary>
    bool IsKeepingLow { get; }

    /// <summary>Gets a value indicating whether it takes part in the level, <c>Human+3318</c>.</summary>
    bool IsActive { get; }
}

/// <summary>The player's character — the original's <c>HumanPlayer</c>.</summary>
/// <remarks>
/// <para>
/// Registered at <c>0x40FE30</c>, 3568 bytes, pool 8. Note what its parameter list does <b>not</b>
/// carry: a skeleton. <see cref="HumanSoldierTask"/> declares <c>Bone Heirachy</c> and the player does
/// not, which matches the constructor hard-coding skeleton 0 (<c>0x410370</c> calls the bone-object
/// initialiser with a literal). The player's rig is not a level-data decision.
/// </para>
/// </remarks>
public partial class HumanPlayerTask : HumanTask, IAiPlayer
{
    /// <summary>Gets how many times a squad has first spotted the player, as an AI that keeps the count reports it.</summary>
    public int TimesSpotted { get; private set; }

    /// <inheritdoc/>
    public bool IsKeepingLow => Body?.IsKeepingLow ?? false;

    /// <inheritdoc/>
    public void NoteDetected() => TimesSpotted++;

    /// <summary>Gets or sets the position the level places the player at.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d SpawnPosition { get => Position; set => Position = value; }

    /// <summary>Gets or sets the heading the player starts facing.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float SpawnGamma { get => Gamma; set => Gamma = value; }

    /// <summary>Gets or sets the player's model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string PlayerModel { get => Model; set => Model = value; }

    /// <summary>Gets or sets the player's team.</summary>
    [ScriptParam(ScriptParamType.Int32, "Team")]
    public int PlayerTeam { get => Team; set => Team = value; }
}

/// <summary>An AI-driven character — the original's <c>HumanSoldier</c>.</summary>
/// <remarks>
/// <para>
/// Registered at <c>0x45CAB0</c>, 3408 bytes. Its two extra parameters are the ones the player's list
/// lacks: <b><c>Bone Heirachy</c> is the skeleton id</b> (default 1, at <c>+3320</c>) and
/// <c>Stand Animation</c> the clip it stands in (default −1, at <c>+3324</c>; −1 or an id the set
/// lacks falls back to 2). The init handler <c>0x456E50</c> reads both and hands the skeleton to the
/// bone object, which is what settles that a model does not name its own skeleton —
/// and the mesh comes from the ordinary <c>Model</c> parameter, resolved by name in the chained
/// <c>Human</c> handler <c>0x45F890</c>.
/// </para>
/// <para>
/// <b>The siblings share this task.</b> <c>HumanSoldierFemale</c> (Level 14) and <c>HumanSoldierRPG</c>
/// (Level 10) are separate classes in the original, registered beside this one under <c>Human</c>, but
/// every level declares them with the same six parameters and their handlers are this class's over
/// different data. <see cref="HumanSoldierFemaleTask"/> and <see cref="HumanSoldierRpgTask"/> carry that
/// data as their <see cref="Class"/>.
/// </para>
/// <para>
/// Its hit events are <see cref="HumanTask"/>'s: <c>0x45CAB0</c> links <c>WeaponHit</c> to <c>0x45D3C0</c> and
/// <c>ExplosionHit</c> to <c>0x45A3D0</c>, over <c>Human</c>'s <c>WeaponCollisionEvaluate</c> (<c>0x45E3E0</c>).
/// </para>
/// </remarks>
public partial class HumanSoldierTask : HumanTask
{
    /// <summary>Initializes an ordinary soldier with its constructor's defaults.</summary>
    public HumanSoldierTask()
        : this(HumanSoldierClass.Soldier)
    {
    }

    /// <summary>Initializes a soldier of the given class with that class's constructor defaults.</summary>
    /// <param name="soldierClass">The class, whose defaults the level's parameters then overwrite.</param>
    protected HumanSoldierTask(HumanSoldierClass soldierClass)
    {
        ArgumentNullException.ThrowIfNull(soldierClass);
        Class = soldierClass;
        BoneHierarchy = soldierClass.DefaultBoneHierarchy;
        StandAnimation = soldierClass.DefaultStandAnimation;
    }

    /// <summary>Gets which of the soldier classes this is.</summary>
    public HumanSoldierClass Class { get; }

    /// <summary>Gets or sets the position the level places the soldier at.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d SpawnPosition { get => Position; set => Position = value; }

    /// <summary>Gets or sets the heading the soldier starts facing.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float SpawnGamma { get => Gamma; set => Gamma = value; }

    /// <summary>Gets or sets the soldier's model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string SoldierModel { get => Model; set => Model = value; }

    /// <summary>Gets or sets the soldier's team.</summary>
    [ScriptParam(ScriptParamType.Int32, "Team")]
    public int SoldierTeam { get => Team; set => Team = value; }

    /// <summary>Gets or sets the skeleton id — the <c>anims/&lt;id&gt;.iff</c> set this character uses.</summary>
    /// <remarks>The script spells the parameter <c>Bone Heirachy</c>, misspelling and all.</remarks>
    [ScriptParam(ScriptParamType.Int32, "Bone Heirachy")]
    public int BoneHierarchy { get; set; }

    /// <summary>Gets or sets the animation the soldier stands in.</summary>
    [ScriptParam(ScriptParamType.Int32, "Stand Animation")]
    public int StandAnimation { get; set; }
}

/// <summary>The handlers a game gives its humans' engine events, as each human's type links them.</summary>
public interface IHumanEvents
{
    /// <summary>Answers whether a bullet may strike the human: <c>WeaponCollisionEvaluate</c>.</summary>
    /// <param name="human">The human asked.</param>
    /// <param name="query">The trace's context.</param>
    /// <returns>Whether it may.</returns>
    bool EvaluateWeaponCollision(HumanTask human, in WeaponCollisionQuery query);

    /// <summary>A round or a blade struck the human: <c>WeaponHit</c>.</summary>
    /// <param name="human">The human struck.</param>
    /// <param name="hit">The hit.</param>
    void WeaponHit(HumanTask human, in WeaponHit hit);

    /// <summary>A blast reached the human: <c>ExplosionHit</c>.</summary>
    /// <param name="human">The human reached.</param>
    /// <param name="hit">The blast.</param>
    void ExplosionHit(HumanTask human, in ExplosionHit hit);

    /// <summary>An AI event reached the human, for its brain: <c>HumanAISendEvent</c> (<c>0x45FDA0</c> hands it on).</summary>
    /// <param name="human">The human it reached.</param>
    /// <param name="aiEvent">The event.</param>
    /// <remarks>A human with no brain lets it go.</remarks>
    void SendEvent(HumanTask human, in HumanAiEvent aiEvent)
    {
    }
}

/// <summary>A female soldier — the original's <c>HumanSoldierFemale</c>, registered at <c>0x454970</c>.</summary>
public sealed class HumanSoldierFemaleTask : HumanSoldierTask
{
    /// <summary>Initializes a female soldier with <c>0x454B10</c>'s defaults.</summary>
    public HumanSoldierFemaleTask()
        : base(HumanSoldierClass.FemaleSoldier)
    {
    }
}

/// <summary>An RPG soldier — the original's <c>HumanSoldierRPG</c>, registered at <c>0x456AD0</c>.</summary>
public sealed class HumanSoldierRpgTask : HumanSoldierTask
{
    /// <summary>Initializes an RPG soldier with <c>0x456C70</c>'s defaults.</summary>
    public HumanSoldierRpgTask()
        : base(HumanSoldierClass.RpgSoldier)
    {
    }
}

/// <summary>A pistol soldier — the original's <c>HumanSoldierPistol</c>, registered at <c>0x459880</c>.</summary>
public sealed class HumanSoldierPistolTask : HumanSoldierTask
{
    /// <summary>Initializes a pistol soldier with <c>0x459A20</c>'s defaults.</summary>
    public HumanSoldierPistolTask()
        : base(HumanSoldierClass.PistolSoldier)
    {
    }
}

/// <summary>
/// The player's input source — the original's <c>HumanPlayerInput</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered at <c>0x45DB70</c>, 484 bytes. Its four parameters line up one for one with the fields
/// the update reads: <c>Record</c> is <c>+128</c>, <c>Playback</c> is <c>+129</c>, <c>File</c> names
/// the demo, and the expression is the script-bound condition at <c>+404</c>.
/// </para>
/// </remarks>
public partial class HumanPlayerInputTask : GameTask
{
    /// <summary>Gets or sets whether sampled frames are recorded to <see cref="File"/>.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Record")]
    public bool Record { get; set; }

    /// <summary>Gets or sets whether frames are replayed from <see cref="File"/> instead of sampled.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Playback")]
    public bool Playback { get; set; }

    /// <summary>Gets or sets the demo file recorded to or replayed from.</summary>
    [ScriptParam(ScriptParamType.String256, "File")]
    public string File { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that gates the map computer.</summary>
    [ScriptParam(ScriptParamType.VarString, "Mapcomputer on expression")]
    public string MapComputerOnExpression { get; set; } = string.Empty;

    /// <summary>Gets the frames this task has posted to its parent.</summary>
    public PlayerInputState State { get; } = new();
}

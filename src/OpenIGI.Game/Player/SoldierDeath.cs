using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>
/// Which of the three death families a soldier falls in — <c>Human+3356</c>, written by the damage
/// handler from the category of the weapon that killed.
/// </summary>
/// <remarks>
/// <c>0x45D3C0</c> writes <c>1</c> when the killing weapon's category is <see cref="WeaponCategory.Shotgun"/>
/// and <c>2</c> for everything else; the melee/explosion handler <c>0x45A5E0</c> always writes <c>2</c>.
/// The constructor leaves it at <c>0</c>, which <c>0x45C440</c> treats the same as <c>1</c>.
/// </remarks>
public enum SoldierDeathMode
{
    /// <summary>The constructor's value. Handled with <see cref="Thrown"/>.</summary>
    Untouched = 0,

    /// <summary>A shotgun kill: the body is <b>thrown</b>, forwards or backwards by where the round came from.</summary>
    Thrown = 1,

    /// <summary>Any other kill: the body <b>crumples</b>, and a head or neck shot has its own pair of clips.</summary>
    Crumple = 2,
}

/// <summary>
/// The four clips a class's general death state chooses between.
/// </summary>
/// <param name="ThrownBackwards">A thrown body struck from the front.</param>
/// <param name="ThrownForwards">A thrown body struck from behind.</param>
/// <param name="CrumpleFirst">The crumple clip the coin flip's 0 picks.</param>
/// <param name="CrumpleSecond">The crumple clip the coin flip's 1 picks.</param>
/// <remarks>
/// The soldier's <c>0x45C440</c> and the RPG soldier's <c>0x4567C0</c> are the same function over different
/// literals. The female soldier's death (<c>0x4546C0</c>) has no families at all, and uses
/// <see cref="SoldierDeath.ChooseByZone"/> instead.
/// </remarks>
public sealed record SoldierDeathClips(int ThrownBackwards, int ThrownForwards, int CrumpleFirst, int CrumpleSecond)
{
    /// <summary>Gets the soldier's clips — the literals in <c>0x45C440</c>.</summary>
    public static SoldierDeathClips Soldier { get; } = new(
        SoldierDeath.ThrownBackwardsAnimation,
        SoldierDeath.ThrownForwardsAnimation,
        SoldierDeath.CrumpleFirstAnimation,
        SoldierDeath.CrumpleSecondAnimation);

    /// <summary>Gets the RPG soldier's clips — the literals in <c>0x4567C0</c>.</summary>
    public static SoldierDeathClips RpgSoldier { get; } = new(15, 16, 14, 12);
}

/// <summary>
/// <c>0x45C440</c> — the soldier death state that <b>chooses</b> its own animation, and the turn
/// <c>0x45DAA0</c> makes before it.
/// </summary>
/// <remarks>
/// <para>
/// The soldier class has <b>three</b> death states, entries 32, 33 and 34 of its 38-state machine
/// (<c>off_53EC10</c>), and all three carry animation id <b>0</b> in the state-to-animation table at
/// <c>0x53ED10</c> — which is that table's "this state names its own clip" marker, the same rule the
/// player's table uses. Two of them name a literal (<c>0x45C5A0</c> plays 24, <c>0x45C650</c> plays 25);
/// the third, this one, works the id out.
/// </para>
/// <para>
/// <b>This corrects the port's own <c>SoldierView.DeathAnimations = [24, 25]</c>.</b> Those two literals
/// are real, but they belong to the two states that are <em>not</em> the general one, and neither is a
/// stand-up-to-ground fall: over the retail sets, 24 starts from a crouch (skeleton 1: root Z 2219 → 1020)
/// and 25 is a 15-tick twitch already on the floor (462 → 523). The clips this function names —
/// <b>22, 23, 28, 30 and 71</b> — all start at the standing pelvis height and end on the ground
/// (skeleton 1: 3877 → 524…772). Playing 24 or 25 on a soldier who was standing is exactly what left a
/// corpse upright in mid-air.
/// </para>
/// </remarks>
public static class SoldierDeath
{
    /// <summary>The clip a body thrown <b>backwards</b> falls in — the literal in <c>0x45C440</c>.</summary>
    /// <remarks>Chosen when the round was travelling <em>against</em> the body's forward axis: a front hit.</remarks>
    public const int ThrownBackwardsAnimation = 28;

    /// <summary>The clip a body thrown <b>forwards</b> falls in — the literal in <c>0x45C440</c>.</summary>
    public const int ThrownForwardsAnimation = 71;

    /// <summary>The crumple clip the coin flip's 0 picks.</summary>
    /// <remarks>
    /// The original holds the pair as a two-element stack array <c>{23, 22}</c> indexed by a call to the
    /// engine's own <c>rand(2)</c>. Both exist in every set that has any death clip at all.
    /// </remarks>
    public const int CrumpleFirstAnimation = 23;

    /// <summary>The crumple clip the coin flip's 1 picks.</summary>
    public const int CrumpleSecondAnimation = 22;

    /// <summary>The two clips an ordinary kill crumples in, in the order the coin flip indexes them.</summary>
    public static IReadOnlyList<int> CrumpleAnimations { get; } = [CrumpleFirstAnimation, CrumpleSecondAnimation];

    /// <summary>The head zone, whose death clips come out of the hit-zone table's own row.</summary>
    public const int HeadZone = 15;

    /// <summary>The neck zone, likewise.</summary>
    public const int NeckZone = 16;

    /// <summary>Which mode the damage handler records for a killing weapon.</summary>
    /// <param name="category">The killing weapon's category — <c>weapon+1552</c>.</param>
    /// <returns>The mode.</returns>
    /// <remarks><c>0x45D3C0</c>: shotgun throws, everything else crumples.</remarks>
    public static SoldierDeathMode ModeFor(WeaponCategory category) =>
        category == WeaponCategory.Shotgun ? SoldierDeathMode.Thrown : SoldierDeathMode.Crumple;

    /// <summary>Picks the death animation.</summary>
    /// <param name="clips">The class's four clips.</param>
    /// <param name="mode">The mode the damage handler recorded — <c>Human+3356</c>.</param>
    /// <param name="zone">The zone the killing round struck — <c>Human+744</c>.</param>
    /// <param name="zones">The soldier's hit-zone table, whose head and neck rows name their own clips.</param>
    /// <param name="shotFromTheFront">
    /// What <see cref="TurnToTheShot"/> reported: the round was travelling against the body's forward axis.
    /// </param>
    /// <param name="coinFlip">The engine's <c>rand(2)</c> — 0 or 1.</param>
    /// <param name="scriptOverride">
    /// <c>HumanAI+15056</c>, what <c>AIFunction_SetDeathAnimation</c> wrote, or <c>-1</c>. It wins over
    /// everything, and is tested before anything else is computed.
    /// </param>
    /// <returns>The animation id, or <c>-1</c> when nothing names one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="zones"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The head and neck rows are the confirmation that a hit-zone row's trailing two integers are
    /// <b>animation ids</b>: <c>0x45C440</c> indexes <c>row + 4·flip + 8</c>, which is
    /// <see cref="HumanHitZone.ReactionA"/> and <see cref="HumanHitZone.ReactionB"/> exactly. Retail gives
    /// the head row <c>30, 30</c> and the neck row <c>22, 22</c>, so the flip is a no-op on shipped data
    /// and the mechanism is still worth having.
    /// </remarks>
    public static int Choose(
        SoldierDeathClips clips,
        SoldierDeathMode mode,
        int zone,
        IReadOnlyList<HumanHitZone> zones,
        bool shotFromTheFront,
        int coinFlip,
        int scriptOverride = -1)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(zones);

        if (scriptOverride != -1)
        {
            return scriptOverride;
        }

        int flip = coinFlip & 1;

        if (mode <= SoldierDeathMode.Thrown)
        {
            return shotFromTheFront ? clips.ThrownBackwards : clips.ThrownForwards;
        }

        if (mode != SoldierDeathMode.Crumple)
        {
            // 0x45C440's switch has no arm past 2, and leaves the id at -1: no clip change at all.
            return -1;
        }

        if (zone is HeadZone or NeckZone && zone < zones.Count)
        {
            HumanHitZone row = zones[zone];
            return flip == 0 ? row.ReactionA : row.ReactionB;
        }

        return flip == 0 ? clips.CrumpleFirst : clips.CrumpleSecond;
    }

    /// <summary>
    /// Picks the female soldier's death animation — <c>0x4546C0</c>: the struck zone's first reaction.
    /// </summary>
    /// <param name="zone">The zone the killing round struck — <c>Human+744</c>.</param>
    /// <param name="zones">The class's hit-zone table.</param>
    /// <param name="scriptOverride"><c>HumanAI+15056</c>, or <c>-1</c>. It wins when set.</param>
    /// <returns>The animation id, or <c>-1</c> for a zone outside the table.</returns>
    /// <remarks>No death family, no coin flip and no turn to the shot: she falls where she stands.</remarks>
    public static int ChooseByZone(int zone, IReadOnlyList<HumanHitZone> zones, int scriptOverride = -1)
    {
        ArgumentNullException.ThrowIfNull(zones);

        if (scriptOverride != -1)
        {
            return scriptOverride;
        }

        return zone >= 0 && zone < zones.Count ? zones[zone].ReactionA : -1;
    }

    /// <summary>Whether the general death turns the body to the round before it falls.</summary>
    /// <param name="mode">The mode the damage handler recorded.</param>
    /// <returns><see langword="true"/> for a thrown death.</returns>
    /// <remarks>
    /// <c>0x45C440</c> calls <c>0x45DAA0</c> only in its <c>mode &lt;= 1</c> branch, the one that needs to
    /// know front from back; a crumple keeps the facing the body died with.
    /// </remarks>
    public static bool TurnsToTheShot(SoldierDeathMode mode) => mode <= SoldierDeathMode.Thrown;

    /// <summary>
    /// <c>0x45DAA0</c> — turns the dying body to the round, and reports which way it was struck.
    /// </summary>
    /// <param name="direction">
    /// The round's <b>direction of travel</b>, as the damage handler stored it at <c>Human+3360</c>.
    /// </param>
    /// <param name="forward">
    /// The body's forward axis — <b>column 1</b> of its 3×3, which is what the original reads out of
    /// <c>Human+124</c>, <c>+136</c>, <c>+148</c>.
    /// </param>
    /// <param name="facing">
    /// Receives the new heading in radians about Z. Left untouched when the direction is a zero vector.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the round came at the body's <b>front</b> — which is also what a zero
    /// direction reports.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The body always ends up aligned with the round: struck from the front it turns to <em>face</em> the
    /// shooter (bearing + π), struck from behind it turns to face away. Either way the fall clip then plays
    /// along the round's line, which is what makes 28 and 71 read as backwards and forwards — and the
    /// retail root motion agrees, 28 travelling −6001 units in Y over skeleton 1 and 71 travelling +9933.
    /// </para>
    /// <para>
    /// The zero-vector case returns <see langword="true"/> without touching the heading, so a kill that
    /// carries no direction (a script's, or a fall) uses the backwards clip.
    /// </para>
    /// </remarks>
    public static bool TurnToTheShot(Vector3d direction, Vector3f forward, ref float facing)
    {
        // 0x4B3320, an exact three-way equality rather than a tolerance.
        if (direction is { X: 0.0, Y: 0.0, Z: 0.0 })
        {
            return true;
        }

        double length = Math.Sqrt((direction.X * direction.X)
            + (direction.Y * direction.Y)
            + (direction.Z * direction.Z));
        double nx = direction.X / length;
        double ny = direction.Y / length;
        double nz = direction.Z / length;

        // The bearing the engine's gamma convention wants: forward is (-sin g, cos g), so a direction
        // (x, y) is at gamma = atan2(-x, y). That is 0x414950's acos signed the way it signs it.
        float bearing = (float)Math.Atan2(-nx, ny);

        double alignment = (nz * forward.Z) + (ny * forward.Y) + (nx * forward.X);
        if (alignment <= 0.0)
        {
            // Travelling against the body's forward: a front hit. Turn to face the shooter.
            facing = bearing + MathF.PI;
            return true;
        }

        facing = bearing;
        return false;
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>How a soldier is struck: its collision, the zone a bullet finds, and the damage it takes.</content>
public sealed partial class Igi2HumanSoldier : IIgi2BonedBody, IIgi2WeaponHitTarget, IIgi2WeaponCollisionTarget
{
    /// <summary>The cries a soldier hurt but alive gives, <c>Human_HitSound</c>'s four.</summary>
    public static IReadOnlyList<string> HurtSounds { get; } = ["GV_69_03", "GV_70_03", "GV_71_03", "GV_72_03"];

    /// <summary>The lines a soldier's mind says when it is hit, one drawn.</summary>
    public static IReadOnlyList<string> HitLines { get; } = ["ai_death_1", "ai_death_2", "ai_death_3", "ai_death_4"];

    /// <summary>How far below which a hit by the groin's mark is to the groin, <c>614.4</c>, a double constant.</summary>
    public const double GroinReach = 614.4;

    private Igi2BodyPose? _pose;
    private bool _isPoseStale = true;

    /// <summary>Gets or sets what the soldier's model gives its body: bone names, collision, damage zones.</summary>
    public Igi2SoldierModel? Model { get; set; }

    /// <summary>Gets or sets what poses the body for its collision and its drawing.</summary>
    public Igi2BodyPoser? Poser { get; set; }

    /// <summary>Gets or sets the soldier's collider in the level's collision world, or <see langword="null"/> when not placed in one.</summary>
    public Igi2BoneCollider? Collider { get; set; }

    /// <summary>Gets or sets which way the killing shot travelled, <c>Human_s.tDeathDirection</c> (<c>+0x7EC</c>); a death normalises it.</summary>
    public Vector3f DeathDirection { get; set; }

    /// <summary>
    /// Gets or sets how the soldier was killed, <c>eDeathByType</c> (<c>+0xBE8</c>): 2 by a gun, 3 by a pistol, 1 by a shotgun and 4
    /// by anything else.
    /// </summary>
    public int DeathByType { get; set; }

    /// <summary>Gets or sets a value indicating whether the soldier has played its long flinch, <c>isLongHitAnimPlayed</c> (<c>+0xBDC</c>).</summary>
    public bool IsLongHitAnimPlayed { get; set; }

    /// <summary>Gets the pose the body last stood in, which its collision is traced against and which it is drawn in.</summary>
    /// <returns>The pose, or <see langword="null"/> when the body has none.</returns>
    public Igi2BodyPose? BodyPose()
    {
        if (_isPoseStale)
        {
            _pose = Poser is { } poser && Model is { } model && Animation is Igi2HumanAnimation animation
                ? poser.Pose(animation, model.BoneNames)
                : null;
            _isPoseStale = false;
        }

        return _pose;
    }

    /// <inheritdoc/>
    public BonePose? CurrentPose() => BodyPose()?.Pose;

    /// <inheritdoc/>
    public Igi2BoneMesh? BoneMesh(int level) => Model?.Mesh(level);

    /// <summary>
    /// Readies the soldier's damage after load, <c>HumanSoldier_UpdateInternalDataAfterLoadHandler</c> (<c>igi2 0x4EB190</c>): a
    /// limit of one, every zone healed with its AI type's multiplier, no zone hit yet.
    /// </summary>
    /// <param name="multipliers">The AI type's damage multipliers, one per zone (its base config row's).</param>
    /// <remarks>Its <c>HumanAI</c>'s own after-load runs next, its child, and sets the limit from the tuning (<see cref="ApplyTweaks"/>).</remarks>
    public void AfterLoad(IReadOnlyList<float> multipliers)
    {
        ArgumentNullException.ThrowIfNull(multipliers);
        Damage.MaxDamage = 1f;
        Damage.Clear(multipliers);
        Damage.LastPart = Igi2DamagePart.None;
    }

    /// <summary>Takes the limit from the AI's tuning, as <c>HumanAIConfig_SetAISettingsFromConfig</c> (<c>igi2 0x41ED66</c>) writes it.</summary>
    /// <param name="tweaks">The soldier's tuning at the session's difficulty.</param>
    public void ApplyTweaks(Igi2AiTweaks tweaks)
    {
        ArgumentNullException.ThrowIfNull(tweaks);
        Damage.MaxDamage = tweaks.HitPoints;
    }

    /// <summary>
    /// Whether a bullet may strike the soldier, <c>Human_WeaponCollisionEvaluateHandler</c> (<c>igi2 0x4FF4E0</c>) in single player for
    /// a human that is not the player: never its own or one it is ignored by, nor one fired by its own side.
    /// </summary>
    /// <param name="query">The trace's context.</param>
    /// <returns>Whether it may.</returns>
    public bool EvaluateWeaponCollision(in Igi2WeaponCollisionQuery query) =>
        !ReferenceEquals(query.Shooter, this)
        && !ReferenceEquals(query.Ignore, this)
        && !(query.Shooter is Igi2Human shooter && shooter.Team == Team);

    /// <summary>A bullet striking the soldier, <c>HumanSoldier_WeaponHitHandler</c> (<c>igi2 0x4EB290</c>).</summary>
    /// <param name="hit">The hit.</param>
    /// <remarks>
    /// <para>
    /// A dead soldier takes nothing. A live one takes the hit unless its mind keeps harm off it or its own side fired; then the zone
    /// is found (<see cref="DamagePartOf"/>) and the damage is the hit's times the zone's multiplier, except a knife's, and a head or
    /// groin hit where the mind keeps instant death off. The zone takes it, the total takes it, and it counts toward a flinch.
    /// </para>
    /// <para>
    /// At the limit the soldier dies: its friends hear of it, and unless one hit took it all a death cry is raised; how and which way
    /// it died are kept and the dead bit is raised. Short of it the soldier flinches and cries out, and the AI hears it take damage.
    /// Unless one hit killed it, its mind says a line.
    /// </para>
    /// <para>Not yet: the player's tally of hits and kills, and the bruise and flesh effects.</para>
    /// </remarks>
    public void WeaponHit(in Igi2WeaponHit hit)
    {
        if (IsDead)
        {
            return;
        }

        bool instantDeathDisabled = false;
        if (Mind is { } mind)
        {
            instantDeathDisabled = mind.IsInstantDeathDisabled;
            bool friendly = hit.Shooter is Igi2Human shooter && shooter.Team == Team;
            if (friendly || mind.IsInvulnerable)
            {
                return;
            }
        }

        bool isPlaySound = true;
        Igi2DamagePart part = DamagePartOf(hit);
        Damage.LastPart = part;
        float damage = hit.Damage;
        if (!(part is Igi2DamagePart.Groin or Igi2DamagePart.Head && instantDeathDisabled)
            && hit.WeaponType.WeaponType != Igi2WeaponTypeKind.Knife)
        {
            damage = ((Fpu)damage * Damage.Multiplier(part)).F;
        }

        Damage.Add(part, damage);
        RecoilMeter = ((Fpu)damage + RecoilMeter).F;
        Vector3d? shooterAt = hit.Shooter is Igi2Human from ? from.Position : null;
        if (Damage.IsFatal)
        {
            Vector3f towardShooter = shooterAt is { } s ? Toward(s) : default;
            SendAiEvent(new Igi2AiEvent(Igi2AiEvent.FriendlyDying, Team, 419, 122880f, Position, towardShooter, this));
            if (damage >= Damage.MaxDamage)
            {
                isPlaySound = false;
            }
            else
            {
                SendAiEvent(new Igi2AiEvent(Igi2AiEvent.DeathCry, -1, 1314, 20480f, Position, towardShooter, this));
            }

            DeathDirection = hit.ShotVector;
            DeathDamage = hit.Damage;
            DeathByType = hit.WeaponType.WeaponType switch
            {
                Igi2WeaponTypeKind.Gun => 2,
                Igi2WeaponTypeKind.Pistol => 3,
                Igi2WeaponTypeKind.Shotgun => 1,
                _ => 4,
            };
            Signal |= Igi2SoldierSignal.Dead;
            IsDead = true;
        }
        else
        {
            Signal |= Igi2SoldierSignal.Hit;
            PlayHitSound(HurtSounds);
            Vector3d at = hit.Intersection.Position;
            Vector3f direction = Toward(shooterAt ?? at);
            SendAiEvent(new Igi2AiEvent(Igi2AiEvent.TakingDamage, Team, 34, 20480f, at, direction, this));
            SendAiEvent(new Igi2AiEvent(Igi2AiEvent.FriendlyDamageSeen, Team, 419, 122880f, at, direction, this));
            SendAiEvent(new Igi2AiEvent(Igi2AiEvent.WoundedCry, Team, 1314, 61440f, at, direction, this));
        }

        if (isPlaySound)
        {
            Say(HitLines);
        }
    }

    /// <summary>
    /// The zone a hit lands on, <c>HumanDamage_GetDamagePartFromWeaponHit</c> (<c>igi2 0x469980</c>): the struck bone's zone; a hit
    /// to the upper body is to the back or the chest when the shot came mostly from behind or the front; and any hit taken from
    /// the front within <see cref="GroinReach"/> of the groin's mark is to the groin.
    /// </summary>
    /// <param name="hit">The hit.</param>
    /// <returns>The zone.</returns>
    public Igi2DamagePart DamagePartOf(in Igi2WeaponHit hit)
    {
        Matrix3f o = Orientation;
        Vector3f shot = hit.ShotVector;
        Fpu bx = -(Fpu)shot.X, by = -(Fpu)shot.Y, bz = -(Fpu)shot.Z;
        float across = (((bz * o.M31) + (by * o.M21)) + (bx * o.M11)).F;
        float ahead = (((bz * o.M32) + (by * o.M22)) + (bx * o.M12)).F;

        int bone = hit.Intersection.Sector;
        IReadOnlyList<Igi2DamagePart>? parts = Model?.DamageParts;
        Igi2DamagePart part = parts is not null && (uint)bone < (uint)parts.Count ? parts[bone] : Igi2DamagePart.RightFoot;
        if (part == Igi2DamagePart.UpperBody)
        {
            if (ahead <= 0f && Math.Abs(ahead) > Math.Abs(across))
            {
                part = Igi2DamagePart.Back;
            }

            if (ahead < 0f)
            {
                return part;
            }

            if (Math.Abs(ahead) > Math.Abs(across))
            {
                part = Igi2DamagePart.Chest;
            }
        }

        if (ahead < 0f || Model is not { } model)
        {
            return part;
        }

        Vector3d mark = model.GroinPosition(CurrentPose(), o, Position);
        Vector3d struck = hit.Intersection.Position;
        Fpu dx = (Fpu)struck.X - mark.X, dy = (Fpu)struck.Y - mark.Y, dz = (Fpu)struck.Z - mark.Z;
        Fpu reach = Fpu.Sqrt(((dy * dy) + (dz * dz)) + (dx * dx));
        return reach.Value < GroinReach ? Igi2DamagePart.Groin : part;
    }

    /// <summary>The body's pose is to be taken again: its clips have stepped or changed.</summary>
    internal void TouchPose() => _isPoseStale = true;

    /// <summary>
    /// The mind's line on a hit, <c>HumanAI_PlayRandomSoundAtPosition</c> (<c>igi2 0x464680</c>): one drawn whatever then happens,
    /// said only while the soldier lives (<c>eLifeState</c> nought).
    /// </summary>
    private void Say(IReadOnlyList<string> lines)
    {
        uint draw = Random.NextRaw();
        if (Mind is { } mind && LifeState == 0)
        {
            mind.Say(this, lines[(int)(draw % (uint)lines.Count)]);
        }
    }

    private void SendAiEvent(in Igi2AiEvent aiEvent) => Surroundings.SendAiEvent(aiEvent);

    /// <summary>A point less the soldier's position, as the event's direction stores it.</summary>
    private Vector3f Toward(Vector3d point) =>
        new(((Fpu)point.X - Position.X).F, ((Fpu)point.Y - Position.Y).F, ((Fpu)point.Z - Position.Z).F);
}

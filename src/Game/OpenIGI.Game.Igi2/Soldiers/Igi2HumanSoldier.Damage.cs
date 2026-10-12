using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>How a soldier is struck: its collision, the zone a bullet finds, and the damage it takes.</content>
public sealed partial class Igi2HumanSoldier : IIgi2WeaponHitTarget, IIgi2ExplosionTarget
{
    /// <summary>How far a blast at full share tilts a soldier's view, <c>0.02617994</c> radians.</summary>
    private const float SoldierShakeAngle = 0.02617994f;
    /// <summary>The cries a soldier hurt but alive gives, <c>Human_HitSound</c>'s four.</summary>
    public static IReadOnlyList<string> HurtSounds { get; } = ["GV_69_03", "GV_70_03", "GV_71_03", "GV_72_03"];

    /// <summary>The lines a soldier's mind says when it is hit, one drawn.</summary>
    public static IReadOnlyList<string> HitLines { get; } = ["ai_death_1", "ai_death_2", "ai_death_3", "ai_death_4"];

    /// <summary>Gets or sets which way the killing shot travelled, <c>Human_s.tDeathDirection</c> (<c>+0x7EC</c>); a death normalises it.</summary>
    public Vector3f DeathDirection { get; set; }

    /// <summary>
    /// Gets or sets how the soldier was killed, <c>eDeathByType</c> (<c>+0xBE8</c>): 2 by a gun, 3 by a pistol, 1 by a shotgun and 4
    /// by anything else.
    /// </summary>
    public int DeathByType { get; set; }

    /// <summary>Gets or sets a value indicating whether the soldier has played its long flinch, <c>isLongHitAnimPlayed</c> (<c>+0xBDC</c>).</summary>
    public bool IsLongHitAnimPlayed { get; set; }

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

    /// <summary>A bullet striking the soldier, <c>HumanSoldier_WeaponHitHandler</c> (<c>igi2 0x4EB290</c>).</summary>
    /// <param name="hit">The hit.</param>
    /// <remarks>
    /// <para>
    /// A dead soldier takes nothing. A live one takes the hit unless its mind keeps harm off it or its own side fired; then the zone
    /// is found (<see cref="Igi2Human.DamagePartOf"/>) and the damage is the hit's times the zone's multiplier, except a knife's, and a head or
    /// groin hit where the mind keeps instant death off. The zone takes it, the total takes it, and it counts toward a flinch.
    /// </para>
    /// <para>
    /// At the limit the soldier dies: its friends hear of it, and unless one hit took it all a death cry is raised; how and which way
    /// it died are kept and the dead bit is raised. Short of it the soldier flinches and cries out, and the AI hears it take damage.
    /// Unless one hit killed it, its mind says a line.
    /// </para>
    /// <para>Blood is thrown once the damage is taken (<see cref="Igi2Human.CreateFleshEnterEffect"/>). Not yet: the player's tally of hits and kills, and the knife's bruise.</para>
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
        CreateFleshEnterEffect(hit.ShotVector, hit.Intersection.Position, this);
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
    /// A blast reaching the soldier, <c>HumanSoldier_ExplosionHitHandler</c> (<c>igi2 0x4EB8E0</c>): none while its mind keeps harm off
    /// it or a human of its own side set it off; otherwise its view shakes with the blast's nearness to its eyes, and the blast lands
    /// on its chest at the chest's multiplier, measured from its eyes. It counts toward a flinch; at the limit the soldier dies of it
    /// (how it died nought, the shot's direction from the blast), else it cries out and flinches.
    /// </summary>
    /// <param name="hit">The blast.</param>
    /// <remarks>
    /// The soldier's size for the blast is a third of its model's radius and its root's distance from its origin, the root as the
    /// animation last left it. Not yet: the player's tally of kills, and the soldier's <c>isActive</c> (<c>Human+0x4A5</c>), taken
    /// as set.
    /// </remarks>
    public void ExplosionHit(in Igi2ExplosionHit hit)
    {
        if (Mind is { IsInvulnerable: true } || (hit.Owner is Igi2Human && hit.Team == Team))
        {
            return;
        }

        Vector3d eyes = View.Position;
        ShakeView(hit, SoldierShakeAngle);
        Damage.LastPart = Igi2DamagePart.Chest;
        Vector3f root = RootTranslation;
        float radius = ((Fpu.Sqrt((((Fpu)root.X * root.X) + ((Fpu)root.Y * root.Y)) + ((Fpu)root.Z * root.Z)) + ModelRadius) * 0.33333334f).F;
        Fpu blast = hit.Resolve(eyes, radius);
        if (blast.Value == 0.0)
        {
            return;
        }

        float damage = (blast * Damage.Multiplier(Igi2DamagePart.Chest)).F;
        Damage.Add(Igi2DamagePart.Chest, damage);
        RecoilMeter = ((Fpu)damage + RecoilMeter).F;
        if (Damage.IsFatal)
        {
            DeathByType = 0;
            DeathDirection = new Vector3f(
                ((Fpu)Position.X - hit.Position.X).F, ((Fpu)Position.Y - hit.Position.Y).F, ((Fpu)Position.Z - hit.Position.Z).F);
            DeathDamage = hit.Damage;
            IsDead = true;
            Signal |= Igi2SoldierSignal.Dead;
        }
        else
        {
            PlayHitSound(HurtSounds);
            Signal |= Igi2SoldierSignal.Hit;
        }
    }

    /// <summary>
    /// A blow in close combat, <c>HumanSoldier_CloseCombatDamageHandler</c> (<c>igi2 0x4EBCD0</c>): to the chest when the soldier faces
    /// its attacker, else to the back, at that zone's multiplier. At the limit the soldier dies of it (how it died 4, the direction
    /// from the attacker); else it cries out and flinches. Unless its mind keeps harm off it.
    /// </summary>
    /// <param name="damage">The blow's damage.</param>
    /// <param name="attacker">Who struck.</param>
    /// <remarks>
    /// Harmed or not, its friends hear a wounded cry from it, and its own mind is told the same (event 31, team 1, the attacker its
    /// reference and originator); the event's direction is left as the stack held it, so none is given.
    /// </remarks>
    public void CloseCombatDamage(float damage, Igi2Human attacker)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        if (Mind is not { IsInvulnerable: true })
        {
            Matrix3f o = Orientation;
            Fpu fx = (((Fpu)o.M13 + o.M11) * 0.0) + ((Fpu)o.M12 * 4096.0);
            Fpu fy = (((Fpu)o.M23 + o.M21) * 0.0) + ((Fpu)o.M22 * 4096.0);
            Fpu fz = (((Fpu)o.M33 + o.M31) * 0.0) + ((Fpu)o.M32 * 4096.0);
            Fpu dx = (Fpu)Position.X - attacker.Position.X;
            Fpu dy = (Fpu)Position.Y - attacker.Position.Y;
            Fpu dz = (Fpu)Position.Z - attacker.Position.Z;
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((dy * dy) + (dz * dz)) + (dx * dx));
            Fpu nx = inverse * dx, ny = dy * inverse, nz = inverse * dz;
            Igi2DamagePart part = (((fz * nz) + (ny * fy)) + (nx * fx)).Value < 0.0 ? Igi2DamagePart.Chest : Igi2DamagePart.Back;
            Damage.LastPart = part;
            float taken = ((Fpu)Damage.Multiplier(part) * damage).F;
            Damage.Add(part, taken);
            RecoilMeter = ((Fpu)taken + RecoilMeter).F;
            if (Damage.IsFatal)
            {
                DeathByType = 4;
                DeathDirection = new Vector3f(nx.F, ny.F, nz.F);
                DeathDamage = damage;
                IsDead = true;
                Signal |= Igi2SoldierSignal.Dead;
            }
            else
            {
                PlayHitSound(HurtSounds);
                Signal |= Igi2SoldierSignal.Hit;
            }
        }

        Igi2AiEvent cry = new(Igi2AiEvent.WoundedCry, 1, 1314, 61440f, Position, default, attacker, attacker);
        SendAiEvent(cry);
        Mind?.Perceive(this, cry);
    }

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

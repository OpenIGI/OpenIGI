using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>When the player last heard a bullet go by, <c>iLastWhizzbySoundTick</c>: one for the whole run of the game.</summary>
public sealed class Igi2WhizzbyClock
{
    /// <summary>Gets or sets the level tick the last pass was heard at; nought until one is.</summary>
    public int LastTick { get; set; }
}

/// <content>The player struck by a bullet, and the bullets it hears go by.</content>
public sealed partial class Igi2HumanPlayer : IIgi2WeaponHitTarget
{
    /// <summary>The cries the player gives when hurt, <c>HumanPlayer_HandleDamage</c>'s three.</summary>
    public static IReadOnlyList<string> HurtSounds { get; } = ["player_hit_1", "player_hit_2", "player_hit_3"];

    /// <summary>The ticks within which another passing bullet is heard only half the time, <c>15</c>.</summary>
    public const int WhizzbyInterval = 15;

    /// <summary>How many passing-bullet sounds there are to draw from, <c>whizzby_00</c> to <c>whizzby_11</c>.</summary>
    public const int WhizzbySounds = 12;

    /// <summary>How hard a bullet pushes the player along its line, <c>163.08148</c> (the float <c>0x432314DC</c>).</summary>
    public const float BulletPush = 163.08148f;

    /// <summary>How long a bullet's shake runs, the float <c>0x3D888889</c>: a fifteenth of it a tick.</summary>
    private static readonly float BulletShakeTime = BitConverter.Int32BitsToSingle(0x3D888889);

    /// <inheritdoc/>
    protected override bool IsThePlayer => true;

    /// <inheritdoc/>
    /// <remarks>The player's states 1 and 24, turning on the spot standing or crouched.</remarks>
    protected override bool IsTurningOnTheSpot =>
        Locomotion.CurrentState is Igi2PlayerState.StandTurn or Igi2PlayerState.CrouchTurn;

    /// <summary>Gets or sets when a passing bullet was last heard, shared for the whole run of the game.</summary>
    public Igi2WhizzbyClock WhizzbyClock { get; set; } = new();

    /// <summary>Gets or sets the level's tick, <c>LevelFlow_ptLevelFlow-&gt;nTick</c>.</summary>
    public Func<int> LevelTick { get; set; } = static () => 0;

    /// <summary>Gets the one whose shot killed the player, <c>ptKillerQTaskRef</c>, or <see langword="null"/>.</summary>
    public object? Killer { get; private set; }

    /// <summary>
    /// A bullet striking the player, <c>HumanPlayer_WeaponHitHandler</c> (<c>igi2 0x4BA0A0</c>) in single player: the zoom's
    /// alternate fire left, the damage eased on the easiest difficulty and sharpened on the hardest, the zone found, the damage
    /// handled (<see cref="HandleDamage"/>); a killing shot from someone records its shooter.
    /// </summary>
    /// <param name="hit">The hit.</param>
    /// <remarks>
    /// The original only answers while the player is active, which a player always is here. Not yet: the flesh effect at the hit
    /// (<c>HumanDamage_CreateFleshEnterEffect</c>, <c>0x469CE0</c>).
    /// </remarks>
    public void WeaponHit(in Igi2WeaponHit hit)
    {
        Weapon.LeaveZoomAlternateFireMode();
        Fpu damage = hit.Damage;
        damage = Difficulty switch
        {
            AiDifficulty.Easy => damage * 0.75f,
            AiDifficulty.Hard => damage * 1.25f,
            _ => damage,
        };
        HandleDamage(DamagePartOf(hit), damage.F, hit.ShotVector);
        if (Damage.IsFatal && Killer is null && hit.Shooter is { } shooter)
        {
            Killer = shooter;
        }
    }

    /// <summary>
    /// Damage done to the player, <c>HumanPlayer_HandleDamage</c> (<c>igi2 0x4B9AC0</c>): the zone takes the hit at its multiplier and
    /// the player flinches; alive, it cries out. The view shakes by a draw held between a quarter and one; the screen flashes red,
    /// brighter on the side the shot came from in the view's frame; and the body is pushed along the shot.
    /// </summary>
    /// <param name="part">The zone hit.</param>
    /// <param name="hitDamage">The hit's damage.</param>
    /// <param name="direction">Which way the shot travelled; nought for none, which flashes the screen evenly and pushes nothing.</param>
    public void HandleDamage(Igi2DamagePart part, float hitDamage, Vector3f direction)
    {
        Damage.LastPart = part;
        Damage.Add(part, ((Fpu)hitDamage * Damage.Multiplier(part)).F);
        Signal |= Igi2PlayerSignal.Hit;
        if (!IsDead)
        {
            PlayHitSound(HurtSounds);
        }

        double shake = Random.NextDouble();
        shake = shake < 0.25 ? 0.25 : shake > 1.0 ? 1.0 : shake;
        View.ShakeFactor = 1f;
        View.ShakeTime = BulletShakeTime;
        View.ShakeRange = ((Fpu)204.8f * shake).F;
        View.ShakeAngle = ((Fpu)shake * 0.01308997f).F;

        bool none = direction.X == 0f && direction.Y == 0f && direction.Z == 0f;
        if (none)
        {
            float even = (((Fpu)Random.NextDouble() + 1.0) * 0.4).F;
            Flash(even, even, even, even);
            return;
        }

        FlashFrom(direction);
        if (!IsDisableDeltaTranslationScale)
        {
            Push(direction);
        }
    }

    /// <summary>The red flash brighter on the side the shot came from, one quadrant pattern per strongest axis in the view's frame.</summary>
    private void FlashFrom(Vector3f d)
    {
        Fpu f = ((Fpu)Random.NextDouble() + 1.0) * 0.4;
        Matrix3f o = View.Orientation;
        float tx = ((((Fpu)o.M31 * d.Z) + ((Fpu)o.M21 * d.Y)) + ((Fpu)o.M11 * d.X)).F;
        float ty = ((((Fpu)o.M32 * d.Z) + ((Fpu)o.M22 * d.Y)) + ((Fpu)o.M12 * d.X)).F;
        Fpu tz = (((Fpu)o.M33 * d.Z) + ((Fpu)o.M23 * d.Y)) + ((Fpu)o.M13 * d.X);
        float full = f.F, quarter = (f * 0.25f).F;
        float ax = MathF.Abs(tx), ay = MathF.Abs(ty);
        double az = Math.Abs(tz.Value);
        if (ax > ay && ax > az)
        {
            if (tx > 0f)
            {
                Flash(full, full, quarter, quarter);
            }
            else
            {
                Flash(quarter, quarter, full, full);
            }
        }
        else if (!(ay < ax) && !(ay < az))
        {
            if (ty > 0f)
            {
                Flash(quarter, full, full, quarter);
            }
            else
            {
                Flash(full, full, full, full);
            }
        }
        else if (tz.Value > 0.0)
        {
            Flash(quarter, full, full, quarter);
        }
        else
        {
            Flash(full, quarter, quarter, full);
        }
    }

    private void Flash(float a, float b, float c, float d)
    {
        _damageFlash[0] = a;
        _damageFlash[1] = b;
        _damageFlash[2] = c;
        _damageFlash[3] = d;
    }

    /// <summary>The body pushed along the shot's line on the ground, its impact speed taking the unit direction's share.</summary>
    private void Push(Vector3f d)
    {
        Fpu x = d.X;
        float y = d.Y;
        if (d.X != 0f || d.Y != 0f || d.Z != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)d.X * d.X) + ((Fpu)d.Y * d.Y)) + ((Fpu)d.Z * d.Z));
            x = (Fpu)d.X * inverse;
            y = ((Fpu)d.Y * inverse).F;
        }

        Vector3f impact = ImpactSpeed;
        ImpactSpeed = new Vector3f(((x * BulletPush) + impact.X).F, (((Fpu)y * BulletPush) + impact.Y).F, impact.Z);
    }

    /// <summary>
    /// A bullet passing the player, the end of <c>Human_WeaponCollisionEvaluateHandler</c> (<c>igi2 0x4FF4E0</c>): one the player fired
    /// or is ignored by is not heard. Within fifteen level ticks of the last, only half the time. Heard, it is one of twelve
    /// <c>whizzby_</c> sounds drawn, at the view.
    /// </summary>
    /// <param name="query">The trace's context.</param>
    protected override void HearBulletPass(in Igi2WeaponCollisionQuery query)
    {
        if (ReferenceEquals(query.Ignore, this) || ReferenceEquals(query.Shooter, this))
        {
            return;
        }

        int tick = LevelTick();
        if (tick - WhizzbyClock.LastTick < WhizzbyInterval && Random.NextDouble() < 0.5)
        {
            return;
        }

        WhizzbyClock.LastTick = tick;
        uint pick = Random.NextRaw() % WhizzbySounds;
        Surroundings.PlaySound("whizzby_" + pick.ToString("00", CultureInfo.InvariantCulture));
    }
}

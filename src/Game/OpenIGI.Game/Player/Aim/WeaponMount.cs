using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player.Aim;

/// <summary>How far the look may turn while the weapon is braced: a sector about the heading it was braced at.</summary>
/// <param name="Yaw">The heading the weapon was braced at, radians.</param>
/// <param name="YawHalfWidth">How far either side of it the look may turn, radians.</param>
/// <param name="MinimumPitch">The lowest the look may go, radians.</param>
/// <param name="MaximumPitch">The highest, radians.</param>
public readonly record struct MountLookLimit(float Yaw, float YawHalfWidth, float MinimumPitch, float MaximumPitch)
{
    /// <summary>Holds a look inside the sector.</summary>
    /// <param name="yaw">The heading the look would take.</param>
    /// <param name="pitch">The pitch it would take.</param>
    /// <returns>The heading and pitch held inside it.</returns>
    public (float Yaw, float Pitch) Clamp(float yaw, float pitch)
    {
        float off = Interpolation.LerpAngle(0f, yaw - Yaw, 1f);
        float held = Math.Clamp(off, -YawHalfWidth, YawHalfWidth);
        return (yaw + (held - off), Math.Clamp(pitch, MinimumPitch, MaximumPitch));
    }
}

/// <summary>
/// Modern aim's brace: the weapon laid on a top or set against a corner, steadier and harder-kicking than held. <b>Not
/// retail.</b>
/// </summary>
/// <remarks>
/// <para>
/// Braced on a top, the eye rises or bends to lay the sights just over it (<see cref="MountSurface.Raise"/>) -- the peek over
/// a crate from a crouch, or the bend to a low sill -- and the look is held to a sector in front. At a corner the eye leans out
/// into the open side. The body stays where it is; a step, a jump or a crouch lets go.
/// </para>
/// <para>
/// Stepped once a tick, so a server and its client agree on it; the eye's rise and the brace's own settle run linearly over
/// <see cref="SettleSeconds"/> and are eased where they are drawn.
/// </para>
/// </remarks>
public sealed class WeaponMount
{
    /// <summary>How long the weapon takes to settle onto its brace, and the eye to its height, in seconds.</summary>
    public const float SettleSeconds = 0.18f;

    /// <summary>How far either side of the braced heading the look may turn, in degrees.</summary>
    public const float YawHalfWidthDegrees = 60f;

    /// <summary>What a brace makes of a round's kick in the view.</summary>
    public const float RecoilScale = 0.45f;

    /// <summary>What a brace makes of a round's cone.</summary>
    public const float SpreadScale = 0.8f;

    private const float Degrees = MathF.PI / 180f;
    private float _raiseTarget;

    /// <summary>Gets how the weapon is braced, or <see cref="MountKind.None"/>.</summary>
    public MountKind Kind { get; private set; }

    /// <summary>Gets a value indicating whether the weapon is braced.</summary>
    public bool IsMounted => Kind != MountKind.None;

    /// <summary>Gets the profile of the weapon that was braced.</summary>
    public AdsProfile? Profile { get; private set; }

    /// <summary>Gets the sector the look is held to while braced.</summary>
    public MountLookLimit Limit { get; private set; }

    /// <summary>Gets how far the eye has risen (negative: dropped) to the brace, in world units, as of the last tick.</summary>
    public float Raise { get; private set; }

    /// <summary>Gets <see cref="Raise"/> as of the tick before.</summary>
    public float PreviousRaise { get; private set; }

    /// <summary>Gets the corner's open side for the lean: minus one (left), one (right), or nought for a top.</summary>
    public float CornerSide => Kind switch
    {
        MountKind.CornerLeft => -1f,
        MountKind.CornerRight => 1f,
        _ => 0f,
    };

    /// <summary>Gets how many times a weapon has been braced, which a new brace moves on: the settle's cue.</summary>
    public int Sequence { get; private set; }

    /// <summary>Braces the weapon.</summary>
    /// <param name="surface">What it is braced on.</param>
    /// <param name="profile">The weapon's profile.</param>
    /// <param name="yaw">The heading it is braced at.</param>
    public void Mount(MountSurface surface, AdsProfile profile, float yaw)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Kind = surface.Kind;
        Profile = profile;
        _raiseTarget = surface.Raise;
        Limit = new MountLookLimit(yaw, YawHalfWidthDegrees * Degrees, -30f * Degrees, 45f * Degrees);
        unchecked
        {
            Sequence++;
        }
    }

    /// <summary>Lets go of the brace; the eye comes back to the body's over the settle.</summary>
    public void Dismount()
    {
        Kind = MountKind.None;
        Profile = null;
        _raiseTarget = 0f;
    }

    /// <summary>Lets go at once, the eye back where the body's is: a death, a teleport, modern aim switched off.</summary>
    public void Reset()
    {
        Dismount();
        Raise = 0f;
        PreviousRaise = 0f;
    }

    /// <summary>Steps the eye's rise one tick towards the brace's.</summary>
    public void Tick()
    {
        PreviousRaise = Raise;
        float step = MountProbe.MaxDropMetres * (float)WorldScene.UnitsPerMetre * AimDownSights.TickSeconds / SettleSeconds;
        Raise = Raise < _raiseTarget ? MathF.Min(_raiseTarget, Raise + step) : MathF.Max(_raiseTarget, Raise - step);
    }

    /// <summary>What the brace does to the eye this tick.</summary>
    public EyeLean Eye => new(0f, Raise, 0f);
}

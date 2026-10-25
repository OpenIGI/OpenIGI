using OpenIGI.Game.Player;
using OpenIGI.UI.Animation;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>
/// The remastered crosshair's motion between frames: how far it stands open, how red it is over an enemy, and the marks a hit
/// and a kill leave, each timed from when it happened. Driven by the frame's clock, so a capture plays it the same each run.
/// </summary>
public sealed class HudCrosshairState
{
    /// <summary>How long a hit's mark shows, in seconds, before it blinks off.</summary>
    public const float HitSeconds = 0.4f;

    /// <summary>How long a kill's mark holds, in seconds.</summary>
    public const float KillSeconds = 0.7f;

    /// <summary>The scale a hit's mark shows at, of a mark's full size.</summary>
    public const float HitScale = 0.5f;

    /// <summary>The scale a kill's mark holds at.</summary>
    public const float KillHeldScale = 0.75f;

    /// <summary>The scale a kill's mark punches out to when another kill lands while it shows, easing back to its held scale.</summary>
    public const float KillPunchScale = 1.1f;

    /// <summary>How much each round opens the crosshair beyond the weapon's spread, in radians: about two-thirds of a degree.</summary>
    public const float KickPerShot = 0.012f;

    /// <summary>The most a run of rounds opens it beyond the spread, in radians.</summary>
    public const float MaximumKick = 0.05f;

    /// <summary>How fast the kick settles, a share lost each second.</summary>
    public const float KickSettle = 10f;

    private readonly Spring _spread = new(0f, 0.07f);
    private readonly Spring _enemy = new(0f, 0.07f);
    private CrosshairShape _shape = (CrosshairShape)(-1);
    private long _shots;
    private int _hits;
    private int _kills;
    private float _kick;
    private float _hitAt = float.NegativeInfinity;
    private float _killAt = float.NegativeInfinity;
    private bool _punch;
    private float _seconds;

    /// <summary>Gets the crosshair in hand.</summary>
    public CrosshairShape Shape => _shape < 0 ? CrosshairShape.None : _shape;

    /// <summary>Gets how far the crosshair stands open, in radians: the weapon's spread, eased, and the rounds' kick on top.</summary>
    public float Spread => _spread.Value + _kick;

    /// <summary>Gets how far it has turned to the enemy colour, nought to one.</summary>
    public float Enemy => _enemy.Value;

    /// <summary>Gets what is left of the last hit's mark, one as it lands to nought.</summary>
    public float Hit => Remaining(_hitAt, HitSeconds);

    /// <summary>Gets a value indicating whether the last hit was to the head.</summary>
    public bool HitHead { get; private set; }

    /// <summary>Gets what is left of the last kill's mark, one as it lands to nought.</summary>
    public float Kill => Remaining(_killAt, KillSeconds);

    /// <summary>Gets a value indicating whether the last kill was by a round to the head.</summary>
    public bool KillHead { get; private set; }

    /// <summary>Gets a value indicating whether a kill's mark shows.</summary>
    public bool KillShowing => Kill > 0f;

    /// <summary>Gets a value indicating whether a hit's mark shows: never over a kill's.</summary>
    public bool HitShowing => Hit > 0f && !KillShowing;

    /// <summary>Gets the kill's mark's scale: held, or easing back from a punch over its whole time.</summary>
    public float KillScale => _punch ? KillPunchScale - ((KillPunchScale - KillHeldScale) * (1f - Kill)) : KillHeldScale;

    /// <summary>Steps the crosshair to the frame's aim.</summary>
    /// <param name="aim">What the player's aim and rounds are doing.</param>
    /// <param name="seconds">The frame's clock.</param>
    /// <param name="elapsed">The seconds since the last frame.</param>
    /// <remarks>
    /// A new weapon starts at its own spread with no kick, and its counts are taken as they stand, so taking a weapon in hand
    /// marks nothing. A count that rises marks its event; one that falls (a mission begun again) is taken as it stands.
    /// </remarks>
    public void Update(in PlayerAimFeedback aim, float seconds, float elapsed)
    {
        _seconds = seconds;
        if (aim.Shape != _shape)
        {
            bool first = _shape < 0;
            _shape = aim.Shape;
            _spread.SnapTo(aim.SpreadRadians);
            _kick = 0f;
            if (first)
            {
                _enemy.SnapTo(aim.OnEnemy ? 1f : 0f);
                _shots = aim.Shots;
                _hits = aim.Hits;
                _kills = aim.Kills;
            }
        }

        if (aim.Shots > _shots)
        {
            _kick = MathF.Min(MaximumKick, _kick + (KickPerShot * (aim.Shots - _shots)));
        }

        _shots = aim.Shots;
        _kick *= MathF.Exp(-KickSettle * elapsed);
        _spread.Target = aim.SpreadRadians;
        _spread.Update(elapsed);
        _enemy.Target = aim.OnEnemy ? 1f : 0f;
        _enemy.Update(elapsed);
        if (aim.Hits > _hits)
        {
            _hitAt = seconds;
            HitHead = aim.LastHitHead;
        }

        _hits = aim.Hits;
        if (aim.Kills > _kills)
        {
            // A kill hides the hit's mark; one landing while the last kill's still shows punches it out again.
            _punch = KillShowing;
            _killAt = seconds;
            _hitAt = float.NegativeInfinity;
            KillHead = aim.LastKillHead;
        }

        _kills = aim.Kills;
    }

    private float Remaining(float at, float duration) => Math.Clamp(1f - ((_seconds - at) / duration), 0f, 1f);
}

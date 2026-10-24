using OpenIGI.Game.Ai;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>
/// What the display does about what changed since the last frame: it shakes and flashes when the player is hit, lets the
/// health it lost fall away behind it, rolls its round counter to the new count, slides a new weapon in, pulses when the
/// player can be seen, and flickers a part in as it appears.
/// </summary>
/// <remarks>
/// Driven by the seconds it is handed, not the wall clock, so a frame captured at a given simulation time draws the same every
/// run.
/// </remarks>
public sealed class HudReactiveState
{
    /// <summary>How long a hit's shake lasts, in seconds.</summary>
    public const float ShakeSeconds = 0.35f;

    /// <summary>How long a weapon takes to slide in, in seconds.</summary>
    public const float SwitchSeconds = 0.32f;

    /// <summary>How long a part takes to flicker in, in seconds.</summary>
    public const float AppearSeconds = 0.45f;

    /// <summary>How long one pulse of the visibility warning lasts, in seconds.</summary>
    public const float PulseSeconds = 0.9f;

    /// <summary>How long a part stays up after something about it changed, in seconds, before it dims again.</summary>
    public const float AttentionSeconds = 3.5f;

    /// <summary>How long a part takes to dim, once its attention has run out, in seconds.</summary>
    public const float DimSeconds = 0.6f;

    /// <summary>How long the alert banner takes to come up, in seconds.</summary>
    public const float BannerInSeconds = 0.25f;

    /// <summary>How long the alert banner lingers, fading, once the soldiers are off the player, in seconds.</summary>
    public const float BannerOutSeconds = 1.4f;

    /// <summary>How far the display trails a turning view at most, in design pixels.</summary>
    public const float MaximumSway = 4f;

    private readonly Spring _health = new(1f, 0.08f);
    private readonly Spring _healthTrail = new(1f, 0.55f);
    private readonly Spring _rounds = new(0f, 0.09f);
    private float? _seconds;
    private float _lastHealth = 1f;
    private float _hitAt = float.NegativeInfinity;
    private float _hitStrength;
    private string? _weaponKey;
    private float _switchAt = float.NegativeInfinity;
    private float _shownAt = float.NegativeInfinity;
    private bool _wasShown;
    private bool _wasAlert;
    private float _alertAt = float.NegativeInfinity;
    private string? _firstActionKey;
    private float _actionAt = float.NegativeInfinity;
    private PlayerAlertStatus _alert;
    private PlayerAlertStatus _banner;
    private float _bannerAt = float.NegativeInfinity;
    private float _bannerGoneAt = float.NegativeInfinity;
    private float _weaponAt = float.NegativeInfinity;
    private float _statusAt = float.NegativeInfinity;
    private int? _lastMagazine;
    private HudWeaponState _lastState;
    private readonly Spring _threat = new(0f, 0.25f);
    private readonly Spring _swayX = new(0f, 0.12f);
    private readonly Spring _swayY = new(0f, 0.12f);
    private float? _lastHeading;
    private float? _lastPitch;

    /// <summary>Gets the seconds of the last update.</summary>
    public float Seconds { get; private set; }

    /// <summary>Gets the health drawn, easing to the true health.</summary>
    public float Health => _health.Value;

    /// <summary>Gets the health lost lately, falling behind the health drawn: the gauge's trail.</summary>
    public float HealthTrail => MathF.Max(_healthTrail.Value, _health.Value);

    /// <summary>Gets the rounds drawn, rolling to the true count.</summary>
    public float Rounds => _rounds.Value;

    /// <summary>Gets how far the last hit's shake has to run, one at the hit to nought.</summary>
    public float Shake => Remaining(_hitAt, ShakeSeconds) * _hitStrength;

    /// <summary>Gets how far the weapon in hand has slid in, nought at the change to one.</summary>
    public float Switch => 1f - Remaining(_switchAt, SwitchSeconds);

    /// <summary>Gets how far the display has flickered in since it was last hidden, nought to one.</summary>
    public float Appear => 1f - Remaining(_shownAt, AppearSeconds);

    /// <summary>Gets how far the first action has flickered in since it changed, nought to one.</summary>
    public float ActionAppear => 1f - Remaining(_actionAt, AppearSeconds);

    /// <summary>Gets the alert the banner tells: the soldiers' status now, or the last one while it fades.</summary>
    public PlayerAlertStatus Banner => _banner;

    /// <summary>Gets how much of the alert banner shows: up over a moment as the status changes, fading after it clears.</summary>
    public float BannerOpacity =>
        _alert != PlayerAlertStatus.None ? 1f - Remaining(_bannerAt, BannerInSeconds) : Remaining(_bannerGoneAt, BannerOutSeconds);

    /// <summary>Gets the flash the banner gives as its status changes, one at the change to nought.</summary>
    public float BannerFlash => _alert != PlayerAlertStatus.None ? Remaining(_bannerAt, BannerInSeconds * 3f) : 0f;

    /// <summary>Gets the visibility warning's pulse, one at its peak to nought, repeating while the player can be seen.</summary>
    public float AlertPulse => _wasAlert ? 1f - (((Seconds - _alertAt) % PulseSeconds) / PulseSeconds) : 0f;

    /// <summary>Gets the most anyone has noticed the player, eased, nought to one: what tints the visor.</summary>
    public float Threat => _threat.Value;

    /// <summary>Gets how far the display trails the turning view, in design pixels, across and down.</summary>
    public (float X, float Y) Sway => (_swayX.Value, _swayY.Value);

    /// <summary>
    /// How much of a part shows at a density: all of it at full; at contextual and minimal, all of it for a while after something
    /// about it changed, then dimming to a ghost -- or to nothing at minimal -- unless what it shows is urgent. The inventory
    /// never dims: what the player carries is always to be read at a glance.
    /// </summary>
    /// <param name="part">The part: <see cref="HudParts.Status"/> or <see cref="HudParts.Weapon"/>; any other always shows whole.</param>
    /// <param name="density">The density.</param>
    /// <param name="model">The frame's model, for what is urgent.</param>
    /// <returns>Its opacity, nought to one.</returns>
    public float Presence(HudParts part, HudDensity density, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (density == HudDensity.Full)
        {
            return 1f;
        }

        bool minimal = density == HudDensity.Minimal;
        (float since, bool urgent, float rest) = part switch
        {
            HudParts.Status => (_statusAt, model.Health < 0.5f || (model.Visibility is { } v && v >= model.VisibilityAlert) || model.ThreatLevel > 0.2f, minimal ? 0f : 0.55f),
            HudParts.Weapon => (_weaponAt, model.Weapon is { State: HudWeaponState.Reloading or HudWeaponState.Empty } || LowRounds(model.Weapon), minimal ? 0f : 0.4f),
            _ => (Seconds, true, 1f),
        };

        if (urgent)
        {
            return 1f;
        }

        float age = Seconds - since;
        if (age <= AttentionSeconds)
        {
            return 1f;
        }

        float fade = Math.Clamp((age - AttentionSeconds) / DimSeconds, 0f, 1f);
        return 1f + ((rest - 1f) * Easing.Evaluate(Ease.OutCubic, fade));
    }

    /// <summary>Whether a weapon's loaded rounds are down to a fifth of what it loads.</summary>
    /// <param name="weapon">The weapon, or none.</param>
    /// <returns>Whether they are low.</returns>
    public static bool LowRounds(HudWeaponView? weapon) =>
        weapon is { Magazine: { } loaded, Capacity: > 0 } held && loaded <= Math.Max(1, held.Capacity / 5);

    /// <summary>Takes in a frame's model.</summary>
    /// <param name="model">What the display shows this frame.</param>
    /// <param name="seconds">The time now, in seconds.</param>
    public void Update(HudModel model, float seconds)
    {
        ArgumentNullException.ThrowIfNull(model);
        float elapsed = _seconds is { } last ? Math.Clamp(seconds - last, 0f, 0.25f) : 0f;
        bool first = _seconds is null;
        _seconds = seconds;
        Seconds = seconds;

        // Shown again after being hidden: flicker in.
        bool shown = model.Show != HudParts.None;
        if (shown && !_wasShown)
        {
            _shownAt = seconds;
            _weaponAt = seconds;
            _statusAt = seconds;
        }

        _wasShown = shown;

        // A hit: the health drops at once, its trail follows slowly, and the display shakes by how much was lost.
        float health = Math.Clamp(model.Health, 0f, 1f);
        if (first)
        {
            _health.SnapTo(health);
            _healthTrail.SnapTo(health);
        }
        else if (health < _lastHealth - 0.001f)
        {
            _hitAt = seconds;
            _hitStrength = Math.Clamp((_lastHealth - health) * 4f, 0.35f, 1f);
            _statusAt = seconds;
        }
        else if (health > _lastHealth)
        {
            _healthTrail.SnapTo(health);
        }

        _lastHealth = health;
        _health.Target = health;
        _healthTrail.Target = health;
        _health.Update(elapsed);
        _healthTrail.Update(elapsed);

        // A new weapon slides in, and its count is shown as it is rather than rolled to.
        HudWeaponView? weapon = model.Weapon;
        string? key = weapon?.Key;
        int rounds = weapon?.Magazine ?? 0;
        if (!string.Equals(key, _weaponKey, StringComparison.Ordinal))
        {
            if (_weaponKey is not null || !first)
            {
                _switchAt = seconds;
            }

            _weaponAt = seconds;

            _weaponKey = key;
            _rounds.SnapTo(rounds);
        }

        _rounds.Target = rounds;
        _rounds.Update(elapsed);

        // The weapon comes back up when its rounds or what it is doing change.
        if (weapon?.Magazine != _lastMagazine || (weapon?.State ?? HudWeaponState.Ready) != _lastState)
        {
            _weaponAt = seconds;
        }

        _lastMagazine = weapon?.Magazine;
        _lastState = weapon?.State ?? HudWeaponState.Ready;

        // The threat eases, so the visor's tint swells and fades rather than blinking.
        _threat.Target = model.ThreatLevel;

        // The alert banner: a new status comes up with a flash; one cleared lingers, fading.
        if (model.AlertStatus != _alert)
        {
            if (model.AlertStatus != PlayerAlertStatus.None)
            {
                _banner = model.AlertStatus;
                _bannerAt = seconds;
            }
            else
            {
                _bannerGoneAt = seconds;
            }

            _alert = model.AlertStatus;
        }
        _threat.Update(elapsed);

        // The sway: the display lags the way the view turns, by how fast it turns, and springs back.
        float yawRate = 0f, pitchRate = 0f;
        if (elapsed > 0f && model.HeadingRadians is { } heading && _lastHeading is { } lastHeading)
        {
            float turn = heading - lastHeading;
            turn -= MathF.Round(turn / MathF.Tau) * MathF.Tau;
            yawRate = turn / elapsed;
        }

        if (elapsed > 0f && model.PitchRadians is { } pitch && _lastPitch is { } lastPitch)
        {
            pitchRate = (pitch - lastPitch) / elapsed;
        }

        _lastHeading = model.HeadingRadians;
        _lastPitch = model.PitchRadians;
        _swayX.Target = Math.Clamp(-yawRate * 1.2f, -MaximumSway, MaximumSway);
        _swayY.Target = Math.Clamp(pitchRate * 1.2f, -MaximumSway, MaximumSway);
        _swayX.Update(elapsed);
        _swayY.Update(elapsed);

        // Seen: the meter pulses from the moment it crosses the warning, for as long as it stays over.
        bool alert = model.Visibility is { } visible && visible >= model.VisibilityAlert;
        if (alert && !_wasAlert)
        {
            _alertAt = seconds;
            _statusAt = seconds;
        }

        _wasAlert = alert;

        // A new first action flickers in.
        string? action = model.Actions.Count > 0 ? model.Actions[0].Key : null;
        if (!string.Equals(action, _firstActionKey, StringComparison.Ordinal))
        {
            _firstActionKey = action;
            if (action is not null)
            {
                _actionAt = seconds;
            }
        }
    }

    // What is left of a span begun at a time, one at its start to nought at its end.
    private float Remaining(float start, float length) =>
        Seconds < start || Seconds >= start + length ? 0f : 1f - ((Seconds - start) / length);
}

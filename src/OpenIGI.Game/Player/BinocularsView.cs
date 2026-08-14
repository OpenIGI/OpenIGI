using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>Data-driven binocular toggle and zoom state.</summary>
/// <remarks>
/// The binocular task publishes minimum, maximum and default half-angle tangents in its own value
/// record. The input layer only supplies intent; this class owns the persistent zoom and clamps it to
/// those authored bounds, so different binocular records do not require special cases.
/// </remarks>
public sealed class BinocularsView
{
    /// <summary>The weapon task type that publishes binocular view parameters.</summary>
    public const string TaskType = "TASKTYPE_BINOCULAR";

    /// <summary>The native current-tangent threshold at which the camera chooses its wide-field rate.</summary>
    /// <remarks><c>FUN_00431E30</c> compares the current tangent with the retail double <c>0.8</c>.</remarks>
    public const float NativeWideFieldThreshold = 0.8f;

    /// <summary>The fallback rate used when the host has not supplied the camera's two runtime rates.</summary>
    /// <remarks>
    /// The executable keeps the transition rate in the view configuration, not in the three binocular
    /// weapon arguments. The managed weapon record therefore cannot recover it from the QVM alone. Keep
    /// the rate here as the retail fallback, but model the native current/target state below so input
    /// changes a target and the camera converges toward it instead of applying an arbitrary jump.
    /// </remarks>
    public const float NativeZoomSpeed = 0.08f;

    /// <summary>Compatibility name for callers that used the old one-step implementation.</summary>
    public const float ZoomStep = NativeZoomSpeed;

    private WeaponType? _weapon;
    private float _targetTanX;
    private readonly RetailFovTransitionRates _transitionRates;

    /// <summary>Initializes binocular state with the retail camera convergence rates.</summary>
    /// <param name="transitionRates">
    /// The two rates read by the native camera config at <c>+0x20</c> and <c>+0x24</c>. When omitted,
    /// both use the compatibility fallback until the desktop host wires its loaded camera profile.
    /// </param>
    public BinocularsView(RetailFovTransitionRates? transitionRates = null)
    {
        _transitionRates = transitionRates ?? RetailFovTransitionRates.Fallback;
    }

    /// <summary>Gets whether the binocular view is active.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets whether this tick opened the view.</summary>
    public bool OpenedThisTick { get; private set; }

    /// <summary>Gets whether this tick closed the view.</summary>
    public bool ClosedThisTick { get; private set; }

    /// <summary>Gets whether a zoom step changed the authored field of view this tick.</summary>
    public bool ZoomChangedThisTick { get; private set; }

    /// <summary>Gets whether the native zoom target changed this tick.</summary>
    /// <remarks>
    /// The retail device cue is an edge-triggered sound: holding a zoom key must not restart it on
    /// every fixed tick while the camera converges on the target tangent.
    /// </remarks>
    public bool ZoomTargetChangedThisTick { get; private set; }

    /// <summary>Returns the binocular state to its closed/default state.</summary>
    public void Reset()
    {
        _weapon = null;
        _targetTanX = HumanView.DefaultTanX;
        IsOpen = false;
        OpenedThisTick = false;
        ClosedThisTick = false;
        ZoomChangedThisTick = false;
        ZoomTargetChangedThisTick = false;
    }

    /// <summary>Advances the binocular toggle and zoom state by one simulation tick.</summary>
    public SightZoomFrame Tick(
        WeaponType? weapon,
        bool togglePressed,
        bool zoomInHeld,
        bool zoomOutHeld,
        float tanX,
        float viewModelTanX)
    {
        OpenedThisTick = false;
        ClosedThisTick = false;
        ZoomChangedThisTick = false;
        ZoomTargetChangedThisTick = false;

        if (!ReferenceEquals(_weapon, weapon))
        {
            if (IsOpen)
            {
                ClosedThisTick = true;
            }

            _weapon = weapon;
            IsOpen = false;
            _targetTanX = HumanView.DefaultTanX;
        }

        if (!TryGetBounds(weapon, out float minimum, out float maximum, out float authoredDefault))
        {
            IsOpen = false;
            _targetTanX = HumanView.DefaultTanX;
            float restoredTanX = MoveToward(tanX, _targetTanX);
            ZoomChangedThisTick = restoredTanX != tanX;
            return new SightZoomFrame(restoredTanX, HumanView.DefaultViewModelTanX);
        }

        if (togglePressed)
        {
            IsOpen = !IsOpen;
            OpenedThisTick = IsOpen;
            ClosedThisTick = !IsOpen;
            if (!IsOpen)
            {
                _targetTanX = HumanView.DefaultTanX;
                ZoomTargetChangedThisTick = true;
            }

            // The native binocular task writes the camera's target field (+730); the camera update
            // then moves its current field (+720) toward that target. Do not snap the current FOV on
            // entry, or the opening transition is visibly faster than retail. The task also does not
            // replace the first-person rig's independently authored view-model tangent.
            if (_targetTanX != authoredDefault)
            {
                _targetTanX = authoredDefault;
                ZoomTargetChangedThisTick = true;
            }
        }

        if (!IsOpen)
        {
            _targetTanX = HumanView.DefaultTanX;
            float restoredTanX = MoveToward(tanX, _targetTanX);
            ZoomChangedThisTick = restoredTanX != tanX;
            return new SightZoomFrame(restoredTanX, HumanView.DefaultViewModelTanX);
        }

        float previousTanX = tanX;
        if (zoomInHeld)
        {
            if (_targetTanX != minimum)
            {
                _targetTanX = minimum;
                ZoomTargetChangedThisTick = true;
            }
        }
        else if (zoomOutHeld)
        {
            if (_targetTanX != maximum)
            {
                _targetTanX = maximum;
                ZoomTargetChangedThisTick = true;
            }
        }

        // FUN_00431E30 moves the current tangent toward its target by the configured rate and clamps
        // to the target. The binocular task owns only that world-FOV target; leave the view-model
        // tangent untouched just as the native task does.
        tanX = MoveToward(tanX, _targetTanX);
        ZoomChangedThisTick = tanX != previousTanX;

        return new SightZoomFrame(tanX, HumanView.DefaultViewModelTanX);
    }

    private float MoveToward(float current, float target)
    {
        float amount = _transitionRates.ForCurrentTangent(current);
        if (current < target)
        {
            return Math.Min(target, current + amount);
        }

        if (current > target)
        {
            return Math.Max(target, current - amount);
        }

        return target;
    }

    private static bool TryGetBounds(WeaponType? weapon, out float minimum, out float maximum, out float authoredDefault)
    {
        minimum = maximum = authoredDefault = 0f;
        if (weapon is null || !string.Equals(weapon.Weapon.TypeName, TaskType, StringComparison.Ordinal)
            || weapon.Weapon.Values.Count < 3)
        {
            return false;
        }

        authoredDefault = weapon.Weapon.Values[0];
        minimum = weapon.Weapon.Values[1];
        maximum = weapon.Weapon.Values[2];
        return minimum <= authoredDefault && authoredDefault <= maximum;
    }
}

/// <summary>The two runtime tangent rates selected by retail's human-camera configuration.</summary>
/// <remarks>
/// The native integrator at <c>0x00431E30</c> reads <c>cameraConfig+0x20</c> when the current tangent
/// is at or above <c>0.8</c>, otherwise it reads <c>cameraConfig+0x24</c>. These are rates per simulation
/// update, not weapon-QVM arguments. Keeping them as a value makes the selection testable and prevents
/// the binocular task from silently growing a second hardcoded zoom algorithm.
/// </remarks>
public readonly record struct RetailFovTransitionRates(float WideFieldRate, float NarrowFieldRate)
{
    /// <summary>Compatibility fallback used until the host supplies its loaded camera profile.</summary>
    public static RetailFovTransitionRates Fallback => new(BinocularsView.NativeZoomSpeed, BinocularsView.NativeZoomSpeed);

    /// <summary>Returns the native rate selected for the supplied current tangent.</summary>
    public float ForCurrentTangent(float currentTangent) =>
        currentTangent >= BinocularsView.NativeWideFieldThreshold ? WideFieldRate : NarrowFieldRate;
}

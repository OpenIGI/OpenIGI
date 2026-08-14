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

    /// <summary>How much of the world tangent survives one retail zoom-in tick.</summary>
    /// <remarks>Ghidra memory at <c>0x005319C4</c> contains the retail float <c>0.95999998</c>.</remarks>
    public const float ZoomInFactor = 0.95999998f;

    /// <summary>How much the world tangent grows on one retail zoom-out tick.</summary>
    /// <remarks>Ghidra memory at <c>0x00531A78</c> contains the retail float <c>1.04</c>.</remarks>
    public const float ZoomOutFactor = 1.04f;

    private WeaponType? _weapon;
    private float _targetTanX;
    private bool _settlingToDefault;
    private bool _settlingClosed;
    /// <summary>Gets whether the binocular view is active.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Gets whether binocular state still owns or is restoring the camera field of view.
    /// </summary>
    /// <remarks>
    /// The camera only ticks this state machine while binoculars are held. The transition therefore
    /// has to be explicitly discarded when a weapon swap selects another optical device.
    /// </remarks>
    public bool HasOpticsState => IsOpen || _settlingToDefault || _settlingClosed;

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
        _settlingToDefault = false;
        _settlingClosed = false;
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
            _settlingToDefault = false;
            _settlingClosed = false;
        }

        if (!TryGetBounds(weapon, out float minimum, out float maximum, out float authoredDefault))
        {
            IsOpen = false;
            _targetTanX = HumanView.DefaultTanX;
            _settlingToDefault = false;
            _settlingClosed = true;
            float restoredTanX = MoveToward(tanX, _targetTanX);
            if (restoredTanX == _targetTanX)
            {
                _settlingClosed = false;
            }
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
                _settlingToDefault = false;
                _settlingClosed = true;
                ZoomTargetChangedThisTick = true;
            }

            // The native binocular task writes the camera's target field (+730); the camera update
            // then moves its current field (+720) toward that target. Do not snap the current FOV on
            // entry, or the opening transition is visibly faster than retail. The task also does not
            // replace the first-person rig's independently authored view-model tangent.
            if (_targetTanX != authoredDefault)
            {
                _targetTanX = authoredDefault;
                _settlingToDefault = true;
                ZoomTargetChangedThisTick = true;
            }
        }

        if (!IsOpen)
        {
            _targetTanX = HumanView.DefaultTanX;
            float restoredTanX = _settlingClosed ? MoveToward(tanX, _targetTanX) : tanX;
            if (restoredTanX == _targetTanX)
            {
                _settlingClosed = false;
            }
            ZoomChangedThisTick = restoredTanX != tanX;
            return new SightZoomFrame(restoredTanX, HumanView.DefaultViewModelTanX);
        }

        float previousTanX = tanX;
        if (zoomInHeld)
        {
            _settlingToDefault = false;
            if (_targetTanX != minimum)
            {
                _targetTanX = minimum;
                ZoomTargetChangedThisTick = true;
            }

            tanX = Math.Max(minimum, tanX * ZoomInFactor);
        }
        else if (zoomOutHeld)
        {
            _settlingToDefault = false;
            if (_targetTanX != maximum)
            {
                _targetTanX = maximum;
                ZoomTargetChangedThisTick = true;
            }

            tanX = Math.Min(maximum, tanX * ZoomOutFactor);
        }
        else if (_settlingToDefault)
        {
            // Opening uses the task's authored default as a one-time camera transition. Once a zoom
            // key has been used, the native handler leaves the current magnification where it is until
            // another zoom input arrives; it does not keep chasing the last min/max target.
            tanX = MoveToward(tanX, _targetTanX);
            if (tanX == _targetTanX)
            {
                _settlingToDefault = false;
            }
        }
        ZoomChangedThisTick = tanX != previousTanX;

        return new SightZoomFrame(tanX, HumanView.DefaultViewModelTanX);
    }

    private static float MoveToward(float current, float target)
    {
        if (current > target)
        {
            return Math.Max(target, current * ZoomInFactor);
        }

        if (current < target)
        {
            return Math.Min(target, current * ZoomOutFactor);
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

using OpenIGI.Game.Weapons;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>The binoculars' view and zoom, as <c>Binocular_Use</c> (<c>0x470890</c>) drives the view's field of view.</summary>
/// <remarks>
/// <para>
/// The handler writes the view's field-of-view tangent (<c>HumanView+484</c>) outright, and nothing between it and the
/// screen smooths it: opening <b>snaps</b> it to the default, each zoom tick multiplies it by
/// <see cref="ZoomInFactor"/> or <see cref="ZoomOutFactor"/>, and closing snaps it home through the weapon change's
/// view reset (<c>0x4659E0</c>). A zoom step is taken while the tangent is still short of its limit, and not clamped,
/// so the last one may pass it by up to a step.
/// </para>
/// <para>
/// Alternate fire zooms too (<c>Human+460</c> against last tick's <c>+300</c>): held, it zooms in; let go, it
/// latches; pressed again while latched it snaps back to the default, and letting go of that press frees it to zoom
/// in again. The dedicated zoom slots (<c>+488</c> in, <c>+492</c> out) zoom whatever the latch says.
/// </para>
/// <para>
/// The three angles are the weapon config's <c>Binocular - Default/Max/Min FOV (degrees)</c> tags, which
/// <c>0x470D70</c> turns into half-angle tangents (default 70, 90 and 5 when a tag is missing) -- not the task's own
/// arguments, whose reader halves with integer division and so makes the minimum 2 degrees, not 2.5.
/// </para>
/// </remarks>
public sealed class BinocularsView
{
    /// <summary>The weapon task type that publishes binocular view parameters.</summary>
    public const string TaskType = "TASKTYPE_BINOCULAR";

    /// <summary>How much of the world tangent survives one zoom-in tick (<c>0x3F75C28F</c>).</summary>
    public const float ZoomInFactor = 0.95999998f;

    /// <summary>How much the world tangent grows on one zoom-out tick (<c>0x3F851EB8</c>).</summary>
    public const float ZoomOutFactor = 1.04f;

    /// <summary>The config tag naming the field of view the binoculars open at.</summary>
    public const string DefaultFovTag = "Binocular - Default FOV (degrees)";

    /// <summary>The config tag naming the widest field of view.</summary>
    public const string MaxFovTag = "Binocular - Max FOV (degrees)";

    /// <summary>The config tag naming the narrowest field of view.</summary>
    public const string MinFovTag = "Binocular - Min FOV (degrees)";

    // 0x470D70's fallbacks for a missing tag, in degrees.
    private const float DefaultFovDegrees = 70f;
    private const float MaxFovDegrees = 90f;
    private const float MinFovDegrees = 5f;

    private WeaponType? _weapon;
    private bool _latched;

    /// <summary>Gets whether the binocular view is active.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets whether binocular state still owns the camera field of view.</summary>
    public bool HasOpticsState => IsOpen;

    /// <summary>Gets whether this tick opened the view.</summary>
    public bool OpenedThisTick { get; private set; }

    /// <summary>Gets whether this tick closed the view.</summary>
    public bool ClosedThisTick { get; private set; }

    /// <summary>Gets whether the field of view changed this tick.</summary>
    public bool ZoomChangedThisTick { get; private set; }

    /// <summary>Gets whether a zoom step changed the magnification this tick, which is when <c>bin_zoom_1</c> plays.</summary>
    /// <remarks>
    /// The handler starts the cue on a step and stops it on any tick that takes none: at a limit, with no zoom input,
    /// on the snap back to the default, and on the opening tick.
    /// </remarks>
    public bool ZoomSteppingThisTick { get; private set; }

    /// <summary>Returns the binocular state to its closed/default state.</summary>
    public void Reset()
    {
        _weapon = null;
        _latched = false;
        IsOpen = false;
        OpenedThisTick = false;
        ClosedThisTick = false;
        ZoomChangedThisTick = false;
        ZoomSteppingThisTick = false;
    }

    /// <summary>Advances the binocular view and zoom by one simulation tick.</summary>
    /// <param name="weapon">The weapon in hand.</param>
    /// <param name="togglePressed">Whether the binoculars were raised or put away this tick.</param>
    /// <param name="zoomInHeld">Whether the zoom-in slot is down.</param>
    /// <param name="zoomOutHeld">Whether the zoom-out slot is down.</param>
    /// <param name="tanX">The view's current field-of-view tangent.</param>
    /// <param name="viewModelTanX">The first-person model's tangent, which the binoculars leave alone.</param>
    /// <param name="alternateHeld">Whether alternate fire is down this tick.</param>
    /// <param name="alternateHeldLastTick">Whether it was down last tick.</param>
    /// <returns>The two tangents to use for this tick.</returns>
    public SightZoomFrame Tick(
        WeaponType? weapon,
        bool togglePressed,
        bool zoomInHeld,
        bool zoomOutHeld,
        float tanX,
        float viewModelTanX,
        bool alternateHeld = false,
        bool alternateHeldLastTick = false)
    {
        _ = viewModelTanX;
        OpenedThisTick = false;
        ClosedThisTick = false;
        ZoomChangedThisTick = false;
        ZoomSteppingThisTick = false;

        if (!ReferenceEquals(_weapon, weapon))
        {
            ClosedThisTick = IsOpen;
            _weapon = weapon;
            IsOpen = false;
        }

        if (!TryGetBounds(weapon, out float minimum, out float maximum, out float authoredDefault))
        {
            IsOpen = false;
            return Closed(tanX);
        }

        if (togglePressed)
        {
            IsOpen = !IsOpen;
            OpenedThisTick = IsOpen;
            ClosedThisTick = !IsOpen;
            if (!IsOpen)
            {
                return Closed(tanX);
            }

            // 0x470B6F: the first use snaps the view to the default and does nothing else this tick.
            _latched = false;
            ZoomChangedThisTick = tanX != authoredDefault;
            return new SightZoomFrame(authoredDefault, HumanView.DefaultViewModelTanX);
        }

        if (!IsOpen)
        {
            return new SightZoomFrame(tanX, HumanView.DefaultViewModelTanX);
        }

        float previousTanX = tanX;
        if (!alternateHeld && alternateHeldLastTick)
        {
            _latched = !_latched;
        }

        if (alternateHeld && !alternateHeldLastTick && _latched)
        {
            tanX = authoredDefault;
        }
        else if (zoomInHeld || (alternateHeld && !_latched))
        {
            if (tanX > minimum)
            {
                tanX *= ZoomInFactor;
                ZoomSteppingThisTick = true;
            }
        }
        else if (zoomOutHeld && tanX < maximum)
        {
            tanX *= ZoomOutFactor;
            ZoomSteppingThisTick = true;
        }

        ZoomChangedThisTick = tanX != previousTanX;
        return new SightZoomFrame(tanX, HumanView.DefaultViewModelTanX);
    }

    /// <summary>
    /// How far the optics are zoomed in from the authored default toward the closest magnification,
    /// from 0 (at the default, or zoomed back out past it) to 1 (at the tightest zoom).
    /// </summary>
    /// <remarks>
    /// The remastered detector's range scales with this: zooming in narrows the field, so the current
    /// half-angle tangent moves from the authored default down toward the authored minimum, and the
    /// fraction is that progress. Clamped, so zooming back out past the default cannot push the range
    /// below its base and the closest zoom cannot push it past the maximum.
    /// </remarks>
    /// <param name="currentTanX">The optics' current horizontal half-angle tangent.</param>
    /// <returns>The zoom-in progress in <c>[0, 1]</c>.</returns>
    public float ZoomInFraction(float currentTanX)
    {
        if (!TryGetBounds(_weapon, out float minimum, out float _, out float authoredDefault)
            || authoredDefault <= minimum)
        {
            return 0f;
        }

        return Math.Clamp((authoredDefault - currentTanX) / (authoredDefault - minimum), 0f, 1f);
    }

    /// <summary>The half-angle tangent <c>0x470D70</c> makes of a field of view in degrees.</summary>
    /// <param name="degrees">The field of view.</param>
    /// <returns>The tangent of half of it.</returns>
    public static float HalfAngleTangent(float degrees) =>
        (float)Math.Tan(degrees * 0.5f * (double)WeaponTaskParameters.DegreesToRadians);

    // Put away, the weapon change resets the view (0x4659E0): the default tangent, at once.
    private SightZoomFrame Closed(float tanX)
    {
        ZoomChangedThisTick = tanX != HumanView.DefaultTanX;
        return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
    }

    private static bool TryGetBounds(WeaponType? weapon, out float minimum, out float maximum, out float authoredDefault)
    {
        minimum = maximum = authoredDefault = 0f;
        if (weapon is null || !string.Equals(weapon.Weapon.TypeName, TaskType, StringComparison.Ordinal))
        {
            return false;
        }

        authoredDefault = HalfAngleTangent(Tag(weapon, DefaultFovTag, DefaultFovDegrees));
        maximum = HalfAngleTangent(Tag(weapon, MaxFovTag, MaxFovDegrees));
        minimum = HalfAngleTangent(Tag(weapon, MinFovTag, MinFovDegrees));
        return true;
    }

    private static float Tag(WeaponType weapon, string tag, float fallback) =>
        weapon.RetailConfig is { } config && WeaponConfigRegistry.TryGetRealTag(config, tag, out float degrees)
            ? degrees
            : fallback;
}

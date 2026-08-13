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

    /// <summary>The authored-tangent step applied while a zoom direction is held.</summary>
    public const float ZoomStep = 0.08f;

    private WeaponType? _weapon;

    /// <summary>Gets whether the binocular view is active.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets whether this tick opened the view.</summary>
    public bool OpenedThisTick { get; private set; }

    /// <summary>Gets whether this tick closed the view.</summary>
    public bool ClosedThisTick { get; private set; }

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

        if (!ReferenceEquals(_weapon, weapon))
        {
            if (IsOpen)
            {
                ClosedThisTick = true;
            }

            _weapon = weapon;
            IsOpen = false;
        }

        if (!TryGetBounds(weapon, out float minimum, out float maximum, out float authoredDefault))
        {
            IsOpen = false;
            return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        }

        if (togglePressed)
        {
            IsOpen = !IsOpen;
            OpenedThisTick = IsOpen;
            ClosedThisTick = !IsOpen;
            if (!IsOpen)
            {
                return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
            }

            tanX = authoredDefault;
            viewModelTanX = authoredDefault;
        }

        if (!IsOpen)
        {
            return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        }

        if (zoomInHeld)
        {
            tanX = Math.Max(minimum, tanX - ZoomStep);
            viewModelTanX = Math.Max(minimum, viewModelTanX - ZoomStep);
        }
        else if (zoomOutHeld)
        {
            tanX = Math.Min(maximum, tanX + ZoomStep);
            viewModelTanX = Math.Min(maximum, viewModelTanX + ZoomStep);
        }

        return new SightZoomFrame(tanX, viewModelTanX);
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

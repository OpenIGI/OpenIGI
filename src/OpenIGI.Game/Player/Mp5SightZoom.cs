using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>The MP5 SD3's alternate-fire sight zoom.</summary>
/// <remarks>
/// <para>
/// <c>GunMP5SD</c>'s fire-message handler (<c>0x473AF0</c>) toggles an internal latch on the rising
/// edge of alternate fire. It narrows both first-person field-of-view tangents by <c>0.8</c> per
/// thirty-hertz tick and restores them by <c>1.25</c>; this is tick-driven and must not be multiplied
/// by render-frame time.
/// </para>
/// <para>
/// The minimum comes from the <c>TASKTYPE_GUNMP5SD</c> tail argument, decoded by
/// <see cref="WeaponTaskParameters"/>. Changing weapons resets the latch and both tangents, as the
/// original weapon-change states do through <c>0x4659E0</c>.
/// </para>
/// </remarks>
public sealed class Mp5SightZoom
{
    /// <summary>The task class whose fire handler implements this zoom.</summary>
    public const string TaskType = "TASKTYPE_GUNMP5SD";

    /// <summary>How much of each tangent survives one zoom-in tick.</summary>
    public const float ZoomInFactor = 0.80000001f;

    /// <summary>How much each tangent grows on one zoom-out tick.</summary>
    public const float ZoomOutFactor = 1.25f;

    private WeaponType? _weapon;

    /// <summary>Gets whether the MP5 zoom latch is engaged.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Advances the sight by one simulation tick.</summary>
    /// <param name="weapon">The weapon currently in hand, or <see langword="null"/>.</param>
    /// <param name="togglePressed">Whether alternate fire rose this tick.</param>
    /// <param name="tanX">The current world horizontal half-FOV tangent.</param>
    /// <param name="viewModelTanX">The current first-person model horizontal half-FOV tangent.</param>
    /// <returns>The two tangents to use for this tick.</returns>
    public SightZoomFrame Tick(
        WeaponType? weapon,
        bool togglePressed,
        float tanX,
        float viewModelTanX)
    {
        if (!ReferenceEquals(_weapon, weapon))
        {
            _weapon = weapon;
            IsActive = false;
            tanX = HumanView.DefaultTanX;
            viewModelTanX = HumanView.DefaultViewModelTanX;
        }

        if (!TryGetMinimum(weapon, out float minimumTanX))
        {
            IsActive = false;
            return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        }

        if (togglePressed)
        {
            IsActive = !IsActive;
        }

        if (IsActive)
        {
            if (tanX > minimumTanX)
            {
                tanX *= ZoomInFactor;
                viewModelTanX *= ZoomInFactor;
            }
        }
        else if (tanX < HumanView.DefaultTanX)
        {
            tanX *= ZoomOutFactor;
            viewModelTanX *= ZoomOutFactor;
            if (tanX > HumanView.DefaultTanX)
            {
                tanX = HumanView.DefaultTanX;
                viewModelTanX = HumanView.DefaultViewModelTanX;
            }
        }

        return new SightZoomFrame(tanX, viewModelTanX);
    }

    private static bool TryGetMinimum(WeaponType? weapon, out float minimumTanX)
    {
        minimumTanX = 0f;
        if (weapon is null
            || !string.Equals(weapon.Weapon.TypeName, TaskType, StringComparison.Ordinal)
            || weapon.Weapon.Values.Count == 0)
        {
            return false;
        }

        minimumTanX = weapon.Weapon.Values[0];
        return true;
    }
}

/// <summary>The world and first-person-model field-of-view tangents for one tick.</summary>
/// <param name="TanX">The world's horizontal half-FOV tangent.</param>
/// <param name="ViewModelTanX">The first-person model's horizontal half-FOV tangent.</param>
public readonly record struct SightZoomFrame(float TanX, float ViewModelTanX);

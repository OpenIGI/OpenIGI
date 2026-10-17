using OpenIGI.Game.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Igi1.Weapons;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>The Dragunov's sniper scope, as <c>GunDRAGUNOV</c>'s fire handler drives it.</summary>
/// <remarks>
/// <para>
/// <c>0x472E50</c>, read for this class, and a different machine from the MP5's sight: the latch
/// lives on the view (<c>+492</c>, set by <c>0x4659D0</c>, cleared by <c>0x4659E0</c>), and it works
/// like this. A <b>rising edge of alternate fire opens the scope</b>; while the key is <em>still
/// held</em> the view glides in by <see cref="ZoomInFactor"/> a tick towards the weapon's own
/// minimum tangent — release and the magnification <b>holds where it is</b>. The dedicated zoom
/// slots adjust from there: <see cref="PlayerInputSlot.ZoomIn"/> resumes the glide in,
/// <see cref="PlayerInputSlot.ZoomOut"/> glides back out by <see cref="ZoomOutFactor"/> a tick, no
/// wider than the default. A second press of alternate fire closes the scope and <b>snaps</b> both
/// tangents home instantly — <c>0x4659E0</c> writes the defaults outright, where the MP5 glides out.
/// </para>
/// <para>
/// The edge detection is the input frame's own: the handler compares the alternate-fire slot's
/// current value (<c>Human+460</c>, slot 7 of the newest frame at <c>+432</c>) against the previous
/// tick's (<c>+300</c>, the same slot of the frame kept at <c>+272</c>). That the "keep zooming
/// while held" behaviour falls out of the <em>previous</em> value being read is the original's own
/// arithmetic, kept here.
/// </para>
/// <para>
/// While the scope is open the handler also multiplies the round's sampled view kick by
/// <see cref="ScopedRecoilFactor"/> before it reaches the accumulators — a braced rifle, not a
/// different recoil model. The minimum comes from the <c>TASKTYPE_GUNDRAGUNOV</c> tail argument
/// exactly as the MP5's does; the shipped <c>5</c> decodes to <c>tan(2°)</c> through the reader's
/// integer halving. Changing weapons snaps the scope closed, as the weapon-change states do through
/// <c>0x4659E0</c>.
/// </para>
/// </remarks>
public sealed class DragunovScope
{
    /// <summary>The task class whose fire handler implements this scope.</summary>
    public const string TaskType = "TASKTYPE_GUNDRAGUNOV";

    /// <summary>How much of each tangent survives one zoom-in tick.</summary>
    /// <remarks>Spelled <c>0.95999998</c> — a far gentler glide than the MP5's 0.8.</remarks>
    public const float ZoomInFactor = 0.95999998f;

    /// <summary>How much each tangent grows on one zoom-out tick.</summary>
    public const float ZoomOutFactor = 1.04f;

    /// <summary>What fraction of a round's view kick survives while scoped.</summary>
    /// <remarks>Spelled <c>0.050000001</c>.</remarks>
    public const float ScopedRecoilFactor = 0.050000001f;

    private WeaponType? _weapon;

    /// <summary>Gets whether the scope is open.</summary>
    public bool IsScoped { get; private set; }

    /// <summary>Closes the scope without waiting for another input or weapon tick.</summary>
    /// <remarks>Used when the player leaves first-person control, such as entering the death camera.</remarks>
    public void Reset()
    {
        _weapon = null;
        IsScoped = false;
    }

    /// <summary>Advances the scope by one simulation tick.</summary>
    /// <param name="weapon">The weapon currently in hand, or <see langword="null"/>.</param>
    /// <param name="alternateHeld">Whether alternate fire is down this tick.</param>
    /// <param name="alternateHeldLastTick">Whether it was down last tick.</param>
    /// <param name="zoomInHeld">Whether the zoom-in slot is down this tick.</param>
    /// <param name="zoomOutHeld">Whether the zoom-out slot is down this tick.</param>
    /// <param name="tanX">The current world horizontal half-FOV tangent.</param>
    /// <param name="viewModelTanX">The current first-person model tangent.</param>
    /// <returns>The two tangents to use for this tick.</returns>
    public SightZoomFrame Tick(
        WeaponType? weapon,
        bool alternateHeld,
        bool alternateHeldLastTick,
        bool zoomInHeld,
        bool zoomOutHeld,
        float tanX,
        float viewModelTanX)
    {
        if (!ReferenceEquals(_weapon, weapon))
        {
            _weapon = weapon;
            IsScoped = false;
            tanX = HumanView.DefaultTanX;
            viewModelTanX = HumanView.DefaultViewModelTanX;
        }

        if (!TryGetMinimum(weapon, out float minimumTanX))
        {
            IsScoped = false;
            return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        }

        bool risingEdge = alternateHeld && !alternateHeldLastTick;

        if (IsScoped)
        {
            if (risingEdge)
            {
                // 0x4659E0: the latch clears and BOTH tangents snap home in the same write -- the
                // one instant transition in a machine that otherwise only glides.
                IsScoped = false;
                return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
            }

            // The original's selector verbatim: the glide IN runs while the zoom-in slot is held OR
            // alternate was down last tick -- which is precisely "keep zooming while the key that
            // opened the scope is still held". Only with neither does the zoom-out slot get a say.
            if (!zoomInHeld && !alternateHeldLastTick)
            {
                if (zoomOutHeld)
                {
                    if (HumanView.DefaultTanX > tanX)
                    {
                        tanX *= ZoomOutFactor;
                        viewModelTanX *= ZoomOutFactor;
                    }

                    if (HumanView.DefaultTanX < tanX)
                    {
                        tanX = HumanView.DefaultTanX;
                        viewModelTanX = HumanView.DefaultViewModelTanX;
                    }
                }
            }
            else if (tanX > minimumTanX)
            {
                tanX *= ZoomInFactor;
                viewModelTanX *= ZoomInFactor;
            }
        }
        else if (risingEdge)
        {
            // 0x4659D0: the latch sets and nothing else happens this tick; the glide begins next
            // tick, off the previous-frame slot the selector above reads.
            IsScoped = true;
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

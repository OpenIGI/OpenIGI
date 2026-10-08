namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Whether a weapon's zoom is up, <c>WeaponOverlayStatus_e</c> (<c>Weapon+0xA0</c>).</summary>
public enum Igi2WeaponOverlayStatus
{
    /// <summary>Down.</summary>
    None = 0,

    /// <summary>Up, with no overlay: the zoom's overlay type is not a task type.</summary>
    WithoutOverlay = 1,

    /// <summary>Up, with its overlay.</summary>
    WithOverlay = 2,
}

/// <summary>
/// What a <c>Zoom</c> does when fired and when the zoom keys step it: <c>Zoom_WeaponFireHandler</c> (<c>igi2 0x525B20</c>)
/// and <c>Zoom_WeaponZoomHandler</c> (<c>0x525CC0</c>).
/// </summary>
/// <remarks>
/// <para>
/// Fired, a zoom held by a human toggles the human's view. Down, the view's zoom comes off, back to the default fields of
/// view, and so do the weapon's overlay and its status. Up, a zoom whose overlay type is a task type puts the view into
/// zoom mode and the overlay up; one whose is not only marks the weapon zoomed. Either way the view steers to the zoom's
/// widest scale, a third of the gap a tick. It fires nothing, with no delay.
/// </para>
/// <para>
/// The zoom keys step a zoomed weapon's aim by its step factor, narrower or wider, as far as its scales reach, and sound
/// its zoom sound at the view.
/// </para>
/// </remarks>
public sealed class Igi2ZoomFire : IIgi2FireHandler, IIgi2ZoomHandler
{
    /// <inheritdoc/>
    public bool HasRelease => false;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        if (request.Operator?.ZoomView is not { } view)
        {
            return Igi2FireResult.Nothing;
        }

        Igi2ZoomTask zoom = (Igi2ZoomTask)request.FireTypeParameters;
        Igi2WeaponFiring weapon = request.Weapon;
        bool hasOverlay = zoom.OverlayQTaskType != Igi2WeaponConstants.NoTaskType;
        if (view.IsZoomMode)
        {
            view.DisableZoomMode();
            if (hasOverlay)
            {
                weapon.Effects.RemoveOverlay();
            }

            weapon.SetOverlayStatus(Igi2WeaponOverlayStatus.None);
            return Igi2FireResult.Nothing;
        }

        if (hasOverlay)
        {
            view.IsZoomMode = true;
            weapon.Effects.ShowOverlay(zoom);
            weapon.MarkOverlayStatus(Igi2WeaponOverlayStatus.WithOverlay);
        }
        else
        {
            weapon.MarkOverlayStatus(Igi2WeaponOverlayStatus.WithoutOverlay);
        }

        view.AimZoom(zoom.MaximumFieldOfViewScale);
        return Igi2FireResult.Nothing;
    }

    /// <inheritdoc/>
    public Igi2FireResult Release(in Igi2FireRequest request) => default;

    /// <inheritdoc/>
    public void Zoom(in Igi2ZoomRequest request)
    {
        if (request.Weapon.OverlayStatus == Igi2WeaponOverlayStatus.None || request.Operator?.ZoomView is not { } view)
        {
            return;
        }

        Igi2ZoomTask zoom = (Igi2ZoomTask)request.FireTypeParameters;
        Fpu ideal = new(view.IdealFieldOfView);
        float stepped = (request.ZoomIn ? ideal / zoom.ZoomStepFactor : ideal * zoom.ZoomStepFactor).F;
        if (stepped < zoom.MinimumFieldOfViewScale || stepped > zoom.MaximumFieldOfViewScale)
        {
            return;
        }

        view.AimZoom(stepped);
        if (zoom.Sound.Length != 0)
        {
            request.Weapon.Effects.PlayViewSound(zoom.Sound);
        }
    }
}

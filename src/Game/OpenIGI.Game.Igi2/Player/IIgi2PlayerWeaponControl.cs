namespace OpenIGI.Game.Igi2.Player;

/// <summary>What the player's input handling asks of the weapon it is holding.</summary>
/// <remarks>
/// The signal handler is mostly about locomotion, but a few channels act on the weapon directly — the
/// zoom keys, and fire for a weapon that zooms when fired. This is the small part of the weapon those
/// paths touch, so the locomotion code does not depend on the whole weapon system.
/// </remarks>
public interface IIgi2PlayerWeaponControl
{
    /// <summary>Gets the held weapon type's <c>Weapon Category</c>, or <see langword="null"/> when nothing is held.</summary>
    int? ActiveCategory { get; }

    /// <summary>Gets a value indicating whether the held weapon type's task is a <c>Zoom</c>.</summary>
    bool ActiveIsZoom { get; }

    /// <summary>Gets a value indicating whether the held weapon is in its zoomed fire mode (<c>Human_IsZoomFireModeActive</c>).</summary>
    bool IsZoomFireModeActive { get; }

    /// <summary>Gets a value indicating whether zooming is allowed now (<c>HumanPlayer_IsZoomedModeAllowed</c>).</summary>
    bool IsZoomedModeAllowed { get; }

    /// <summary>Fires the held weapon once (<c>Weapon_Fire(weapon, 1)</c>).</summary>
    void Fire();

    /// <summary>Clears the held weapon's trigger latch (<c>Weapon+0x48</c>).</summary>
    void ClearTrigger();

    /// <summary>Steps the held weapon's zoom (<c>Weapon_Zoom</c>).</summary>
    /// <param name="zoomIn"><see langword="true"/> to zoom in, <see langword="false"/> to zoom out.</param>
    void Zoom(bool zoomIn);

    /// <summary>Moves the action menu's selection one way or the other.</summary>
    /// <param name="direction">+1 or −1.</param>
    void StepActionMenu(int direction);
}

/// <summary>A player holding nothing: every query answers "no weapon" and every request does nothing.</summary>
public sealed class Igi2NoWeaponControl : IIgi2PlayerWeaponControl
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2NoWeaponControl Instance { get; } = new();

    /// <inheritdoc/>
    public int? ActiveCategory => null;

    /// <inheritdoc/>
    public bool ActiveIsZoom => false;

    /// <inheritdoc/>
    public bool IsZoomFireModeActive => false;

    /// <inheritdoc/>
    public bool IsZoomedModeAllowed => false;

    /// <inheritdoc/>
    public void Fire()
    {
    }

    /// <inheritdoc/>
    public void ClearTrigger()
    {
    }

    /// <inheritdoc/>
    public void Zoom(bool zoomIn)
    {
    }

    /// <inheritdoc/>
    public void StepActionMenu(int direction)
    {
    }
}

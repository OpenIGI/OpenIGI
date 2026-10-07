namespace OpenIGI.Game.Igi2.Player;

/// <summary>A weapon type's spread per posture, in degrees, and its two modifiers.</summary>
/// <param name="Standing">Standing still (<c>WeaponType+0x348</c>).</param>
/// <param name="Running">Running (<c>+0x34C</c>).</param>
/// <param name="Walking">Walking (<c>+0x350</c>).</param>
/// <param name="Crouching">Crouching still (<c>+0x354</c>).</param>
/// <param name="CrouchWalking">Walking crouched (<c>+0x358</c>).</param>
/// <param name="LyingFlat">Lying flat (<c>+0x35C</c>).</param>
/// <param name="Crawling">Crawling (<c>+0x360</c>).</param>
/// <param name="JumpingModifier">Added while in the air (<c>+0x364</c>).</param>
/// <param name="TurningModifier">Scaled by the turning speed and added in every posture (<c>+0x368</c>).</param>
/// <remarks>The <c>WeaponType</c> parameters of the same names, registered by <c>WeaponType_Open</c> (<c>igi2 0x436C60</c>).</remarks>
public readonly record struct Igi2WeaponPrecision(
    float Standing,
    float Running,
    float Walking,
    float Crouching,
    float CrouchWalking,
    float LyingFlat,
    float Crawling,
    float JumpingModifier,
    float TurningModifier);

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

    /// <summary>Gets the held weapon type's precision, or <see langword="null"/> when nothing is held.</summary>
    Igi2WeaponPrecision? ActivePrecision { get; }

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

    /// <summary>Takes a zoomed weapon out of its zoomed fire mode, as getting up from lying flat does.</summary>
    /// <remarks><c>HumanPlayer_LeaveZoomAlternateFireMode</c> (<c>igi2 0x4BEC80</c>).</remarks>
    void LeaveZoomAlternateFireMode();

    /// <summary>Stops the sound the held weapon has in its first slot, as death does.</summary>
    /// <remarks>
    /// <c>Human_UpdateDie</c> (<c>igi2 0x504010</c>) deletes the task <c>Weapon.aptSoundSlotQTaskRef[0]</c>
    /// refers to, and the reference.
    /// </remarks>
    void StopSoundSlot();

    /// <summary>Takes any overlay off: the scope's zoom on the owner's view and the overlay task.</summary>
    /// <remarks><c>Weapon_SetOverlayStatus</c> (<c>igi2 0x51F1C0</c>) with status 0.</remarks>
    void ClearOverlay();

    /// <summary>Lets go of both fire types' triggers and clears the weapon's status, as death and water do.</summary>
    /// <remarks>Sets <c>atFireTypeData[0..1].isReleaseFire</c> and zeroes <c>eWeaponStatus</c>.</remarks>
    void ReleaseTriggers();

    /// <summary>Takes every weapon away (<c>Weapon_ClearAllWeapons</c>, <c>igi2 0x51DCF0</c>).</summary>
    void ClearAllWeapons();

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
    public Igi2WeaponPrecision? ActivePrecision => null;

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
    public void LeaveZoomAlternateFireMode()
    {
    }

    /// <inheritdoc/>
    public void StopSoundSlot()
    {
    }

    /// <inheritdoc/>
    public void ClearOverlay()
    {
    }

    /// <inheritdoc/>
    public void ReleaseTriggers()
    {
    }

    /// <inheritdoc/>
    public void ClearAllWeapons()
    {
    }

    /// <inheritdoc/>
    public void StepActionMenu(int direction)
    {
    }
}

using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>What the human-level functions ask of a human's weapon, whoever holds it.</summary>
public interface IIgi2HumanWeapon
{
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

    /// <summary>Gets the held weapon type's clips for a body's rig, one per base animation.</summary>
    /// <param name="rig">The body's rig.</param>
    /// <returns>The clips, or <see langword="null"/> when nothing is held.</returns>
    /// <remarks>
    /// A weapon type keeps a table for each of two rigs and hands over the first when the body's rig is its rig, the
    /// second otherwise (<c>HumanAnim_SetAnimationAndTimeFromState</c>, <c>igi2 0x58FAA0</c>).
    /// </remarks>
    IReadOnlyList<Igi2AnimationClip?>? BodyClips(Igi2BoneHierarchy? rig);
}

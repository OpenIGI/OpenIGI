using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>A projectile a fire type launches, <c>WeaponCreateProjectileContext_s</c>.</summary>
/// <param name="ProjectileTaskType">The projectile's task type, the gun's <c>Projectile TaskType</c>.</param>
/// <param name="Position">Where it starts.</param>
/// <param name="Orientation">Which way it flies, nine floats row by row, Y forward; this bullet's own when a round spreads.</param>
/// <param name="ParentSpeed">The speed it inherits from the one firing it.</param>
/// <param name="Speed">Its muzzle velocity, as the gun gives it.</param>
/// <param name="EffectiveRange">Its range, as the gun gives it.</param>
/// <param name="HeldTime">The ticks the trigger had been down.</param>
/// <param name="WeaponType">The weapon that fired it.</param>
/// <param name="AmmoType">The ammunition, or <see langword="null"/>.</param>
/// <param name="ExplodeTime">The ticks before a thrown projectile goes off, <c>nExplodeTime</c>; none for a bullet.</param>
/// <param name="Thrower">Who fired it, <c>ptIgnoreQTask</c>, or <see langword="null"/>.</param>
public readonly record struct Igi2ProjectileLaunch(
    int ProjectileTaskType,
    Vector3d Position,
    float[] Orientation,
    Vector3f ParentSpeed,
    float Speed,
    float EffectiveRange,
    int HeldTime,
    Igi2WeaponTypeTask WeaponType,
    Igi2AmmoTypeTask? AmmoType,
    int ExplodeTime = 0,
    IIgi2WeaponOwner? Thrower = null);

/// <summary>An event a weapon raises for the AI to perceive, <c>HumanAIEventContext_t</c> as <c>HumanAI_SendEvent</c> takes it.</summary>
/// <param name="Event">The event, <c>AIEVENT_*</c>.</param>
/// <param name="Flags">Its flags.</param>
/// <param name="Radius">How far it reaches, in world units.</param>
/// <param name="Position">Where it happens.</param>
/// <param name="Direction">Its direction.</param>
/// <param name="Team">The team of the one causing it.</param>
/// <param name="Originator">The human who caused it, <c>ptHumanOriginatorQTask</c>.</param>
/// <param name="Reference">What it is about, <c>ptReferenceQTask</c>.</param>
public readonly record struct Igi2WeaponAiEvent(
    int Event, int Flags, float Radius, Vector3d Position, Vector3f Direction, int Team, object? Originator = null, object? Reference = null);

/// <summary>A casing a gun throws out, <c>GunCasing_New</c> (<c>igi2 0x5287A0</c>) as the gun asks for it.</summary>
/// <param name="WeaponObject">Which of the holder's weapon objects it leaves from: its third magic vertex is where.</param>
/// <param name="Model">The casing model.</param>
/// <param name="Angle">The angle it leaves at in the weapon's frame, in radians.</param>
/// <param name="IsGun">Whether the weapon is a <c>WEAPONTYPE_GUN</c>, which throws it upward as well as out.</param>
/// <param name="Spin">The casing's own turn, in whole degrees about X, Y and Z: drawn from the run stream when it is made.</param>
public readonly record struct Igi2CasingEjection(int WeaponObject, string Model, float Angle, bool IsGun, (int Alpha, int Beta, int Gamma) Spin);

/// <summary>What firing shows and sounds, and what it hands on, behind which none of the weapon's logic depends.</summary>
/// <remarks>
/// Everything here is an effect of a decision the weapon already made, and of the run-stream draws it already took: the
/// projectile's own flight, the AI's hearing, the muzzle flash, the casing and the sounds arrive with their systems.
/// </remarks>
public interface IIgi2WeaponEffects
{
    /// <summary>Launches a projectile (<c>Weapon_eCreateProjectileQTaskEvent</c>).</summary>
    /// <param name="launch">The launch.</param>
    void CreateProjectile(in Igi2ProjectileLaunch launch);

    /// <summary>Raises an event for the AI (<c>HumanAI_SendEvent</c>, <c>igi2 0x460A90</c>).</summary>
    /// <param name="aiEvent">The event.</param>
    void SendAiEvent(in Igi2WeaponAiEvent aiEvent);

    /// <summary>Flashes the muzzle (<c>MuzzleFlash_New</c>, <c>igi2 0x4D12A0</c>).</summary>
    /// <param name="weaponObject">Which of the holder's weapon objects.</param>
    /// <param name="sprite">The front sprite, or <see langword="null"/> when the gun has none.</param>
    /// <param name="size">The flash's size.</param>
    void MuzzleFlash(int weaponObject, string? sprite, float size);

    /// <summary>Throws out a casing.</summary>
    /// <param name="casing">The casing.</param>
    void EjectCasing(in Igi2CasingEjection casing);

    /// <summary>Plays a sound in one of the weapon's sound slots, stopping what was there (<c>Weapon_PlaySound</c>, <c>igi2 0x51F290</c>).</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="sound">The sound.</param>
    /// <param name="looped">Whether it loops.</param>
    void PlaySound(int slot, string sound, bool looped);

    /// <summary>Stops every sound the weapon has playing, its slots' and its sequence's (<c>Weapon_StopAllSound</c>).</summary>
    void StopAllSounds()
    {
    }

    /// <summary>Empties the weapon's sound sequence (<c>Weapon_ClearSoundSequence</c>, <c>igi2 0x51F680</c>).</summary>
    void ClearSoundSequence();

    /// <summary>Queues a sound, to play until its time or its end (<c>Weapon_AddSoundToSoundSequence</c>, <c>igi2 0x51F5C0</c>).</summary>
    /// <param name="sound">The sound.</param>
    /// <param name="timeToPlay">How long it plays before the next, in seconds; negative for to its end.</param>
    void AddSoundToSequence(string sound, float timeToPlay);

    /// <summary>Plays the sequence on (<c>Weapon_MaintainSoundSequence</c>, <c>igi2 0x51F6B0</c>), once a tick.</summary>
    void MaintainSoundSequence();

    /// <summary>Puts up a zoom's overlay, the task of its <c>Overlay QTaskType</c> the weapon keeps (<c>Weapon+0xA4</c>).</summary>
    /// <param name="zoom">The zoom, whose overlay type and parameters make the overlay.</param>
    void ShowOverlay(Igi2ZoomTask zoom);

    /// <summary>Takes the weapon's overlay down, if it has one.</summary>
    void RemoveOverlay();

    /// <summary>Makes a sound under the holder's view, at the view (<c>SoundSys_NewSoundFn</c>, <c>SoundSys_SetSoundPosition</c>).</summary>
    /// <param name="sound">The sound definition's name.</param>
    void PlayViewSound(string sound);
}

/// <summary>Effects that show nothing and sound nothing.</summary>
public sealed class Igi2NoWeaponEffects : IIgi2WeaponEffects
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2NoWeaponEffects Instance { get; } = new();

    /// <inheritdoc/>
    public void CreateProjectile(in Igi2ProjectileLaunch launch)
    {
    }

    /// <inheritdoc/>
    public void SendAiEvent(in Igi2WeaponAiEvent aiEvent)
    {
    }

    /// <inheritdoc/>
    public void MuzzleFlash(int weaponObject, string? sprite, float size)
    {
    }

    /// <inheritdoc/>
    public void EjectCasing(in Igi2CasingEjection casing)
    {
    }

    /// <inheritdoc/>
    public void PlaySound(int slot, string sound, bool looped)
    {
    }

    /// <inheritdoc/>
    public void ClearSoundSequence()
    {
    }

    /// <inheritdoc/>
    public void AddSoundToSequence(string sound, float timeToPlay)
    {
    }

    /// <inheritdoc/>
    public void MaintainSoundSequence()
    {
    }

    /// <inheritdoc/>
    public void ShowOverlay(Igi2ZoomTask zoom)
    {
    }

    /// <inheritdoc/>
    public void RemoveOverlay()
    {
    }

    /// <inheritdoc/>
    public void PlayViewSound(string sound)
    {
    }
}

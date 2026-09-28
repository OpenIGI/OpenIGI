namespace OpenIGI.Game.World;

/// <summary>The authored limits of a gun a vehicle model carries (<c>TASKTYPE_AISTATIONARYGUN</c>).</summary>
/// <remarks>
/// <para>
/// A tank's cannon, its coaxial machine gun, an APC's gun and a helicopter's nose gun are not authored in
/// a mission script. They are attachments of the vehicle's model, and <c>DefineMagicObj</c> gives each one
/// a longer argument list that <c>0x46FA50</c> parses: the weapon's model, the arc and pitch limits, the
/// ammunition, both turn rates, the turning sound, a barrel spin rate and a minimum range.
/// </para>
/// <para>
/// The weapon is found by that model name, not by a <c>WEAPON_ID_*</c>: the gun's constructor
/// (<c>0x46EAE0</c>) hands it to <c>0x413C00</c>, which returns the weapon row whose model string matches.
/// So <c>614_03_1</c> is the T80 cannon, <c>123_01_1</c> the M2HB, <c>601_05_1</c> the APC gun and
/// <c>700_12_1</c> the helicopter's MIL gun.
/// </para>
/// </remarks>
/// <param name="WeaponModel">The weapon row's model name (argument 3).</param>
/// <param name="YawLimitDegrees">How far either side of the mount the gun may turn (argument 4, <c>+40</c>); 360 or more turns freely.</param>
/// <param name="MaximumPitchDegrees">The highest elevation (argument 5, <c>+32</c>).</param>
/// <param name="MinimumPitchDegrees">The lowest elevation (argument 6, <c>+36</c>).</param>
/// <param name="Ammunition">The rounds the gun carries (argument 7, <c>+44</c>).</param>
/// <param name="PitchRateDegreesPerSecond">How fast the barrel elevates (argument 8, <c>+48</c>, stored per tick).</param>
/// <param name="YawRateDegreesPerSecond">How fast the gun traverses (argument 9, <c>+52</c>, stored per tick).</param>
/// <param name="TurnSound">The looping sound while the gun turns (argument 10, <c>+56</c>).</param>
/// <param name="SpinDegreesPerSecond">The barrel spin added on each firing tick (argument 11, <c>+88</c>, stored per tick).</param>
/// <param name="MinimumRangeMetres">The gun holds fire at anything closer (argument 12, <c>+92</c>, stored in world units).</param>
public sealed record MagicGunMount(
    string WeaponModel,
    float YawLimitDegrees,
    float MaximumPitchDegrees,
    float MinimumPitchDegrees,
    int Ammunition,
    float PitchRateDegreesPerSecond,
    float YawRateDegreesPerSecond,
    string TurnSound,
    float SpinDegreesPerSecond,
    float MinimumRangeMetres);

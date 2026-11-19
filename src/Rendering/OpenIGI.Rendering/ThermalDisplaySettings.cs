namespace OpenIGI.Rendering;

/// <summary>How the remastered thermal sensor colours what it measures.</summary>
public enum ThermalPalette
{
    /// <summary>Heat as brightness: a cool world in greys, people glowing near-white. The military sensor's default.</summary>
    WhiteHot,

    /// <summary>White-hot inverted: people dark against a pale world.</summary>
    BlackHot,

    /// <summary>The thermographer's ramp: violet through red and orange to a yellow-white peak.</summary>
    Ironbow,

    /// <summary>A high-contrast spectrum, blue through green and yellow to red and white, which tells small differences apart.</summary>
    Rainbow,
}

/// <summary>
/// Turns the lit scene into the picture a remastered thermal sensor shows: the world re-imaged as heat, the people in it
/// glowing, plus the fade and the calibration that raise it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original's infrared view colours bodies in its heat ramp and leaves the world as it was
/// (<see cref="IGraphicsDevice.DrawInfrared"/>). This is the remaster's sensor. It runs over the linear scene before the bloom
/// and the tone map, so the hot parts of the picture bloom as light does. Who is hot comes from the bodies handed to
/// <see cref="IGraphicsDevice.DrawThermalBody"/> this frame, and how hot the world is comes from the scene's own radiance and depth.
/// </para>
/// <para>
/// Continuous rather than a switch, as <see cref="BinocularDisplaySettings"/> is: the sensor fades in as the goggles come up
/// and goes through a moment of calibration before the picture settles.
/// </para>
/// </remarks>
/// <param name="Amount">How much of the sensor's picture replaces the scene, from 0 to 1.</param>
/// <param name="Palette">How heat is coloured.</param>
/// <param name="Seconds">A continuously rising wall-clock time in seconds, for the sensor's animated noise.</param>
/// <param name="Calibration">
/// How far through its calibration the sensor is, from 1 as the goggles come up to 0 once settled: a shutter blanks the
/// picture to a flat field, and then the picture comes back noisy and settles.
/// </param>
/// <param name="Noise">How strong the sensor's grain and fixed-pattern noise are, where 1 is the remaster's look and 0 is none.</param>
public readonly record struct ThermalDisplaySettings(
    float Amount = 0f,
    ThermalPalette Palette = ThermalPalette.WhiteHot,
    float Seconds = 0f,
    float Calibration = 0f,
    float Noise = 1f)
{
    /// <summary>Gets a value indicating whether any of this would change the picture.</summary>
    public bool IsActive => Amount > 0f;
}

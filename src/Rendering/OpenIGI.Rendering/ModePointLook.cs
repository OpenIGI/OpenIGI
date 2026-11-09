namespace OpenIGI.Rendering;

/// <summary>How <see cref="IGraphicsDevice.DrawModePoint"/> draws a place a mode plays for: its colour, letter and state.</summary>
/// <param name="Red">The colour's red, linear.</param>
/// <param name="Green">The colour's green, linear.</param>
/// <param name="Blue">The colour's blue, linear.</param>
/// <param name="Brightness">Its radiance at full strength.</param>
/// <param name="Letter">The letter on the disc: 0 none, 1 A, 2 B, 3 C.</param>
/// <param name="Seconds">The time, for its animation.</param>
/// <param name="Alarm">How strongly it pulses, in <c>[0,1]</c>: a bomb planted at it.</param>
public readonly record struct ModePointLook(float Red, float Green, float Blue, float Brightness, int Letter, float Seconds, float Alarm);

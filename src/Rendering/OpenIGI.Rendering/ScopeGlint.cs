namespace OpenIGI.Rendering;

/// <summary>A sniper's scope catching the light, where it shows on the screen.</summary>
/// <param name="X">Its centre across, in pixels.</param>
/// <param name="Y">Its centre down, in pixels.</param>
/// <param name="Radius">How large its core and spikes are drawn, in pixels; the horizontal stripe runs a dozen times as far.</param>
/// <param name="Strength">How bright it is, nought to one.</param>
public readonly record struct ScopeGlint(float X, float Y, float Radius, float Strength);

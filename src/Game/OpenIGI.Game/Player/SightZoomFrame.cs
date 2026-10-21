namespace OpenIGI.Game.Player;

/// <summary>The world and first-person-model field-of-view tangents for one tick.</summary>
/// <param name="TanX">The world's horizontal half-FOV tangent.</param>
/// <param name="ViewModelTanX">The first-person model's horizontal half-FOV tangent.</param>
public readonly record struct SightZoomFrame(float TanX, float ViewModelTanX);

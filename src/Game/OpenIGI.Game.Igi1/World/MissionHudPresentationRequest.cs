using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What the first game's mission HUD is drawn from.</summary>
/// <param name="Device">The device it draws with.</param>
/// <param name="Gameplay">The mission, whose player and objectives it reads.</param>
public readonly record struct MissionHudPresentationRequest(IGraphicsDevice Device, Igi1Gameplay Gameplay);

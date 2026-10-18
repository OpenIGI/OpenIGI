using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What the first game's mission HUD and first-person view are drawn from.</summary>
/// <param name="Device">The device they draw with.</param>
/// <param name="Gameplay">The mission, whose player and objectives they read.</param>
/// <param name="Logger">Where they report.</param>
public readonly record struct MissionHudPresentationRequest(IGraphicsDevice Device, Igi1Gameplay Gameplay, IOpenIgiLogger Logger);

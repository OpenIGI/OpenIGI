using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What a title's mission HUD, first-person view, world view and viewpoint are drawn from.</summary>
/// <param name="Device">The device they draw with.</param>
/// <param name="Gameplay">The title's mission, whose player and objectives they read.</param>
/// <param name="Logger">Where they report.</param>
public readonly record struct MissionPresentationRequest(IGraphicsDevice Device, IMissionGameplay Gameplay, IOpenIgiLogger Logger);

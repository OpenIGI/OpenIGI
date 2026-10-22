namespace OpenIGI.Game.Igi2.Detection;

/// <summary>How a player of another game is seen by I.G.I. 2's soldiers: its task's visibility parameters, as I.G.I. 2 sets its own.</summary>
/// <param name="Gamma">What half the light is raised to, a level's <c>Visibility Gamma</c>.</param>
/// <param name="Minimum">The least the factor comes to, <c>Visibility Minimum</c>.</param>
/// <param name="Maximum">The most it comes to, <c>Visibility Maximum</c>.</param>
/// <param name="Light">
/// The light the player stands in where the level bakes none under it, on the lightmaps' scale: one shows a surface as bright
/// as its texture.
/// </param>
/// <remarks>The defaults are I.G.I. 2's first mission's player: 0.8, 0.2 and 0.95.</remarks>
public sealed record Igi2VisibilityTuning(float Gamma = 0.8f, float Minimum = 0.2f, float Maximum = 0.95f, float Light = 1f);

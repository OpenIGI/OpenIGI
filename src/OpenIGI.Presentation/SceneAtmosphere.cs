using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation;

/// <summary>
/// The air a level is played in, as a title reports it: what colour the distance goes and how quickly.
/// </summary>
/// <remarks>
/// <para>
/// The renderer's side of a title seam. What fills it is the running game's business — I.G.I. 2 reads
/// it off its <c>GlobalLight</c> task — and a title that authors no atmosphere reports none, which
/// leaves the frame exactly as it was.
/// </para>
/// </remarks>
/// <param name="FogColour">The colour a surface tends to with distance, linear.</param>
/// <param name="FogDensity">
/// The extinction per metre. A surface keeps <c>exp(-density * distance)</c> of its own colour; zero
/// is no fog at all.
/// </param>
/// <param name="Ambient">The flat light the level sits in.</param>
public readonly record struct SceneAtmosphere(Vector3f FogColour, float FogDensity, Vector3f Ambient);

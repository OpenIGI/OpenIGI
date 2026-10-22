using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>A hosted I.G.I. 2 level, brought up.</summary>
/// <param name="Soldiers">The soldiers' bodies.</param>
/// <param name="Detection">The detectors, which list the soldiers and the host's player.</param>
/// <param name="Ai">The level's AI.</param>
/// <param name="Run">The level's walk, which the host has its own walk run.</param>
public sealed record Igi2GuestLevelRun(Soldiers.Igi2Soldiers Soldiers, Igi2DetectionWorld Detection, Igi2AiWorld Ai, Igi2LevelRun Run);

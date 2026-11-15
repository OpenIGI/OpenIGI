using OpenIGI.Core.Numerics;
using OpenIGI.Core.Simulation;

namespace OpenIGI.Game.Hosting;

/// <summary>
/// What a level checkpoint records, for both games: the level's task tree and the gameplay that runs it -- its soldiers, vehicles,
/// bodies, scripts' machines, the random stream -- and nothing of what draws it, sounds it, loads it or connects it.
/// </summary>
/// <remarks>
/// <para>
/// By assembly and namespace: the engine's tasks (not its flow, whose loop and clock schedule frames rather than hold the level);
/// the script runtime's machines; the games' own code but for multiplayer and the menus; the seeded random stream. Presentation,
/// rendering, audio, formats and the host are outside. A type inside that is not state -- a definition, a registry, a service --
/// is marked <see cref="NotSimulationStateAttribute"/> where it is declared.
/// </para>
/// <para>Not retail: the original has no editor to rewind.</para>
/// </remarks>
public static class SimulationBoundary
{
    /// <summary>Gets the boundary.</summary>
    public static StateBoundary Level { get; } = new(IsState);

    private static bool IsState(Type type)
    {
        string space = type.Namespace ?? string.Empty;
        return type.Assembly.GetName().Name switch
        {
            "OpenIGI.Engine" => space.StartsWith("OpenIGI.Engine.Tasks", StringComparison.Ordinal),
            "OpenIGI.Scripting" => space.StartsWith("OpenIGI.Scripting.Runtime", StringComparison.Ordinal)
                || space.StartsWith("OpenIGI.Scripting.Qvm", StringComparison.Ordinal)
                || space.StartsWith("OpenIGI.Scripting.Expressions", StringComparison.Ordinal),
            "OpenIGI.Game" or "OpenIGI.Game.Igi1" or "OpenIGI.Game.Igi2" =>
                !space.StartsWith("OpenIGI.Game.Multiplayer", StringComparison.Ordinal)
                && !space.StartsWith("OpenIGI.Game.Menus", StringComparison.Ordinal),
            "OpenIGI.Core" => type == typeof(IgiRandom),

            // The bodies' animation as it plays -- the clips' clocks, the poses, the samplers' cursors -- whose root motion moves
            // every soldier; not the clips' data, which never changes.
            "OpenIGI.Formats" => PlayingAnimation.Contains(type.Name) && space == "OpenIGI.Formats.Animation",
            _ => false,
        };
    }

    private static readonly HashSet<string> PlayingAnimation = new(StringComparer.Ordinal)
    {
        "BoneObject", "BoneClip", "BonePose", "BoneTrackSampler", "BoneRotationSampler", "Igi2ClipSampler", "Igi2AnimationClip",
    };
}

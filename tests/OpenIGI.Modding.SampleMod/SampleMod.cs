using OpenIGI.Game.Events;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Modding;

namespace SampleMod;

/// <summary>A mod as a mod author writes one: what it changes is said by attributes, and its Configure is generated.</summary>
[OpenIgiMod]
public sealed partial class Sample
{
    /// <summary>The name of the script constant the mod adds.</summary>
    public const string AnswerConstant = "SampleMod_Answer";

    /// <summary>The name of the level system the mod adds.</summary>
    public const string GreetingSystem = "SampleMod.Greeting";

    /// <summary>The answer the level scripts read.</summary>
    [ScriptConstant(AnswerConstant)]
    public const int Answer = 42;

    /// <summary>Gets how many levels the mod has seen loaded.</summary>
    public int LevelsLoaded { get; private set; }

    // Runs as each I.G.I. 2 level comes up, right after its flow.
    [AddsLevelSystem<Igi2LevelBuild>(After = Igi2LevelSystems.Flow, Name = GreetingSystem)]
    private static void Greet(Igi2LevelBuild build)
    {
    }

    [OnGameEvent]
    private void Loaded(in LevelLoaded loaded) => LevelsLoaded++;
}

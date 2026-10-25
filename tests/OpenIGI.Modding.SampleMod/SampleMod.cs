using OpenIGI.Game.Events;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Modding;

namespace SampleMod;

/// <summary>A mod as a mod author writes one: one class, whose Configure says everything it changes.</summary>
public sealed class Sample : IOpenIgiMod
{
    /// <summary>The name of the script constant the mod adds.</summary>
    public const string AnswerConstant = "SampleMod_Answer";

    /// <summary>The name of the level system the mod adds.</summary>
    public const string GreetingSystem = "SampleMod.Greeting";

    /// <summary>The answer the level scripts read.</summary>
    public const int Answer = 42;

    /// <summary>Gets how many levels the mod has seen loaded.</summary>
    public int LevelsLoaded { get; private set; }

    /// <inheritdoc/>
    public void Configure(IModBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The greyscale pass's switch and strength, which its project names as the pass's Toggle and Parameters.
        builder.Settings.Bool("greyscale", false, "Drains the scene of its colour.");
        builder.Settings.Float("greyscale_amount", 1f, 0f, 1f, "How much of the scene's colour the greyscale pass drains.");

        builder.Scripts.Constant(AnswerConstant, Answer);

        // Runs as each I.G.I. 2 level comes up, right after its flow.
        builder.LevelSystems<Igi2LevelBuild>().InsertAfter(Igi2LevelSystems.Flow, GreetingSystem, static _ => { });

        builder.Events.On<LevelLoaded>(_ => LevelsLoaded++);
    }
}

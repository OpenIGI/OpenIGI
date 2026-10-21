using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Game.Level;
using OpenIGI.Modding;

[assembly: OpenIgiMod(typeof(SampleMod.Sample))]

namespace SampleMod;

/// <summary>Adds a script constant to every game, and a level system to I.G.I. 2's right after its flow.</summary>
public sealed class Sample : IOpenIgiMod
{
    /// <summary>The constant's name.</summary>
    public const string AnswerConstant = "SampleMod_Answer";

    /// <summary>The level system's name.</summary>
    public const string GreetingSystem = "SampleMod.Greeting";

    /// <inheritdoc/>
    public void Configure(IModBuilder builder)
    {
        builder.AddModule(new AnswerModule());
        builder.EditLevelSystems<Igi2LevelBuild>(systems =>
            systems.InsertAfter(Igi2LevelSystems.Flow, new LevelSystem<Igi2LevelBuild>(GreetingSystem, static _ => { })));
    }

    private sealed class AnswerModule : IContentModule
    {
        public string Name => "SampleMod.Answer";

        public void Register(ContentRegistrationContext context) => context.Natives.RegisterConstant(AnswerConstant, 42);

        public void LoadTables(ContentLoadContext context)
        {
        }
    }
}

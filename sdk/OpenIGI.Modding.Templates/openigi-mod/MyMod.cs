using OpenIGI.Engine.Titles;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Level;
using OpenIGI.Modding;

[assembly: OpenIgiMod(typeof(MyMod.Mod))]

namespace MyMod;

/// <summary>The mod: what it adds to the games its mod.json names, configured once for each before the game loads.</summary>
public sealed class Mod : IOpenIgiMod
{
    /// <summary>The name of the level system this mod adds.</summary>
    public const string ArrivalSystem = "MyMod.Arrival";

    /// <inheritdoc/>
    public void Configure(IModBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Content modules register after the game's own: task types (or replacements of the game's, through
        // builder.ReplaceTask), script natives and constants, and services.
        builder.AddModule(new Constants());

        // Level systems bring each mission up, in order; a mod's own runs among the game's, placed by their names.
        builder.EditLevelSystems<MissionLevelBuild>(systems => systems.InsertAfter(
            MissionSystemNames.Player,
            new LevelSystem<MissionLevelBuild>(ArrivalSystem, static build =>
            {
                // The player stands in the level here: build.Mission.Player, build.LevelRoot, build.Loaded ...
            })));
    }

    // A script constant the level scripts can read, as MyMod_Answer.
    private sealed class Constants : IContentModule
    {
        public string Name => "MyMod.Constants";

        public void Register(ContentRegistrationContext context) => context.Natives.RegisterConstant("MyMod_Answer", 42);

        public void LoadTables(ContentLoadContext context)
        {
        }
    }
}

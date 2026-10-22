#if (kind == 'shader')
using OpenIGI.Modding;

namespace MyMod;

/// <summary>The mod: everything it changes, said in Configure. Its pass itself is named in the project (ModPostPass).</summary>
public sealed class Mod : IOpenIgiMod
{
    /// <inheritdoc/>
    public void Configure(IModBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The pass's switch and strength: saved as mod_MOD_SETTING_vignette and mod_MOD_SETTING_vignette_amount, the names
        // the project gives the pass as its Toggle and Parameters, and listed on the settings screen's Mods page.
        builder.Settings.Bool("vignette", true, "Darkens the picture toward its edges.");
        builder.Settings.Float("vignette_amount", 0.5f, 0f, 1f, "How dark the vignette makes the corners.");
    }
}
#else
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Events;
using OpenIGI.Game.Hosting;
using OpenIGI.Modding;

namespace MyMod;

/// <summary>The mod: everything it changes, said in Configure, once for each game it is for.</summary>
public sealed class Mod : IOpenIgiMod
{
    /// <inheritdoc/>
    public void Configure(IModBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A setting of its own, saved as mod_MOD_SETTING_loud and listed on the settings screen's Mods page.
        BoolCVar loud = builder.Settings.Bool("loud", false, "Says so whenever a guard goes down.");

        // A constant the level scripts can read.
        builder.Scripts.Constant("MyMod_Answer", 42);

        // A step of the mission's bring-up, placed among the game's own by their names (MissionSystemNames).
        builder.LevelSystems<MissionLevelBuild>().InsertAfter(MissionSystemNames.Player, "MyMod.Arrive", build =>
        {
            // The player stands in the level here: build.Mission, build.LevelRoot, build.Loaded ...
        });

        // Told as each guard dies; the other events are in OpenIGI.Game.Events.
        builder.Events.On<HumanKilled>(killed =>
        {
            if (loud.Value)
            {
                // ...
            }
        });

        // Once a session has loaded its tables: its services, files and log, which the mod reads from then on.
        builder.OnStarted(session => session.Logger.Information("Mods", $"{builder.Manifest.Name} is on."));

        // Further: builder.Tasks.Replace<MySoldier>("HumanSoldier"), builder.MountTitleData(...), builder.AddModule(...),
        // and, from OpenIGI.Modding.Presentation, builder.Presenters(p => p.ForHud(...)).
    }
}
#endif

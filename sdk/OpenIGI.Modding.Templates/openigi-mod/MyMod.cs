using OpenIGI.Game.Events;
using OpenIGI.Game.Hosting;
using OpenIGI.Modding;

namespace MyMod;

/// <summary>The mod. Its Configure is generated from the attributes below; anything else goes in OnConfigure.</summary>
[OpenIgiMod]
public sealed partial class Mod
{
    /// <summary>A constant the level scripts can read, as MyMod_Answer.</summary>
    [ScriptConstant("MyMod_Answer")]
    public const int Answer = 42;

    // A step of the mission's bring-up, placed among the game's own by their names (MissionSystemNames).
    [AddsLevelSystem<MissionLevelBuild>(After = MissionSystemNames.Player)]
    private static void Arrive(MissionLevelBuild build)
    {
        // The player stands in the level here: build.Mission, build.LevelRoot, build.Loaded ...
    }

    // Told as each guard dies; the other events are in OpenIGI.Game.Events.
    [OnGameEvent]
    private static void GuardDown(in HumanKilled killed)
    {
    }

    // Anything the attributes do not say: builder.ReplaceTask, builder.AddModule, builder.MountTitleData ...
    partial void OnConfigure(IModBuilder builder)
    {
    }
}

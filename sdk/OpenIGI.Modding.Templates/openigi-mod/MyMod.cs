#if (kind == 'shader')
using OpenIGI.Modding;

namespace MyMod;

/// <summary>
/// The mod: its pass is named in the project (ModPostPass) and its settings in Settings.cs, so it has nothing to configure
/// here; code of its own goes in OnConfigure, or as the attributes the modding API offers.
/// </summary>
[OpenIgiMod]
public sealed partial class Mod
{
}
#else
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
#endif

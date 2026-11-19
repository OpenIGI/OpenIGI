using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>Which of Project I.G.I.'s files a session makes the host's on every player's machine.</summary>
public static class Igi1GameplayFiles
{
    /// <summary>Gets the first game's layout.</summary>
    /// <remarks>
    /// Every level plays from the shared tables and the location's models; a level's own folder is
    /// <c>missions/location0/level&lt;n&gt;</c>, of which its textures, lightmaps and sounds only look or sound.
    /// </remarks>
    public static GameplayFileLayout Layout { get; } = new(
        SharedFolders: ["ammo", "weapons", "humanplayer", "physicsobj", "magicobj", "material", "animtrigger", "common/anims", "common/ai", "missions/location0/common/models"],
        SharedFiles: ["lod.qvm", "common/common.dat", "common/common.mtp", "missions/location0/common/location0.dat", "missions/location0/common/location0.mtp"],
        LevelRoot: "missions/location0/level",
        LooksFolders: ["textures", "lightmaps", "sounds"],
        NeverExtensions: [".tex", ".qsc", ".bmp", ".tga", ".txt", ".log", ".bak", ".exe", ".dll", ".lnk"]);
}

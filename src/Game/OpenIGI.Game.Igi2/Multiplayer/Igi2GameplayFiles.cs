using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>Which of I.G.I. 2's files a session makes the host's on every player's machine.</summary>
/// <remarks>
/// <para>
/// Every level plays from the shared tables in <c>weapons</c>, <c>humanplayer</c>, <c>physicsobj</c>, <c>material</c>,
/// <c>animtrigger</c> and <c>common</c> (its AI settings, and the models -- shapes, skeletons and animations together in
/// <c>common/models/*.res</c>). A map plays from the folders its mission names: its own, and its location's shared one
/// (<c>missions/location1/common</c>, <c>missions/multiplayer/common</c>) -- objects, graphs, AI scripts, heightmaps, models.
/// </para>
/// <para>
/// Left out, as only looking or sounding: textures, lightmaps, environment maps, sounds, sprites and fonts. That is also
/// where retail and GOG copies differ most (GOG's fonts, its four extra languages, a few voice recordings), so those never
/// make two players' copies count as different.
/// </para>
/// </remarks>
public static class Igi2GameplayFiles
{
    /// <summary>Gets the second game's layout.</summary>
    public static GameplayFileLayout Layout { get; } = new(
        SharedFolders: ["weapons", "humanplayer", "physicsobj", "material", "animtrigger", "common"],
        SharedFiles: ["lod.qvm", "magicobjconfig.qvm"],
        LevelRoot: "missions/",
        LooksFolders: ["textures", "lightmaps", "envmaps", "sounds", "sprites", "fonts"],
        NeverExtensions: [".tex", ".qsc", ".bmp", ".tga", ".jpg", ".mp3", ".wav", ".fnt", ".txt", ".log", ".bak", ".exe", ".dll", ".lnk"]);
}

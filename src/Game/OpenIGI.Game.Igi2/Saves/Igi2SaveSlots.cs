using System.Globalization;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>A save game the load list shows: its slot and the line it is listed as.</summary>
/// <param name="Slot">The slot, or <see cref="Igi2SaveSlots.RestartMission"/>.</param>
/// <param name="Text">The line, or the key it is looked up by.</param>
/// <param name="IsTranslated">Whether the line is a key into the strings.</param>
public readonly record struct Igi2SaveListing(int Slot, string Text, bool IsTranslated);

/// <summary>
/// I.G.I. 2's save game slots (game.c): the shared slots (<see cref="SaveSlots"/>) under the game's own <c>savegames</c> folder as
/// <c>.igi2save</c> files, which retail reads and writes too; the load list its menus show; and the carry-over a mission leaves for
/// the next, the player's weapons.
/// </summary>
/// <param name="directory">The folder <c>savegames</c> lies in: the game's own folder for retail's saves.</param>
public sealed class Igi2SaveSlots(string directory) : SaveSlots(directory, Folder, Extension, Igi2SaveFormat.Version)
{
    /// <summary>The folder the slots lie in.</summary>
    public const string Folder = "savegames";

    /// <summary>The slot files' extension.</summary>
    public const string Extension = ".igi2save";

    /// <summary>The id of the load list's last line, which restarts the mission: <c>12345678</c>.</summary>
    public const int RestartMission = 12345678;

    /// <summary>The key of the load list's restart line.</summary>
    public const string RestartMissionKey = "menusystem/Restart Mission";

    /// <summary>The key of the mission time's label.</summary>
    public const string MissionTimeKey = "menusystem/Mission Time";

    /// <summary>The type of the task that holds what a human carries, <c>WeaponHandler</c> (<c>Weapon_eQTaskType</c>, weapon.c).</summary>
    public const string WeaponPart = "WeaponHandler";

    /// <summary>The key of the quick save's tag.</summary>
    public const string QuickSaveKey = "menusystem/Quicksave";

    /// <summary>A slot's file, <c>Game_CreateSaveFilenameForSlotEx</c>: <c>savegames/%d-%d-%d-%d.igi2save</c>.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The path under the folder.</returns>
    public static string SlotFileName(SaveOwner owner, int slot) => FileName(Folder, Extension, owner, slot);

    /// <summary>Reads a slot's file name back, <c>Game_ParseSaveGameFilename</c>.</summary>
    /// <param name="fileName">The file's name, with or without its folder.</param>
    /// <param name="owner">Whose it is.</param>
    /// <param name="slot">Its slot.</param>
    /// <returns><see langword="true"/> for a name of that form.</returns>
    public static bool TryParseSlotFileName(string fileName, out SaveOwner owner, out int slot) =>
        TryParseFileName(fileName, Extension, out owner, out slot);

    /// <summary>The carry-over a mission leaves, <c>Game_GetCarryoverFilename</c>: <c>savegames/missions/co%02d%02d%02d.qsc</c>.</summary>
    /// <param name="owner">Whose, and the mission it is for.</param>
    /// <returns>The path under the folder.</returns>
    public static string CarryOverFileName(SaveOwner owner) =>
        string.Create(CultureInfo.InvariantCulture, $"savegames/missions/co{owner.Profile:00}{owner.Difficulty:00}{owner.Mission:00}.qsc");

    /// <summary>
    /// The load list, <c>Game_FillSaveGameScrollListBox</c>: each valid save of the six as <c>(date time) Mission Time: h:mm:ss</c>, the
    /// quick save tagged <c>[Quicksave]</c>, then the line that restarts the mission.
    /// </summary>
    /// <param name="owner">Whose.</param>
    /// <param name="translate">The localized text of a key.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<Igi2SaveListing> Listings(SaveOwner owner, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);
        List<Igi2SaveListing> lines = [];
        for (int slot = 0; slot <= SaveRules.QuickSave; slot++)
        {
            SaveInfo info = Check(owner, slot);
            if (!info.IsValid)
            {
                continue;
            }

            DateTime written = WrittenAt(owner, slot);
            string when = string.Create(CultureInfo.CurrentCulture, $"({written:d} {written:T})");
            string description = $"{translate(MissionTimeKey)}: {SaveRules.MissionTime(info.Ticks)}";
            lines.Add(new Igi2SaveListing(
                slot,
                slot >= SaveRules.MenuSlots ? $"{when} [{translate(QuickSaveKey)}] {description}" : $"{when} {description}",
                false));
        }

        lines.Add(new Igi2SaveListing(RestartMission, RestartMissionKey, true));
        return lines;
    }

    /// <summary>
    /// Leaves the player's weapons for the next mission, <c>Game_SaveCarryover</c>: what it carries, written as its weapon task's
    /// state alone (<c>WeaponHandler</c>, against the player), for the mission given.
    /// </summary>
    /// <param name="owner">Whose, and the mission it is for.</param>
    /// <param name="player">The player's task.</param>
    /// <returns><see langword="true"/> once written.</returns>
    public bool SaveCarryOver(SaveOwner owner, GameTask player) =>
        WriteText(CarryOverFileName(owner), SaveScript.CreatePart(player, WeaponPart, Igi2SaveFormat.Info(owner.Mission, 0), Igi2SaveFormat.Context()));

    /// <summary>
    /// Gives the player the weapons the last mission left, <c>Game_LoadCarryOver</c>, when the player carries weapons over.
    /// </summary>
    /// <param name="owner">Whose, and the mission.</param>
    /// <param name="root">The task the carry-over is loaded onto.</param>
    /// <returns>The header; not valid when there is none.</returns>
    public SaveInfo LoadCarryOver(SaveOwner owner, GameTask root) =>
        TryRead(CarryOverFileName(owner), out byte[] file) ? SaveScript.Load(file, root, Igi2SaveFormat.Context()) : default;
}

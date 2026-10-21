using System.Globalization;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>Whose save games these are: the profile, the difficulty it plays at, and the mission.</summary>
/// <param name="Profile">The active profile, <c>Config_tConfig.iActiveProfile</c>.</param>
/// <param name="Difficulty">Its difficulty, <c>GameDifficulty_e</c>: 0 easy, 1 normal, 2 hard.</param>
/// <param name="Mission">The mission, <c>Game_iMissionID</c>.</param>
public readonly record struct Igi2SaveOwner(int Profile, int Difficulty, int Mission);

/// <summary>A save game the load list shows: its slot and the line it is listed as.</summary>
/// <param name="Slot">The slot, or <see cref="Igi2SaveSlots.RestartMission"/>.</param>
/// <param name="Text">The line, or the key it is looked up by.</param>
/// <param name="IsTranslated">Whether the line is a key into the strings.</param>
public readonly record struct Igi2SaveListing(int Slot, string Text, bool IsTranslated);

/// <summary>
/// The save game slots of game.c: each a file under the user's <c>savegames</c> folder named by profile, difficulty, mission and slot,
/// five slots and a quick save, written as <see cref="Igi2SaveGame"/> writes them and read whether this game or retail wrote them; and
/// the carry-over a mission leaves for the next, the player's weapons.
/// </summary>
/// <param name="directory">The folder <c>savegames</c> lies in: the game's own folder for retail's saves.</param>
public sealed class Igi2SaveSlots(string directory)
{
    /// <summary>The slots the load list offers, <c>Game_GetNumberOfValidSaveGames</c>' five.</summary>
    public const int MenuSlots = 5;

    /// <summary>The quick save's slot, the sixth.</summary>
    public const int QuickSave = 5;

    /// <summary>The id of the load list's last line, which restarts the mission: <c>12345678</c>.</summary>
    public const int RestartMission = 12345678;

    /// <summary>The key of the load list's restart line.</summary>
    public const string RestartMissionKey = "menusystem/Restart Mission";

    /// <summary>The key of the mission time's label.</summary>
    public const string MissionTimeKey = "menusystem/Mission Time";

    /// <summary>The key of the quick save's tag.</summary>
    public const string QuickSaveKey = "menusystem/Quicksave";

    private readonly string _directory = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <summary>Gets the folder <c>savegames</c> lies in.</summary>
    public string Directory => _directory;

    /// <summary>A slot's file, <c>Game_CreateSaveFilenameForSlotEx</c>: <c>savegames/%d-%d-%d-%d.igi2save</c>.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The path under the folder.</returns>
    public static string SlotFileName(Igi2SaveOwner owner, int slot) =>
        string.Create(CultureInfo.InvariantCulture, $"savegames/{owner.Profile}-{owner.Difficulty}-{owner.Mission}-{slot}.igi2save");

    /// <summary>Reads a slot's file name back, <c>Game_ParseSaveGameFilename</c>.</summary>
    /// <param name="fileName">The file's name, with or without its folder.</param>
    /// <param name="owner">Whose it is.</param>
    /// <param name="slot">Its slot.</param>
    /// <returns><see langword="true"/> for a name of that form.</returns>
    public static bool TryParseSlotFileName(string fileName, out Igi2SaveOwner owner, out int slot)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        owner = default;
        slot = 0;
        string name = Path.GetFileName(fileName);
        if (!name.EndsWith(".igi2save", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = name[..^".igi2save".Length].Split('-');
        int[] values = new int[4];
        if (parts.Length != 4 || !parts.Select((p, i) => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i])).All(static ok => ok))
        {
            return false;
        }

        owner = new Igi2SaveOwner(values[0], values[1], values[2]);
        slot = values[3];
        return true;
    }

    /// <summary>The carry-over a mission leaves, <c>Game_GetCarryoverFilename</c>: <c>savegames/missions/co%02d%02d%02d.qsc</c>.</summary>
    /// <param name="owner">Whose, and the mission it is for.</param>
    /// <returns>The path under the folder.</returns>
    public static string CarryOverFileName(Igi2SaveOwner owner) =>
        string.Create(CultureInfo.InvariantCulture, $"savegames/missions/co{owner.Profile:00}{owner.Difficulty:00}{owner.Mission:00}.qsc");

    /// <summary>The slots a difficulty allows, <c>Game_GetAvailableSaveGameSlots</c>: three on easy, two on normal, none on hard.</summary>
    /// <param name="difficulty">The difficulty.</param>
    /// <returns>The slots.</returns>
    public static int AvailableSlots(int difficulty) => difficulty switch
    {
        0 => 3,
        1 => 2,
        _ => 0,
    };

    /// <summary>A mission time as the lists show it, <c>Game_GetTimeStringFromGameTicks</c>: hours, minutes and seconds of thirty ticks.</summary>
    /// <param name="ticks">The ticks.</param>
    /// <returns><c>h:mm:ss</c>.</returns>
    public static string MissionTime(int ticks)
    {
        int seconds = ticks / 30;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 3600 % 60:00}");
    }

    /// <summary>The full path of a file under the folder.</summary>
    /// <param name="relative">The path under it.</param>
    /// <returns>The full path.</returns>
    public string PathOf(string relative) => Path.Combine(_directory, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Saves to a slot, <c>Game_SaveToSlot</c>: the level's state under a header of the mission and the player's mission time.
    /// </summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="root">The task the state is written from.</param>
    /// <param name="ticks">The player's mission time.</param>
    /// <param name="glass">The level's panes of glass, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> once written: retail's "Saving complete", otherwise its "Saving failed".</returns>
    public bool Save(Igi2SaveOwner owner, int slot, GameTask root, int ticks, Interaction.Igi2GlassContainer? glass = null) =>
        WriteText(SlotFileName(owner, slot), Igi2SaveGame.Create(root, new Igi2SaveInfo(Igi2SaveInfo.CurrentVersion, owner.Mission, ticks), glass));

    /// <summary>Reads a slot's header, <c>SaveGame_Check</c>.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The header; not valid for an empty slot.</returns>
    public Igi2SaveInfo Check(Igi2SaveOwner owner, int slot) =>
        TryRead(SlotFileName(owner, slot), out byte[] file) ? Igi2SaveGame.CheckFile(file) : default;

    /// <summary>Loads a slot onto a level's tasks, <c>Game_LoadFromSlot</c>'s load once its header is valid.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="root">The level's root.</param>
    /// <param name="natives">The namespace a text save's constants resolve in.</param>
    /// <param name="functions">The function table.</param>
    /// <param name="glass">The level's panes of glass, or <see langword="null"/>.</param>
    /// <returns>The header; not valid when nothing was loaded: retail's "Loading failed".</returns>
    public Igi2SaveInfo Load(
        Igi2SaveOwner owner, int slot, GameTask root, NativeRegistry natives, Igi2ScriptFunctions functions, Interaction.Igi2GlassContainer? glass = null)
    {
        if (!TryRead(SlotFileName(owner, slot), out byte[] file) || !Igi2SaveGame.CheckFile(file).IsValid)
        {
            return default;
        }

        return Igi2SaveGame.LoadFile(file, root, natives, functions, glass: glass);
    }

    /// <summary>The newest valid save of the six, by when its file was written, <c>Game_FindNewestSaveGame</c>.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">Its slot.</param>
    /// <returns><see langword="true"/> when there is one.</returns>
    public bool TryFindNewest(Igi2SaveOwner owner, out int slot)
    {
        slot = -1;
        DateTime newest = DateTime.MinValue;
        for (int i = 0; i <= QuickSave; i++)
        {
            if (Check(owner, i).IsValid && File.GetLastWriteTimeUtc(PathOf(SlotFileName(owner, i))) is var written && written > newest)
            {
                newest = written;
                slot = i;
            }
        }

        return slot >= 0;
    }

    /// <summary>The valid saves of a mission in the five menu slots, <c>Game_GetNumberOfValidSaveGames</c>.</summary>
    /// <param name="owner">Whose, and the mission.</param>
    /// <returns>The count.</returns>
    public int CountValid(Igi2SaveOwner owner) => Enumerable.Range(0, MenuSlots).Count(i => Check(owner, i).IsValid);

    /// <summary>
    /// The load list, <c>Game_FillSaveGameScrollListBox</c>: each valid save of the six as <c>(date time) Mission Time: h:mm:ss</c>, the
    /// quick save tagged <c>[Quicksave]</c>, then the line that restarts the mission.
    /// </summary>
    /// <param name="owner">Whose.</param>
    /// <param name="translate">The localized text of a key.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<Igi2SaveListing> Listings(Igi2SaveOwner owner, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);
        List<Igi2SaveListing> lines = [];
        for (int slot = 0; slot <= QuickSave; slot++)
        {
            Igi2SaveInfo info = Check(owner, slot);
            if (!info.IsValid)
            {
                continue;
            }

            DateTime written = File.GetLastWriteTime(PathOf(SlotFileName(owner, slot)));
            string when = string.Create(CultureInfo.CurrentCulture, $"({written:d} {written:T})");
            string description = $"{translate(MissionTimeKey)}: {MissionTime(info.Ticks)}";
            lines.Add(new Igi2SaveListing(
                slot,
                slot >= MenuSlots ? $"{when} [{translate(QuickSaveKey)}] {description}" : $"{when} {description}",
                false));
        }

        lines.Add(new Igi2SaveListing(RestartMission, RestartMissionKey, true));
        return lines;
    }

    /// <summary>
    /// Leaves the player's weapons for the next mission, <c>Game_SaveCarryover</c>: what it carries, written as its weapon's state
    /// alone, for the mission given.
    /// </summary>
    /// <param name="owner">Whose, and the mission it is for.</param>
    /// <param name="player">The player's task.</param>
    /// <returns><see langword="true"/> once written.</returns>
    public bool SaveCarryOver(Igi2SaveOwner owner, GameTask player) =>
        WriteText(CarryOverFileName(owner), Igi2SaveGame.CreatePart(player, "Weapon", new Igi2SaveInfo(Igi2SaveInfo.CurrentVersion, owner.Mission, 0)));

    /// <summary>
    /// Gives the player the weapons the last mission left, <c>Game_LoadCarryOver</c>, when the player carries weapons over.
    /// </summary>
    /// <param name="owner">Whose, and the mission.</param>
    /// <param name="root">The task the carry-over is loaded onto.</param>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The function table.</param>
    /// <returns>The header; not valid when there is none.</returns>
    public Igi2SaveInfo LoadCarryOver(Igi2SaveOwner owner, GameTask root, NativeRegistry natives, Igi2ScriptFunctions functions) =>
        TryRead(CarryOverFileName(owner), out byte[] file) && Igi2SaveGame.CheckFile(file).IsValid
            ? Igi2SaveGame.LoadFile(file, root, natives, functions)
            : default;

    private bool WriteText(string relative, string? script)
    {
        if (script is null)
        {
            return false;
        }

        try
        {
            string path = PathOf(relative);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, script, System.Text.Encoding.Latin1);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool TryRead(string relative, out byte[] file)
    {
        try
        {
            string path = PathOf(relative);
            file = File.Exists(path) ? File.ReadAllBytes(path) : [];
        }
        catch (IOException)
        {
            file = [];
        }
        catch (UnauthorizedAccessException)
        {
            file = [];
        }

        return file.Length > 0;
    }
}

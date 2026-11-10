using System.Globalization;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Saves;

/// <summary>Whose save games these are: the profile, the difficulty it plays at, and the mission.</summary>
/// <param name="Profile">The active profile, <c>Config_tConfig.iActiveProfile</c>.</param>
/// <param name="Difficulty">Its difficulty, <c>GameDifficulty_e</c>: 0 easy, 1 normal, 2 hard.</param>
/// <param name="Mission">The mission, <c>Game_iMissionID</c>.</param>
public readonly record struct SaveOwner(int Profile, int Difficulty, int Mission);

/// <summary>
/// The rules of the save games, I.G.I. 2's (game.c, humanplayer.c): the slots there are and how many saves a difficulty allows a mission.
/// Project I.G.I., which shipped with no save games, is played by the same ones.
/// </summary>
public static class SaveRules
{
    /// <summary>The slots the computer's save screen and the load list offer, <c>Game_GetNumberOfValidSaveGames</c>' five.</summary>
    public const int MenuSlots = 5;

    /// <summary>The quick save's slot, the sixth.</summary>
    public const int QuickSave = 5;

    /// <summary>
    /// The saves a mission allows at a difficulty, the full charge of the computer's battery (<c>HumanPlayer_GetBatteryLevel</c>):
    /// five, three, or none on hard.
    /// </summary>
    /// <param name="difficulty">The difficulty.</param>
    /// <returns>The saves.</returns>
    public static int SavesAllowed(int difficulty) => difficulty switch
    {
        0 => 5,
        1 => 3,
        _ => 0,
    };

    /// <summary>When a save was written, as the lists show it, <c>Game_GetSaveGameTimeDateString</c>: <c>(date time)</c>.</summary>
    /// <param name="written">When.</param>
    /// <returns>The date and the time, in the player's own form.</returns>
    public static string WrittenLabel(DateTime written) => string.Create(CultureInfo.CurrentCulture, $"({written:d} {written:T})");

    /// <summary>A mission time as the lists show it, <c>Game_GetTimeStringFromGameTicks</c>: hours, minutes and seconds of thirty ticks.</summary>
    /// <param name="ticks">The ticks.</param>
    /// <returns><c>h:mm:ss</c>.</returns>
    public static string MissionTime(int ticks)
    {
        int seconds = ticks / 30;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 3600 % 60:00}");
    }
}

/// <summary>
/// The save game slots of game.c: each a file under a folder, named by profile, difficulty, mission and slot, five slots and a quick
/// save, written as <see cref="SaveScript"/> writes them and read whether this game or retail wrote them.
/// </summary>
/// <param name="directory">The directory the slots' folder lies in.</param>
/// <param name="folder">The folder under it, <c>savegames</c>.</param>
/// <param name="extension">The files' extension, with its dot.</param>
/// <param name="version">The format's version: what is written, and the least that is read.</param>
public class SaveSlots(string directory, string folder, string extension, int version)
{
    private readonly string _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    private readonly string _folder = folder ?? throw new ArgumentNullException(nameof(folder));
    private readonly string _extension = extension ?? throw new ArgumentNullException(nameof(extension));
    private readonly SaveContext _headers = new() { Version = version };

    /// <summary>Gets the directory the slots' folder lies in.</summary>
    public string Directory => _directory;

    /// <summary>Gets the format's version.</summary>
    public int Version => _headers.Version;

    /// <summary>A slot's file name, <c>Game_CreateSaveFilenameForSlotEx</c>: <c>folder/%d-%d-%d-%d.ext</c>.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="extension">The extension, with its dot.</param>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The path under the directory.</returns>
    public static string FileName(string folder, string extension, SaveOwner owner, int slot) =>
        string.Create(CultureInfo.InvariantCulture, $"{folder}/{owner.Profile}-{owner.Difficulty}-{owner.Mission}-{slot}{extension}");

    /// <summary>Reads a slot's file name back, <c>Game_ParseSaveGameFilename</c>.</summary>
    /// <param name="fileName">The file's name, with or without its folder.</param>
    /// <param name="extension">The extension, with its dot.</param>
    /// <param name="owner">Whose it is.</param>
    /// <param name="slot">Its slot.</param>
    /// <returns><see langword="true"/> for a name of that form.</returns>
    public static bool TryParseFileName(string fileName, string extension, out SaveOwner owner, out int slot)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(extension);
        owner = default;
        slot = 0;
        string name = Path.GetFileName(fileName);
        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = name[..^extension.Length].Split('-');
        int[] values = new int[4];
        if (parts.Length != 4 || !parts.Select((p, i) => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i])).All(static ok => ok))
        {
            return false;
        }

        owner = new SaveOwner(values[0], values[1], values[2]);
        slot = values[3];
        return true;
    }

    /// <summary>A slot's file.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The path under the directory.</returns>
    public string SlotFile(SaveOwner owner, int slot) => FileName(_folder, _extension, owner, slot);

    /// <summary>The full path of a file under the directory.</summary>
    /// <param name="relative">The path under it.</param>
    /// <returns>The full path.</returns>
    public string PathOf(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        return Path.Combine(_directory, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>When a slot's file was last written, in local time.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The time.</returns>
    public DateTime WrittenAt(SaveOwner owner, int slot) => File.GetLastWriteTime(PathOf(SlotFile(owner, slot)));

    /// <summary>
    /// Saves to a slot, <c>Game_SaveToSlot</c>: the level's state under a header of the mission and the player's mission time.
    /// </summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="root">The task the state is written from.</param>
    /// <param name="ticks">The player's mission time.</param>
    /// <param name="context">What the level is written with.</param>
    /// <returns><see langword="true"/> once written: retail's "Saving complete", otherwise its "Saving failed".</returns>
    public bool Save(SaveOwner owner, int slot, GameTask root, int ticks, SaveContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return WriteText(SlotFile(owner, slot), SaveScript.Create(root, context.Info(owner.Mission, ticks), context));
    }

    /// <summary>Reads a slot's header, <c>SaveGame_Check</c>.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The header; not valid for an empty slot.</returns>
    public SaveInfo Check(SaveOwner owner, int slot) =>
        TryRead(SlotFile(owner, slot), out byte[] file) ? SaveScript.Check(file, _headers) : default;

    /// <summary>Loads a slot onto a level's tasks, <c>Game_LoadFromSlot</c>'s load once its header is valid.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="root">The level's root.</param>
    /// <param name="context">What the level is read with.</param>
    /// <returns>The header; not valid when nothing was loaded: retail's "Loading failed".</returns>
    public SaveInfo Load(SaveOwner owner, int slot, GameTask root, SaveContext context) =>
        TryRead(SlotFile(owner, slot), out byte[] file) ? SaveScript.Load(file, root, context) : default;

    /// <summary>The newest valid save of the six, by when its file was written, <c>Game_FindNewestSaveGame</c>.</summary>
    /// <param name="owner">Whose.</param>
    /// <param name="slot">Its slot.</param>
    /// <returns><see langword="true"/> when there is one.</returns>
    public bool TryFindNewest(SaveOwner owner, out int slot)
    {
        slot = -1;
        DateTime newest = DateTime.MinValue;
        for (int i = 0; i <= SaveRules.QuickSave; i++)
        {
            if (Check(owner, i).IsValid && File.GetLastWriteTimeUtc(PathOf(SlotFile(owner, i))) is var written && written > newest)
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
    public int CountValid(SaveOwner owner) => Enumerable.Range(0, SaveRules.MenuSlots).Count(i => Check(owner, i).IsValid);

    /// <summary>Writes a script to a file under the directory, making its folder.</summary>
    /// <param name="relative">The path under the directory.</param>
    /// <param name="script">The script, or <see langword="null"/> for none.</param>
    /// <returns><see langword="true"/> once written.</returns>
    public bool WriteText(string relative, string? script)
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

    /// <summary>Reads a file under the directory.</summary>
    /// <param name="relative">The path under the directory.</param>
    /// <param name="file">Its bytes; empty when there is none.</param>
    /// <returns><see langword="true"/> for a file with something in it.</returns>
    public bool TryRead(string relative, out byte[] file)
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

/// <summary>What is kept of the save games for a session: the slot to load as the next level comes up.</summary>
/// <remarks><c>Game_nMenuLoadFromSlot</c>, <c>-1</c> for none: a load asked for from a menu restarts the mission and loads into it.</remarks>
public class SaveSession
{
    /// <summary>Gets or sets the slot to load as the next level starts, or <c>-1</c>.</summary>
    public int PendingLoadSlot { get; set; } = -1;

    /// <summary>Takes back the slot asked for, leaving none, as the level that loads it starts.</summary>
    /// <returns>The slot, or <c>-1</c>.</returns>
    public int TakePendingLoadSlot()
    {
        int slot = PendingLoadSlot;
        PendingLoadSlot = -1;
        return slot;
    }
}

using System.Security.Cryptography;
using System.Text.Json;

namespace OpenIGI.Tools.Release;

/// <summary>
/// The community maps' side of the release tool: the hashes of a game's own files, which the master server keeps so that no
/// map carries one (a map names the game's files it uses; each player's game gives them) and none names a file falsely.
/// </summary>
/// <remarks>
/// Hashes only: nothing of the game leaves the machine. Loaded with
/// <c>curl -X POST -H "Authorization: Bearer $ADMIN_TOKEN" --data @hashes.json https://master.openigi.com/v1/maps/admin/game-files</c>,
/// once for each game's files together (the list replaces the last).
/// </remarks>
internal static class MapsCommand
{
    internal const string Usage =
        "usage: maps game-files <game folder> [<game folder>...] --out <hashes.json>\n"
        + "       (the SHA-256 of every file of the games given, for the master server's /v1/maps/admin/game-files)";

    internal static int Run(ToolContext context, IReadOnlyList<string> args)
    {
        if (args is not ["game-files", ..])
        {
            return context.Usage(Usage);
        }

        List<string> folders = [];
        string? output = null;
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] == "--out")
            {
                if (i + 1 >= args.Count)
                {
                    return context.Usage(Usage, "--out needs a file.");
                }

                output = args[++i];
            }
            else
            {
                folders.Add(args[i]);
            }
        }

        if (folders.Count == 0 || output is null)
        {
            return context.Usage(Usage, "Name the game folders and --out.");
        }

        SortedSet<string> hashes = new(StringComparer.Ordinal);
        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                return context.Fail($"No folder {folder}.");
            }

            int files = 0;
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                using FileStream stream = File.OpenRead(file);
                hashes.Add(Convert.ToHexStringLower(SHA256.HashData(stream)));
                files++;
            }

            context.Info($"{folder}: {files} files.");
        }

        File.WriteAllText(output, JsonSerializer.Serialize(hashes.ToArray()));
        context.Info($"Wrote {hashes.Count} hashes to {output}.");
        return 0;
    }
}

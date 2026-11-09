using System.Text.Json;
using OpenIGI.Formats.World;
using OpenIGI.Tools;

namespace OpenIGI.Tools.ResExtract;

/// <summary>
/// <c>ResExtract kit</c>: copies named assets out of an I.G.I. 2 installation into one flat folder -- models with every
/// texture their pack instances sample, clips, sounds and sprites -- with a <c>kit.json</c> saying which texture each of a
/// model's material slots draws with, so another title can embed and draw them without the sequel's packs.
/// </summary>
internal static class KitExport
{
    // The sequel's common packs, in the order the game opens them; a later one's entry of the same name wins.
    private static readonly string[] Packs = ["common", "new"];

    /// <summary>Runs the command.</summary>
    /// <param name="ctx">The tool context.</param>
    /// <param name="readArchive">Reads an archive's entries.</param>
    /// <returns>The exit code.</returns>
    public static int Run(ToolContext ctx, Func<string, IReadOnlyList<ArchiveItem>> readArchive)
    {
        IReadOnlyList<string> args = ctx.Arguments;
        if (args.Count < 3)
        {
            return ctx.Usage(Usage, "usage: " + Usage);
        }

        string root = args[1];
        string output = Path.GetFullPath(args[2]);
        List<string> models = Option(args, "--models");
        List<string> clips = Option(args, "--anims");
        List<string> sounds = Option(args, "--sounds");
        List<string> sprites = Option(args, "--sprites");
        Directory.CreateDirectory(output);

        // Every entry of the archives the assets can be in, by bare file name.
        Dictionary<string, ReadOnlyMemory<byte>> entries = new(StringComparer.OrdinalIgnoreCase);
        void Mount(string relative)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path))
            {
                return;
            }

            foreach (ArchiveItem item in readArchive(path))
            {
                entries[item.Name[(item.Name.LastIndexOf('/') + 1)..]] = item.Data;
            }
        }

        foreach (string pack in Packs)
        {
            Mount($"COMMON/models/{pack}.res");
            Mount($"COMMON/textures/{pack}.res");
        }

        Mount("COMMON/textures/textures.res");
        Mount("COMMON/sounds/sounds.res");
        Mount("WEAPONS/weapons.res");

        int missing = 0;
        bool Copy(string file)
        {
            if (!entries.TryGetValue(file, out ReadOnlyMemory<byte> data))
            {
                ctx.Error.WriteLine($"missing: {file}");
                missing++;
                return false;
            }

            File.WriteAllBytes(Path.Combine(output, file), data.ToArray());
            ctx.Out.WriteLine($"{data.Length,9}  {file}");
            return true;
        }

        // Each model's material slots, through the last pack whose instances name it.
        using MemoryStream manifest = new();
        using (Utf8JsonWriter json = new(manifest, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("models");
            foreach (string model in models)
            {
                string[]? slots = null;
                foreach (string pack in Packs)
                {
                    string mtp = Path.Combine(root, "COMMON", $"{pack}.mtp");
                    if (!File.Exists(mtp))
                    {
                        continue;
                    }

                    MtpPack read = MtpPack.Read(File.ReadAllBytes(mtp));
                    foreach (MtpInstance instance in read.Instances)
                    {
                        bool named = string.Equals(instance.Name, model, StringComparison.OrdinalIgnoreCase)
                            || (!instance.Model.IsNone && !instance.Model.IsCommon && instance.Model.Index < read.ModelNames.Count
                                && string.Equals(read.ModelNames[instance.Model.Index], model, StringComparison.OrdinalIgnoreCase));
                        if (!named)
                        {
                            continue;
                        }

                        slots = new string[instance.SlotCount];
                        for (int slot = 0; slot < instance.SlotCount; slot++)
                        {
                            slots[slot] = read.TryGetTextureName(instance, slot, out string? texture) ? texture : string.Empty;
                        }
                    }
                }

                Copy($"{model}.mef");
                json.WriteStartArray(model);
                foreach (string texture in slots ?? [])
                {
                    json.WriteStringValue(texture);
                    if (texture.Length > 0 && !File.Exists(Path.Combine(output, $"{texture}.tex")))
                    {
                        Copy($"{texture}.tex");
                    }
                }

                json.WriteEndArray();
                if (slots is null)
                {
                    ctx.Error.WriteLine($"no pack instance names {model}: it draws untextured");
                }
            }

            json.WriteEndObject();
            WriteList(json, "anims", clips, c => Copy($"{c}.iff"));
            WriteList(json, "sounds", sounds, s => Copy($"{s}.wav"));
            WriteList(json, "sprites", sprites, s => Copy($"{s}.spr"));
            json.WriteEndObject();
        }

        File.WriteAllBytes(Path.Combine(output, "kit.json"), manifest.ToArray());
        return missing == 0 ? ToolExitCode.Ok : ctx.Fail($"{missing} asset(s) not found.");
    }

    /// <summary>The usage line.</summary>
    public const string Usage = "ResExtract kit <igi2-pc-dir> <outdir> [--models a,b] [--anims a,b] [--sounds a,b] [--sprites a,b]";

    private static void WriteList(Utf8JsonWriter json, string name, List<string> items, Func<string, bool> copy)
    {
        json.WriteStartArray(name);
        foreach (string item in items)
        {
            if (copy(item))
            {
                json.WriteStringValue(item);
            }
        }

        json.WriteEndArray();
    }

    private static List<string> Option(IReadOnlyList<string> args, string name)
    {
        for (int i = 3; i < args.Count - 1; i++)
        {
            if (args[i] == name)
            {
                return [.. args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }
        }

        return [];
    }
}

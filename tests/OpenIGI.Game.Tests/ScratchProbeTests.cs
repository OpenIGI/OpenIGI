using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

public sealed class ScratchProbeTests
{
    private readonly ITestOutputHelper _output;

    public ScratchProbeTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void DumpSniperTextureResolution()
    {
        CompareCrossLevelTextures(7, 8);
        foreach (int level in new[] { 7, 8 })
        {
            Dumplevel(level);
        }
    }

    [RequiresGameDataFact]
    public void DumpSniperRegistryOrigins()
    {
        foreach (int level in new[] { 7, 8 })
        {
            (LoadedMission mission, LevelTask root, ResourceRegistry resources) = Load(level);
            ReportAllRegistryCollisions(resources, level);
            DumpSniperTextures(mission, root, resources, level);
        }
    }

    [RequiresGameDataFact]
    public void DumpSniperBodyPng()
    {
        const int level = 8;
        (LoadedMission mission, _, ResourceRegistry resources) = Load(level);
        string dir = @"D:\Code\open-igi\scratch\sniper-dump";
        Directory.CreateDirectory(dir);
        List<string> written = [];
        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, "001_02_1");
        if (pack is null || instance is not { } resolved)
        {
            _output.WriteLine("no instance 001_02_1");
            return;
        }

        int[] slots = { 0, 4, 11, 12, 3, 7 };
        foreach (int slot in slots)
        {
            if (!pack.TryGetTextureName(resolved, slot, out string texture))
            {
                _output.WriteLine($"slot {slot}: no texture");
                continue;
            }

            if (!resources.TryLoad(LoadedPack.TextureResourceName(texture), out ReadOnlyMemory<byte> bytes))
            {
                _output.WriteLine($"slot {slot} '{texture}': not in registry");
                continue;
            }

            OpenIGI.Formats.Textures.TextureImage image =
                OpenIGI.Formats.Textures.TexFile.Load(bytes.Span);
            string png = System.IO.Path.Combine(dir, $"L8_001_02_1_slot{slot}_{texture}.png");
            OpenIGI.Core.Graphics.PngWriter.Write(png, image.GetLevel(0), image.Width, image.Height);
            written.Add(png);

            int n = System.Math.Min(image.Pixels.Length, 64);
            int[] sums = new int[3];
            foreach (OpenIGI.Core.Graphics.Bgra32 pixel in image.Pixels.Slice(0, n))
            {
                sums[0] += pixel.R;
                sums[1] += pixel.G;
                sums[2] += pixel.B;
            }

            _output.WriteLine($"slot {slot} '{texture}': wrote {png}; " +
                $"avg RGB(0..{n}) = ({sums[0] / n}, {sums[1] / n}, {sums[2] / n})");
        }

        _output.WriteLine($"wrote {written.Count} PNG(s) to {dir}");
    }

    private void ReportAllRegistryCollisions(ResourceRegistry resources, int level)
    {
        Dictionary<string, List<(string Parent, int Size)>> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (ResourceEntry entry in resources.Entries)
        {
            if (!entry.Name.StartsWith("LOCAL:textures/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string bare = BareName(entry.Name);
            if (!byName.TryGetValue(bare, out List<(string, int)>? list))
            {
                byName[bare] = list = [];
            }

            list.Add((entry.Parent ?? "<standalone>", entry.Data.Length));
        }

        int shared = 0;
        foreach ((string name, List<(string Parent, int Size)> origins) in byName)
        {
            string[] distinct = origins.Select(o => o.Parent).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (distinct.Length <= 1)
            {
                continue;
            }

            shared++;
            int[] sizes = origins.Select(o => o.Size).Distinct().ToArray();
            _output.WriteLine($"  MULTI-ORIGIN level {level} '{name}': " +
                $"{string.Join(" | ", origins.Select(o => $"[{o.Size}@{o.Parent}]"))}" +
                (sizes.Length > 1 ? "  <-- DIFFERENT SIZES" : ""));
        }

        _output.WriteLine($"LEVEL {level}: {byName.Count} texture names in registry, {shared} registered from 2+ archives");
    }

    private void DumpSniperTextures(LoadedMission mission, LevelTask root, ResourceRegistry resources, int level)
    {
        foreach (HumanSoldierTask soldier in FindAll<HumanSoldierTask>(root))
        {
            string modelName = soldier.Model;
            if (!string.Equals(modelName, "001_02_1", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
            if (pack is null || instance is not { } resolved)
            {
                _output.WriteLine($"LEVEL {level} soldier '{soldier.Name}': model '{modelName}' has NO pack instance");
                continue;
            }

            _output.WriteLine($"LEVEL {level} soldier '{soldier.Name}': model '{modelName}' -> pack '{pack.Name}' " +
                $"({instance.Value.MaterialIndices.Length} slots)");
            for (int slot = 0; slot < resolved.MaterialIndices.Length; slot++)
            {
                bool has = pack.TryGetTextureName(resolved, slot, out string texture);
                if (!has)
                {
                    _output.WriteLine($"   slot {slot}: (no texture)");
                    continue;
                }

                _output.WriteLine($"   slot {slot}: '{texture}' {DescribeRegistry(resources, texture)}");
                try
                {
                    if (resources.TryLoad(LoadedPack.TextureResourceName(texture), out ReadOnlyMemory<byte> bytes))
                    {
                        OpenIGI.Formats.Textures.TextureDescriptor header =
                            OpenIGI.Formats.Textures.TexFile.ReadHeader(bytes.Span, out _);
                        _output.WriteLine($" decode={header.Format} {header.Width}x{header.Height} mips={header.MipLevels}");
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($" DECODE-ERROR {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    private void CompareCrossLevelTextures(int a, int b)
    {
        Dictionary<string, (int Size, byte[] Bytes)> ia = LevelTextureNames(a);
        Dictionary<string, (int Size, byte[] Bytes)> ib = LevelTextureNames(b);
        var shared = ia.Keys.Intersect(ib.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var differing = shared.Where(k => !ia[k].Bytes.AsSpan().SequenceEqual(ib[k].Bytes)).ToList();
        _output.WriteLine($"CROSS-LEVEL {a} vs {b}: level{a}={ia.Count}, level{b}={ib.Count}, " +
            $"shared names={shared.Count}, shared-with-DIFFERENT-bytes={differing.Count}");
        foreach (string name in differing.Take(30))
        {
            _output.WriteLine($"   XLEVEL {name}: level{a}[{ia[name].Size} bytes] vs level{b}[{ib[name].Size} bytes]");
        }
    }

    private static Dictionary<string, (int Size, byte[] Bytes)> LevelTextureNames(int level)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        string archive = $"LOCAL:missions/location0/level{level}/textures/level{level}.res";
        var result = new Dictionary<string, (int, byte[])>(
            StringComparer.OrdinalIgnoreCase);
        if (resources.TryLoad(archive, out ReadOnlyMemory<byte> bytes))
        {
            foreach (IresEntry entry in OpenIGI.Formats.Ilff.IresArchive.Read(bytes).Entries)
            {
                string name = BareName(entry.Name);
                result[name] = (entry.Data.Length, entry.Data.ToArray());
            }
        }

        return result;
    }

    private void Dumplevel(int level)
    {
        (LoadedMission mission, LevelTask root, ResourceRegistry resources) = Load(level);
        DatIndex dat = ReadDat(resources, mission.LevelPack);
        ReportArchiveCollisions(mission, resources, level);
        ScanAllInstances(mission, resources, dat, level);
        int soldierCount = 0;
        foreach (HumanSoldierTask soldier in FindAll<HumanSoldierTask>(root))
        {
            soldierCount++;
            HumanAiTask? mind = FindMind(soldier);
            string aiType = mind?.AiTypeName ?? "?";
            string modelName = soldier.Model;
            bool hasModel = mission.TryGetModel(modelName, out MefModel? model) && model is not null;

            _output.WriteLine(
                $"level {level} soldier '{soldier.Name}' ai={aiType} model='{modelName}' resolved={hasModel}");

            if (!hasModel)
            {
                continue;
            }

            RequestTextureBindings(mission, resources, dat, soldier, modelName);
        }

        _output.WriteLine($"LEVEL {level}: {soldierCount} soldiers");
    }

    private void ScanAllInstances(LoadedMission mission, ResourceRegistry resources, DatIndex dat, int level)
    {
        int checkedCount = 0;
        int mismatchCount = 0;
        foreach (MtpInstance instance in mission.LevelPack.Instances)
        {
            if (!mission.LevelPack.TryGetModel(instance, out MefModel? model) || model is null)
            {
                continue;
            }

            MeshGeometry? geometry = MefMeshBuilder.Build(model, 1f);
            if (geometry is null)
            {
                continue;
            }

            DatEntry? datEntry = dat.Entries.FirstOrDefault(e =>
                string.Equals(e.Model, instance.Name, StringComparison.OrdinalIgnoreCase));
            if (datEntry is null)
            {
                continue;
            }

            List<string> resolved = [];
            foreach (MeshSubset sub in geometry.Subsets)
            {
                if (sub.HasMaterial
                    && mission.LevelPack.TryGetTextureName(instance, sub.MaterialSlot, out string texture))
                {
                    resolved.Add(texture);
                }
            }

            checkedCount++;
            if (!datEntry.Value.Textures.SequenceEqual(resolved, StringComparer.OrdinalIgnoreCase))
            {
                mismatchCount++;
                _output.WriteLine($"  MISMATCH level {level} instance '{instance.Name}': " +
                    $"resolved {{{string.Join(", ", resolved)}}} vs .dat {{{string.Join(", ", datEntry.Value.Textures)}}}");
            }
        }

        _output.WriteLine($"LEVEL {level}: scanned {checkedCount} instances, {mismatchCount} mismatches vs .dat");
    }

    private void ReportArchiveCollisions(LoadedMission mission, ResourceRegistry resources, int level)
    {
        Dictionary<string, ResourceEntry> common = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ResourceEntry> levelP = new(StringComparer.OrdinalIgnoreCase);
        foreach (ResourceEntry entry in resources.Entries)
        {
            if (entry.Parent?.EndsWith("textures/textures.res", StringComparison.OrdinalIgnoreCase) == true
                || entry.Parent?.EndsWith("textures/location0.res", StringComparison.OrdinalIgnoreCase) == true)
            {
                common[BareName(entry.Name)] = entry;
            }
            else if (entry.Parent?.EndsWith($"textures/level{level}.res", StringComparison.OrdinalIgnoreCase) == true)
            {
                levelP[BareName(entry.Name)] = entry;
            }
        }

        var shared = common.Keys.Intersect(levelP.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var differing = shared
            .Where(k => !common[k].Data.Span.SequenceEqual(levelP[k].Data.Span))
            .ToList();

        _output.WriteLine($"LEVEL {level}: common={common.Count} textures, level={levelP.Count} textures, " +
            $"shared names={shared.Count}, shared-with-DIFFERENT-bytes={differing.Count}");

        foreach (string name in differing.Take(30))
        {
            _output.WriteLine($"   COLLIDE {name}: common[{common[name].Data.Length}@{common[name].Parent}] " +
                $"vs level[{levelP[name].Data.Length}@{levelP[name].Parent}]");
        }
    }

    private static string BareName(string resourceName)
    {
        ReadOnlySpan<char> span = resourceName.Replace('\\', '/');
        int slash = span.LastIndexOf('/');
        if (slash >= 0)
        {
            span = span[(slash + 1)..];
        }

        return span.ToString();
    }

    private void RequestTextureBindings(
        LoadedMission mission,
        ResourceRegistry resources,
        DatIndex dat,
        HumanSoldierTask soldier,
        string modelName)
    {
        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
        if (pack is null || instance is not { } resolvedInstance)
        {
            _output.WriteLine($"  -> no pack instance named '{modelName}'");
            return;
        }

        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            return;
        }

        MeshGeometry? geometry = MefMeshBuilder.Build(model, 1f);
        if (geometry is null)
        {
            return;
        }

        DatEntry? datEntry = dat.Entries.FirstOrDefault(e =>
            string.Equals(e.Model, modelName, StringComparison.OrdinalIgnoreCase));

        List<string> resolvedNames = [];
        _output.WriteLine($"  -> pack '{pack.Name}' instance slots={resolvedInstance.MaterialIndices.Length}");
        int subset = 0;
        foreach (MeshSubset sub in geometry.Subsets)
        {
            subset++;
            bool hasTexture = false;
            string? texture = null;
            if (sub.HasMaterial
                && pack.TryGetTextureName(resolvedInstance, sub.MaterialSlot, out string name))
            {
                hasTexture = true;
                texture = name;
            }

            string resolved = hasTexture ? texture! : "(untextured / none)";
            if (texture is not null)
            {
                resolvedNames.Add(texture);
            }

            string served = "(not registered)";
            if (texture is not null)
            {
                served = DescribeRegistry(resources, texture);
            }

            _output.WriteLine($"     subset {subset} slot={sub.MaterialSlot} -> '{resolved}' registry: {served}");
        }

        if (datEntry is { } entry)
        {
            bool matches = entry.Textures.SequenceEqual(resolvedNames, StringComparer.OrdinalIgnoreCase);
            _output.WriteLine($"     .dat {modelName}: {entry.Textures.Count} texture(s) " +
                $"{(matches ? "== MATCH" : $"!= MISMATCH; expected {{{string.Join(", ", entry.Textures)}}}")}");
        }
        else
        {
            _output.WriteLine($"     .dat has NO entry for '{modelName}'");
        }
    }

    private static DatIndex ReadDat(ResourceRegistry resources, LoadedPack pack)
    {
        foreach (string candidate in new[] { pack.Directory + pack.Name + ".dat", pack.Directory + "level.dat" })
        {
            if (resources.TryLoad(candidate, out ReadOnlyMemory<byte> bytes))
            {
                return DatIndex.Parse(System.Text.Encoding.Latin1.GetString(bytes.Span));
            }
        }

        return DatIndex.Parse("0");
    }

    private static string DescribeRegistry(ResourceRegistry resources, string textureName)
    {
        string resourceName = LoadedPack.TextureResourceName(textureName);
        if (resources.TryGetEntry(resourceName, out ResourceEntry? entry))
        {
            return $"'{resourceName}' [{entry.Data.Length} bytes] from {entry.Parent ?? "<standalone>"}";
        }

        return $"'{resourceName}' NOT IN REGISTRY";
    }

    private static (LoadedPack? Pack, MtpInstance? Instance) FindInstance(LoadedMission mission, string name)
    {
        foreach (LoadedPack pack in new[] { mission.LevelPack, mission.CommonPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return (pack, instance);
                }
            }
        }

        return (null, null);
    }

    private static HumanAiTask? FindMind(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is HumanAiTask mind)
            {
                return mind;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindAll<T>(GameTask task)
        where T : GameTask
    {
        foreach (GameTask child in task.Children)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T deeper in FindAll<T>(child))
            {
                yield return deeper;
            }
        }
    }

    private static (LoadedMission, LevelTask, ResourceRegistry) Load(int mission)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader loader = new(root, loop.TaskCommands, loop.Natives, resources);
        LoadedMission loaded = new MissionLoader(registry, fileSystem, resources, loader, null, null).Load(mission);
        return (loaded, root, resources);
    }
}

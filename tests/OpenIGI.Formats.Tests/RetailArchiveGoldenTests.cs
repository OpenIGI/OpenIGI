using System.Globalization;
using OpenIGI.Formats.Ilff;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Reads every archive of a real installation, which is the only way to know the reader survives the
/// shapes the shipped data actually uses.
/// </summary>
/// <remarks>Skipped when <c>IGI_GAME_PATH</c> is unset.</remarks>
public sealed class RetailArchiveGoldenTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="RetailArchiveGoldenTests"/> class.</summary>
    /// <param name="output">The xunit output sink.</param>
    public RetailArchiveGoldenTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void EveryResArchiveEnumeratesWithoutError()
    {
        IReadOnlyList<string> archives = GameData.EnumerateFiles("*.res");
        Assert.NotEmpty(archives);

        List<string> failures = [];
        int entryCount = 0;

        foreach (string path in archives)
        {
            try
            {
                IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));
                Assert.NotEmpty(archive.Entries);

                foreach (IresEntry entry in archive.Entries)
                {
                    Assert.False(string.IsNullOrEmpty(entry.Name));
                    Assert.DoesNotContain('\\', entry.Name);
                    entryCount++;
                }
            }
            catch (Exception error) when (error is IlffFormatException or IOException)
            {
                failures.Add($"{path}: {error.Message}");
            }
        }

        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{archives.Count} archives, {entryCount} entries."));

        Assert.Empty(failures);
    }

    [RequiresGameDataFact]
    public void EveryContainerFileParsesWhateverItsFormType()
    {
        // .mef models and .fnt fonts are ILFF containers too, with form types OCEM and FONT.
        List<string> paths =
        [
            .. GameData.EnumerateFiles("*.mef"),
            .. GameData.EnumerateFiles("*.fnt"),
        ];

        List<string> failures = [];
        foreach (string path in paths)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!IlffContainer.IsIlff(bytes))
            {
                failures.Add($"{path}: does not start with an ILFF header.");
                continue;
            }

            try
            {
                IlffContainer container = IlffContainer.Read(bytes);
                Assert.NotEmpty(container.Chunks);
            }
            catch (IlffFormatException error)
            {
                failures.Add($"{path}: {error.Message}");
            }
        }

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{paths.Count} model/font containers."));
        Assert.Empty(failures);
    }

    [RequiresGameDataFact]
    public void LanguageArchivesExposeTheirStringsAsContextStrings()
    {
        string path = GameData.Resolve(Path.Combine("language", "english", "objectives.res"));
        Assert.True(File.Exists(path), $"Expected '{path}' in the installation.");

        IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));

        Assert.True(archive.TryGetEntry("OBJECTIVE", out IresEntry? objective));
        Assert.Equal(IresEntryKind.ContextString, objective.Kind);
        Assert.Equal("Objective", objective.AsText());

        Assert.True(archive.TryGetEntry("COMPLETE", out IresEntry? complete));
        Assert.Equal("Complete", complete.AsText());
    }

    [RequiresGameDataFact]
    public void TextureArchivesExposeVirtualFileSystemPaths()
    {
        string path = GameData.Resolve(Path.Combine("common", "textures", "textures.res"));
        Assert.True(File.Exists(path), $"Expected '{path}' in the installation.");

        IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));

        Assert.All(archive.Entries, entry => Assert.Equal(IresEntryKind.Body, entry.Kind));
        Assert.Contains(archive.Entries, entry => entry.Name.StartsWith("LOCAL:", StringComparison.Ordinal));

        // shadow1.tex is 32x32 with a full mip chain: 32 header bytes + 4 bytes per texel.
        Assert.True(archive.TryGetEntry("LOCAL:common/textures/shadow1.tex", out IresEntry? shadow));
        Assert.Equal(32 + (4 * (1024 + 256 + 64 + 16 + 4 + 1)), shadow.Data.Length);
    }
}

using System.Buffers.Binary;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The claims <see cref="AssetFormatProfile.Igi2"/> makes, checked against a retail I.G.I. 2
/// installation rather than asserted.
/// </summary>
/// <remarks>
/// Skips when <c>IGI2_GAME_PATH</c> is unset, so a checkout with only I.G.I. 1 — or neither game —
/// still runs green. These are the measurements the profile was built from; keeping them as tests is
/// what stops the profile drifting away from what the games actually ship.
/// </remarks>
public sealed class RetailIgi2FormatTests
{
    private static readonly GameDataSource Igi2 = GameDataSource.Igi2;

    /// <summary>Every I.G.I. 2 script is bytecode version 7 on the same instruction set as I.G.I. 1.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_qvm_is_version_seven_on_instruction_set_eight()
    {
        IReadOnlyList<string> scripts = Igi2.EnumerateFiles("*.qvm");
        Assert.NotEmpty(scripts);

        foreach (string path in scripts)
        {
            byte[] head = ReadPrefix(path, 12);
            Assert.Equal("LOOP"u8.ToArray(), head[..4]);

            int instructionSet = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
            int version = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(8));

            Assert.Equal(AssetFormatProfile.Igi2.QvmInstructionSet, instructionSet);
            Assert.True(
                AssetFormatProfile.Igi2.AcceptsQvm(version),
                $"'{path}' is QVM version {version}, which the I.G.I. 2 profile does not ship.");
        }
    }

    /// <summary>
    /// The version 7 header is four bytes longer than version 5's, and the code section ends at the
    /// file-info block rather than at the end of the file.
    /// </summary>
    /// <remarks>
    /// This is the identity the loader asserts, and the one a version 5 reader gets wrong on a version 7
    /// file: it checks that the code runs to the end of the file, which is only true when there is no
    /// file-info block after it.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_code_section_ends_at_the_file_info_block()
    {
        int checkedFiles = 0;

        foreach (string path in Igi2.EnumerateFiles("*.qvm"))
        {
            byte[] head = ReadPrefix(path, 64);
            if (head.Length < 64)
            {
                continue;
            }

            int codeOffset = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(0x2C));
            int codeSize = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(0x30));
            int fileInfoOffset = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(0x3C));
            long length = new FileInfo(path).Length;

            long codeEnd = fileInfoOffset != 0 ? fileInfoOffset : length;
            Assert.True(
                codeOffset + codeSize == codeEnd,
                $"'{path}': code ends at {codeOffset + codeSize}, expected {codeEnd}.");

            checkedFiles++;
        }

        Assert.True(checkedFiles > 0, "No I.G.I. 2 scripts were checked.");
    }

    /// <summary>I.G.I. 2's model archives are ILFF, and open with the reader written for I.G.I. 1.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Model_archives_open_with_the_shared_ilff_reader()
    {
        string archive = FirstModelArchive();
        IlffContainer container = IlffContainer.Read(File.ReadAllBytes(archive));

        Assert.Equal(IlffTags.Ires, container.FormType);
        Assert.NotEmpty(container.Chunks);
    }

    /// <summary>
    /// I.G.I. 2's models are mesh version 0.16, which the I.G.I. 1 profile refuses and the I.G.I. 2
    /// profile accepts. This is the one format gate where the two games genuinely disagree.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Models_are_mesh_version_that_only_the_igi2_profile_accepts()
    {
        IresArchive models = IresArchive.Read(File.ReadAllBytes(FirstModelArchive()));
        IresEntry model = models.Entries.First(static e => e.Name.EndsWith(".mef", StringComparison.OrdinalIgnoreCase));

        // Refused under I.G.I. 1's profile -- which is also what the parameterless overload does.
        Assert.Throws<PackFormatException>(() => MefModel.Read(model.Data, AssetFormatProfile.Igi1));

        // And accepted under I.G.I. 2's, as far as the version gate is concerned.
        PackFormatException? failure = Record.Exception(
            () => MefModel.Read(model.Data, AssetFormatProfile.Igi2)) as PackFormatException;

        Assert.True(
            failure is null || !failure.Message.Contains("Mesh version", StringComparison.Ordinal),
            $"The I.G.I. 2 profile still refused the version: {failure?.Message}");
    }

    /// <summary>Finds a model archive in the installation, whichever levels it happens to ship.</summary>
    /// <remarks>
    /// Searched rather than named, because level directories differ between releases -- the retail 1.1
    /// tree mixes <c>level1</c> with <c>LEVEL5</c> -- and a test that hardcodes one fails as though the
    /// format had changed when all that changed was the install.
    /// </remarks>
    private static string FirstModelArchive()
    {
        string root = Path.Combine(Igi2.RequireRoot(), "missions");
        string? archive = Directory
            .EnumerateFiles(root, "*.res", SearchOption.AllDirectories)
            .Where(static p => Path.GetFileName(Path.GetDirectoryName(p))!
                .Equals("models", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        Assert.True(archive is not null, $"No models/*.res found below '{root}'.");
        return archive!;
    }

    private static byte[] ReadPrefix(string path, int count)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[count];
        int read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
        return read == count ? buffer : buffer[..read];
    }
}

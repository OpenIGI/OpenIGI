using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// The <c>.cube</c> reader, and the shipped grades it has to read.
/// </summary>
/// <remarks>
/// A grade is a look, so most of what it does cannot be asserted. What can be asserted is that the
/// file says what it means — the right number of entries, in the right order, with the identity table
/// actually being the identity — because every one of those failures produces a picture that looks
/// like a rendering fault rather than like a bad file.
/// </remarks>
public sealed class ColourLookupTableTests
{
    private static string LutDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "luts");

    [Fact]
    public void TheIdentityTableMapsEveryColourToItself()
    {
        ColourLookupTable table = ColourLookupTable.Identity(16);

        Assert.Equal(16, table.Size);
        Assert.Equal(16 * 16 * 16 * 3, table.Entries.Length);

        // Red fastest, then green, then blue: entry (r,g,b) sits at ((b*N + g)*N + r)*3. Getting this
        // order wrong transposes the grade's channels, and checking a corner would not notice.
        const int n = 16;
        float step = 1f / (n - 1);

        for (int b = 0; b < n; b += 5)
        {
            for (int g = 0; g < n; g += 5)
            {
                for (int r = 0; r < n; r += 5)
                {
                    int at = (((b * n) + g) * n + r) * 3;
                    Assert.Equal(r * step, table.Entries[at], 5);
                    Assert.Equal(g * step, table.Entries[at + 1], 5);
                    Assert.Equal(b * step, table.Entries[at + 2], 5);
                }
            }
        }
    }

    [Fact]
    public void AWellFormedCubeIsRead()
    {
        string[] lines =
        [
            "# a comment tools routinely write",
            "TITLE \"two\"",
            "LUT_3D_SIZE 2",
            "0 0 0",
            "1 0 0",
            "0 1 0",
            "1 1 0",
            "0 0 1",
            "1 0 1",
            "0 1 1",
            "1 1 1",
        ];

        ColourLookupTable? table = ColourLookupTable.Parse("two", lines);

        Assert.NotNull(table);
        Assert.Equal(2, table.Size);
        Assert.Equal("two", table.Name);
        Assert.Equal(24, table.Entries.Length);
    }

    [Fact]
    public void ATableShorterThanItsDeclaredSizeIsRefused()
    {
        // The failure this is really about: a truncated table read as though it were whole produces a
        // grade that goes black in the highlights, which reads as the renderer being broken.
        string[] lines = ["LUT_3D_SIZE 2", "0 0 0", "1 0 0", "0 1 0"];

        Assert.Null(ColourLookupTable.Parse("short", lines));
    }

    [Fact]
    public void ATableLongerThanItsDeclaredSizeIsRefused()
    {
        string[] lines = ["LUT_3D_SIZE 2", .. Enumerable.Repeat("0.5 0.5 0.5", 9)];

        Assert.Null(ColourLookupTable.Parse("long", lines));
    }

    [Theory]
    [InlineData("LUT_1D_SIZE 16")]
    [InlineData("LUT_3D_SIZE 1")]
    [InlineData("LUT_3D_SIZE 128")]
    [InlineData("LUT_3D_SIZE banana")]
    public void ATableThatIsNotAUsableCubeIsRefused(string header) =>
        Assert.Null(ColourLookupTable.Parse("bad", [header, "0 0 0"]));

    [Fact]
    public void EntriesBeforeTheSizeAreRefused() =>
        Assert.Null(ColourLookupTable.Parse("early", ["0 0 0", "LUT_3D_SIZE 2"]));

    [Fact]
    public void AMissingFileIsNotAnError() =>
        Assert.Null(ColourLookupTable.Load(Path.Combine(LutDirectory, "no-such-grade.cube")));

    [Fact]
    public void EveryShippedGradeLoads()
    {
        Assert.True(Directory.Exists(LutDirectory), $"no colour tables were copied to {LutDirectory}");

        string[] files = Directory.GetFiles(LutDirectory, "*.cube");
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            ColourLookupTable? table = ColourLookupTable.Load(file);
            Assert.True(table is not null, $"{Path.GetFileName(file)} did not load");
            Assert.Equal(table!.Size * table.Size * table.Size * 3, table.Entries.Length);
        }
    }

    [Fact]
    public void TheShippedIdentityGradeReallyIsTheIdentity()
    {
        // The one shipped table whose contents can be checked outright, and the reason it is shipped:
        // it is what a wrong sampler coordinate shows up against. Anything else only looks a bit off.
        ColourLookupTable? shipped = ColourLookupTable.Load(Path.Combine(LutDirectory, "identity.cube"));
        Assert.NotNull(shipped);

        ColourLookupTable expected = ColourLookupTable.Identity(shipped.Size);

        for (int i = 0; i < expected.Entries.Length; i++)
        {
            Assert.Equal(expected.Entries[i], shipped.Entries[i], 5);
        }
    }

    [Fact]
    public void TheMissionGradesLeaveBlackNearBlackAndWhiteNearWhite()
    {
        // A grade may do anything in between, but one that moves the endpoints far is one that will
        // clip the picture: black going up is a washed-out frame and white coming down is a dull one.
        // Generous bounds -- these are lifts and gains of a few percent by design.
        foreach (string file in Directory.GetFiles(LutDirectory, "mission*.cube"))
        {
            ColourLookupTable? table = ColourLookupTable.Load(file);
            Assert.NotNull(table);

            string name = Path.GetFileNameWithoutExtension(file);
            int last = (table.Size * table.Size * table.Size * 3) - 3;

            for (int channel = 0; channel < 3; channel++)
            {
                Assert.True(
                    table.Entries[channel] < 0.12f,
                    $"{name} lifts black to {table.Entries[channel]:0.000} on channel {channel}");
                Assert.True(
                    table.Entries[last + channel] > 0.88f,
                    $"{name} pulls white down to {table.Entries[last + channel]:0.000} on channel {channel}");
            }
        }
    }
}

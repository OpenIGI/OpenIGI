using OpenIGI.Formats;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Which encodings each title ships. The values are measured against retail installations of both
/// games, so these tests are as much a record of the measurement as a check on the type.
/// </summary>
public sealed class AssetFormatProfileTests
{
    /// <summary>
    /// The mesh version is the one format where the two games genuinely disagree: I.G.I. 1 ships 0.10
    /// and I.G.I. 2 ships 0.16, so each refuses the other's models.
    /// </summary>
    [Fact]
    public void The_two_titles_ship_different_mesh_versions()
    {
        Assert.True(AssetFormatProfile.Igi1.AcceptsMesh(0.1f));
        Assert.False(AssetFormatProfile.Igi1.AcceptsMesh(0.16f));

        Assert.True(AssetFormatProfile.Igi2.AcceptsMesh(0.16f));
        Assert.False(AssetFormatProfile.Igi2.AcceptsMesh(0.1f));

        Assert.True(AssetFormatProfile.Permissive.AcceptsMesh(0.1f));
        Assert.True(AssetFormatProfile.Permissive.AcceptsMesh(0.16f));
    }

    /// <summary>
    /// Textures and sprites, by contrast, already overlap: every I.G.I. 2 texture is version 11 and
    /// every sprite version 9, both of which the readers have always parsed. Recorded because it is the
    /// finding that made this a correctness seam rather than the enabler it was expected to be.
    /// </summary>
    [Fact]
    public void The_titles_overlap_on_textures_and_sprites()
    {
        Assert.True(AssetFormatProfile.Igi1.AcceptsTexture(11));
        Assert.True(AssetFormatProfile.Igi2.AcceptsTexture(11));

        Assert.True(AssetFormatProfile.Igi1.AcceptsSprite(9));
        Assert.True(AssetFormatProfile.Igi2.AcceptsSprite(9));

        // I.G.I. 2 ships neither of these, so a file carrying one came from somewhere else.
        Assert.False(AssetFormatProfile.Igi2.AcceptsTexture(2));
        Assert.False(AssetFormatProfile.Igi2.AcceptsSprite(1));
    }

    /// <summary>The QVM version differs; the instruction set does not.</summary>
    [Fact]
    public void The_titles_differ_on_bytecode_version_but_not_instruction_set()
    {
        Assert.True(AssetFormatProfile.Igi1.AcceptsQvm(5));
        Assert.False(AssetFormatProfile.Igi1.AcceptsQvm(7));

        Assert.True(AssetFormatProfile.Igi2.AcceptsQvm(7));
        Assert.False(AssetFormatProfile.Igi2.AcceptsQvm(5));

        Assert.Equal(8, AssetFormatProfile.Igi1.QvmInstructionSet);
        Assert.Equal(8, AssetFormatProfile.Igi2.QvmInstructionSet);
    }

    /// <summary>
    /// Mesh versions compare on the bit pattern, not within a tolerance. 0.1f is not exactly
    /// representable, and a tolerance would invent a range the format does not have.
    /// </summary>
    [Fact]
    public void Mesh_versions_compare_exactly()
    {
        Assert.False(AssetFormatProfile.Igi1.AcceptsMesh(0.1f + 1e-6f));
        Assert.False(AssetFormatProfile.Igi1.AcceptsMesh(0f));
    }

    /// <summary>I.G.I. 1's sets are unchanged from what its readers always accepted.</summary>
    /// <remarks>
    /// The guard against introducing this type narrowing what already loads: every version the readers
    /// name as supported is one the I.G.I. 1 profile still ships.
    /// </remarks>
    [Fact]
    public void The_igi1_profile_did_not_narrow_the_readers()
    {
        Assert.True(AssetFormatProfile.Igi1.AcceptsTexture(Textures.TexFile.Version2));
        Assert.True(AssetFormatProfile.Igi1.AcceptsTexture(Textures.TexFile.Version11));
        Assert.True(AssetFormatProfile.Igi1.AcceptsMesh(World.MefModel.SupportedVersion));
    }
}

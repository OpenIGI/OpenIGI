using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Pins where per-user files are looked for: beside the executable when one is already there, and in
/// the per-user directory otherwise.
/// </summary>
/// <remarks>
/// The rule matters beyond tidiness. Anything the player accumulates that lands in the game folder is
/// lost the moment that folder is read-only or shared, so "not in the installation" is the property
/// being held here.
/// </remarks>
public sealed class SettingsPathsTests
{
    [Fact]
    public void AFileWithNoPortableCopyResolvesUnderTheUserDirectory()
    {
        string name = "openigi-test-" + Guid.NewGuid().ToString("N") + ".qvm";

        string resolved = SettingsPaths.GetFilePath(name);

        Assert.Equal(Path.Combine(SettingsPaths.GetUserDirectory(), name), resolved);
        Assert.Equal(name, Path.GetFileName(resolved));
        Assert.True(Path.IsPathFullyQualified(resolved));
    }

    [Fact]
    public void APortableCopyBesideTheExecutableWins()
    {
        string name = "openigi-test-" + Guid.NewGuid().ToString("N") + ".qvm";
        string portable = Path.Combine(SettingsPaths.PortableDirectory, name);

        // Absent, it resolves to the user directory; present, to the copy beside the executable.
        Assert.Equal(Path.Combine(SettingsPaths.GetUserDirectory(), name), SettingsPaths.GetFilePath(name));

        try
        {
            File.WriteAllBytes(portable, [0]);
            Assert.Equal(portable, SettingsPaths.GetFilePath(name));
        }
        finally
        {
            File.Delete(portable);
        }
    }

    [Fact]
    public void TheSettingsFileFollowsTheSameRule() =>
        Assert.Equal(SettingsPaths.GetFilePath(SettingsPaths.SettingsFileName), SettingsPaths.GetSettingsPath());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNameIsRejected(string name) =>
        Assert.Throws<ArgumentException>(() => SettingsPaths.GetFilePath(name));

    [Fact]
    public void ANullNameIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => SettingsPaths.GetFilePath(null!));
}

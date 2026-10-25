using Xunit;

namespace OpenIGI.Launcher.Tests;

public sealed class SizeFormatTests
{
    [Theory]
    [InlineData(0.14, "140 KB")]
    [InlineData(0.0004, "0 KB")]
    [InlineData(0.9994, "999 KB")]
    [InlineData(0.9996, "1 MB")]
    [InlineData(48.6, "49 MB")]
    [InlineData(1004.9, "1.0 GB")]
    public void A_size_under_a_megabyte_is_given_in_kilobytes(double megabytes, string shown) =>
        Assert.Equal(shown, LauncherModel.FormatSize(megabytes));
}

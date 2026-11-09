using Xunit;

namespace OpenIGI.Launcher.Tests;

/// <summary>Issues #26 and #61: a GLX lookup answers for EGL names with stubs that crashed Skia.</summary>
public sealed class SkiaGlProcsTests
{
    // A lookup like glXGetProcAddress: an address for every name, whether the function exists or not.
    private static nint AnswersEverything(string name) => 0x1000;

    [Theory]
    [InlineData("eglQueryString")]
    [InlineData("eglGetCurrentDisplay")]
    public void Without_an_EGL_context_Skia_is_told_EGL_is_missing(string name) =>
        Assert.Equal(0, SkiaGlProcs.Resolve(name, eglCurrent: false, AnswersEverything));

    [Fact]
    public void An_EGL_context_hands_Skia_the_EGL_entry_points() =>
        Assert.Equal(0x1000, SkiaGlProcs.Resolve("eglQueryString", eglCurrent: true, AnswersEverything));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenGL_entry_points_are_looked_up_either_way(bool eglCurrent) =>
        Assert.Equal(0x1000, SkiaGlProcs.Resolve("glGetString", eglCurrent, AnswersEverything));
}

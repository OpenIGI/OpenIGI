namespace OpenIGI.Tools.ShaderBuild;

/// <summary>A fault the shader build can report and explain rather than crash on.</summary>
/// <remarks>
/// Everything the tool can go wrong on -- a missing compiler, a source that does not compile, a
/// reflection it cannot reduce -- is a build fault with a cause worth printing. Program.cs turns one
/// of these into a message and an exit code; anything else keeps its stack trace, because it is a
/// bug in the tool rather than in the shaders.
/// </remarks>
public sealed class ShaderBuildException : Exception
{
    /// <summary>Initialises a new instance of the <see cref="ShaderBuildException"/> class.</summary>
    public ShaderBuildException()
    {
    }

    /// <summary>Initialises a new instance of the <see cref="ShaderBuildException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public ShaderBuildException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance of the <see cref="ShaderBuildException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying fault.</param>
    public ShaderBuildException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

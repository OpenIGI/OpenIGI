namespace OpenIGI.Tools;

/// <summary>The exit codes every tool returns.</summary>
public static class ToolExitCode
{
    /// <summary>The tool did what it was asked.</summary>
    public const int Ok = 0;

    /// <summary>The tool was asked properly and failed.</summary>
    public const int Failure = 1;

    /// <summary>The tool was asked wrongly; the usage was printed.</summary>
    public const int Usage = 2;
}

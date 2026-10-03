using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Tools;

/// <summary>Runs a tool: sets up its logging, takes the switches every tool shares, and reports a crash.</summary>
/// <remarks>
/// <para>
/// Every tool accepts <c>-v</c>/<c>--verbose</c>, which adds debug lines and the stack traces of failures,
/// and <c>-q</c>/<c>--quiet</c>, which leaves only warnings and errors. <c>OPENIGI_LOG</c> works too.
/// </para>
/// <para>
/// This and <see cref="ConsoleSink"/> are where a tool touches the process's standard streams; the tool's
/// own code writes through the <see cref="ToolContext"/> it is given.
/// </para>
/// </remarks>
public static class ToolHost
{
    /// <summary>Runs a tool.</summary>
    /// <param name="name">The tool's name.</param>
    /// <param name="args">The command line.</param>
    /// <param name="main">The tool.</param>
    /// <returns>The exit code.</returns>
    public static int Run(string name, string[] args, Func<ToolContext, int> main)
    {
        ArgumentNullException.ThrowIfNull(main);
        return RunAsync(name, args, context => Task.FromResult(main(context))).GetAwaiter().GetResult();
    }

    /// <summary>Runs a tool whose work is asynchronous.</summary>
    /// <param name="name">The tool's name.</param>
    /// <param name="args">The command line.</param>
    /// <param name="main">The tool.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(string name, string[] args, Func<ToolContext, Task<int>> main)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(main);

        LogLevel level = LogLevel.Information;
        List<string> arguments = [];
        foreach (string argument in args)
        {
            switch (argument)
            {
                case "-v" or "--verbose":
                    level = LogLevel.Debug;
                    break;
                case "-q" or "--quiet":
                    level = LogLevel.Warning;
                    break;
                default:
                    arguments.Add(argument);
                    break;
            }
        }

        // The tool's own lines at the chosen level; the engine libraries it drives only when something is wrong,
        // unless --verbose asks for everything.
        LogLevelPolicy policy = LogLevelPolicy.FromEnvironment(level == LogLevel.Debug ? LogLevel.Debug : LogLevel.Warning);
        if (!policy.Overrides.ContainsKey(name))
        {
            policy.SetOverride(name, level);
        }

        using LogRouter log = new(policy, replayCapacity: 0);
        log.AddSink(new ConsoleSink(ConsoleSinkLayout.Plain));
        using IDisposable crashLogging = CrashLogging.Install(log);

#pragma warning disable RS0030 // The tool host is where a tool's result stream is the process's standard output.
        ToolContext context = new(name, arguments, log, Console.Out, Console.Error);
#pragma warning restore RS0030
        try
        {
            return await main(context).ConfigureAwait(false);
        }
        finally
        {
            context.Out.Flush();
        }
    }
}

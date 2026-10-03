namespace OpenIGI.Core.Diagnostics;

/// <summary>Logs the exceptions nothing caught, so a crash leaves its reason in the log.</summary>
/// <remarks>
/// An exception that escapes a thread ends the process; one that escapes a task that nobody awaited is lost
/// silently. Both are logged as <see cref="LogLevel.Fatal"/> and <see cref="LogLevel.Error"/> respectively,
/// and a <see cref="LogRouter"/> flushes its sinks on a fatal record, so the file holds the stack trace before
/// the runtime tears the process down.
/// </remarks>
public static class CrashLogging
{
    private const string Category = "Crash";

    /// <summary>Starts logging unhandled exceptions to a logger.</summary>
    /// <param name="logger">The logger.</param>
    /// <returns>A handle that stops it when disposed.</returns>
    public static IDisposable Install(IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return new Subscription(logger);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly IOpenIgiLogger _logger;

        public Subscription(IOpenIgiLogger logger)
        {
            _logger = logger;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }

        private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
            _logger.Fatal(
                Category,
                e.IsTerminating ? "Unhandled exception; the process is ending." : "Unhandled exception.",
                e.ExceptionObject as Exception);

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
            _logger.Error(Category, "A background task failed and nothing observed it.", e.Exception);
    }
}

using OpenIGI.Host.Configuration;
using OpenIGI.Rendering.Streamline;

namespace OpenIGI.Desktop;

/// <summary>Decides which folder, if any, the NVIDIA Streamline runtime is loaded from.</summary>
/// <remarks>
/// <para>
/// The runtime is optional and proprietary, so the game never carries it: a player gets it as the
/// launcher's <c>nvidia-streamline</c> runtime pack, which the launcher names with
/// <see cref="EnvironmentVariable"/> when it starts the game; a development checkout gets it from
/// <c>tools/fetch-streamline.ps1</c> into <c>third-party/streamline</c>.
/// </para>
/// <para>
/// Order: <c>-nostreamline</c> turns it off; <c>-streamline &lt;dir&gt;</c> names a folder outright; then the
/// launcher's pack; then a development checkout above the executable. Finding a folder is not the same as
/// loading it — the backend still refuses one holding any DLL NVIDIA did not sign.
/// </para>
/// </remarks>
internal static class StreamlineLocator
{
    /// <summary>The variable the launcher sets to the installed runtime pack's folder.</summary>
    public const string EnvironmentVariable = "OPENIGI_STREAMLINE_PATH";

    /// <summary>Where a development checkout keeps the fetched runtime, relative to the repository root.</summary>
    public const string DevelopmentRelativeDirectory = "third-party/streamline";

    /// <summary>Finds the runtime for this run.</summary>
    /// <param name="options">The command line.</param>
    /// <returns>The options to hand the backend, or <see langword="null"/> to run without it.</returns>
    public static StreamlineOptions? Locate(HostOptions options) =>
        Locate(
            options,
            Environment.GetEnvironmentVariable,
            AppContext.BaseDirectory,
            Directory.Exists,
            OperatingSystem.IsWindows(),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenIGI",
                "streamline"));

    /// <summary>Finds the runtime, with everything it reads handed in.</summary>
    /// <param name="options">The command line.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="baseDirectory">The executable's folder, which the development walk starts from.</param>
    /// <param name="directoryExists">Answers whether a folder exists.</param>
    /// <param name="windows">Whether this is Windows, the only platform Streamline ships for.</param>
    /// <param name="logDirectory">Where Streamline may write its log and NGX its data.</param>
    /// <returns>The options, or <see langword="null"/>.</returns>
    internal static StreamlineOptions? Locate(
        HostOptions options,
        Func<string, string?> environment,
        string baseDirectory,
        Func<string, bool> directoryExists,
        bool windows,
        string logDirectory)
    {
        if (!windows || options.NoStreamline)
        {
            return null;
        }

        string? directory = options.StreamlineDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = environment(EnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            // A development build runs from bin/Release/net10.0 several levels below the repository root,
            // so the walk upwards is what finds the fetched folder with nothing configured.
            DirectoryInfo? probe = new(baseDirectory);
            for (int depth = 0; probe is not null && depth < 8; depth++, probe = probe.Parent)
            {
                string candidate = Path.Combine(probe.FullName, DevelopmentRelativeDirectory);
                if (directoryExists(candidate))
                {
                    directory = candidate;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(directory) || !directoryExists(directory))
        {
            return null;
        }

        return new StreamlineOptions(Path.GetFullPath(directory), options.StreamlineDevelopment, logDirectory);
    }
}

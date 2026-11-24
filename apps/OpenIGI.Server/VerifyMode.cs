using System.Text.Json;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Engine.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Replays;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Replays;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Server;

/// <summary>
/// The server as a replay checker (<c>-verify &lt;file&gt;</c>): one ranked run played again in a level of the game's own, headless
/// and only simulated, as fast as it runs, and its verdict written to standard output as one line of JSON. Exit code nought for a
/// run verified, one for one refused, two where it could not be checked at all (the game not installed, the file not a replay).
/// </summary>
/// <remarks>
/// With <c>-out &lt;file&gt;</c>, a verified run is written there again carrying the ghost the runner made here: the one a race
/// against it shows, which the runner cannot have made up. The log goes to standard error, so the verdict is all that standard
/// output holds.
/// <para>
/// The game may be installed here more than once -- its editions differ in the files a run plays from -- each named in
/// <c>-game</c> or the game's path variable, separated as <c>PATH</c> is: a run is played in each in turn until one holds
/// the files it was played from.
/// </para>
/// </remarks>
internal static class VerifyMode
{
    /// <summary>The exit code of a run verified.</summary>
    public const int Verified = 0;

    /// <summary>The exit code of a run refused.</summary>
    public const int Refused = 1;

    /// <summary>The exit code of a run that could not be checked.</summary>
    public const int Unchecked = 2;

    /// <summary>Gets whether the arguments ask for a check.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>Whether they do.</returns>
    public static bool Asked(string[] args) => Array.Exists(args, static a => a.Equals("-verify", StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks the run the arguments name.</summary>
    /// <param name="args">The command line: <c>-verify &lt;file&gt; [-game &lt;dir&gt;] [-out &lt;file&gt;]</c>.</param>
    /// <returns>The exit code.</returns>
    public static int Run(string[] args)
    {
        using LogRouter logger = new();
#pragma warning disable RS0030 // A checker's verdict is its standard output; its log goes to standard error.
        logger.AddSink(new ConsoleSink(Console.Error, Console.Error, minimumLevel: LogLevel.Error));
        Stream stdout = Console.OpenStandardOutput();
#pragma warning restore RS0030

        string? path = Value(args, "-verify");
        if (path is null || !File.Exists(path))
        {
            return Write(stdout, Unchecked, "no replay file was given, or it is not there", null);
        }

        ReplayFile replay;
        try
        {
            replay = ReplayFile.Read(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Write(stdout, Refused, $"it is not a replay: {ex.Message.TrimEnd('.')}", null);
        }

        if (!TitleCatalog.TryResolve(replay.Header.Title, out IGameTitleRuntime? runtime))
        {
            return Write(stdout, Refused, $"it is of a game this build does not run ({replay.Header.Title})", null, replay.Header);
        }

        // Another build's run is left for a checker of that build, which plays it as it was played: never refused here.
        if (!string.Equals(replay.Header.GameVersion, GameHost.BuildVersion, StringComparison.Ordinal))
        {
            return Write(stdout, Unchecked, $"it was recorded by {replay.Header.GameVersion}, and this checker is {GameHost.BuildVersion}", null, replay.Header);
        }

        GameTitle title = runtime.Title;
        string[] installs = Installs(title, Value(args, "-game"));
        if (installs.Length == 0)
        {
            return Write(stdout, Unchecked, $"no {title.DisplayName} installation to check it in", null);
        }

        ReplayVerdict verdict = ReplayVerdict.Refused(ReplayVerifier.OtherFiles, replay.Header.Ruleset);
        foreach (string gamePath in installs)
        {
            verdict = Check(replay, path, runtime, gamePath, logger);
            if (verdict.Reason != ReplayVerifier.OtherFiles)
            {
                break;
            }
        }

        if (verdict.Verified && Value(args, "-out") is { } output)
        {
            File.WriteAllBytes(output, (replay with { Ghost = verdict.Ghost }).ToBytes());
        }

        return Write(stdout, verdict.Verified ? Verified : Refused, verdict.Reason, verdict, replay.Header);
    }

    /// <summary>
    /// The installations of a game here: those named (<c>-game</c>, else the game's path variable), separated as <c>PATH</c> is,
    /// each a different edition maybe; else the ones the machine has.
    /// </summary>
    /// <param name="title">The game.</param>
    /// <param name="named">The folders named on the command line, or null.</param>
    /// <returns>The folders that are there.</returns>
    internal static string[] Installs(GameTitle title, string? named)
    {
        named ??= Environment.GetEnvironmentVariable(TitleCatalog.GamePathVariableFor(title));
        return named is { Length: > 0 }
            ? [.. named.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Directory.Exists)]
            : [.. GameInstalls.Find(title)];
    }

    /// <summary>A headless, only simulated host over one installation of a game, nothing saved.</summary>
    /// <param name="runtime">The game.</param>
    /// <param name="gamePath">The installation.</param>
    /// <param name="logger">The log.</param>
    /// <param name="device">The picture it draws nothing into.</param>
    /// <param name="hostArgs">The host's own options besides those.</param>
    /// <returns>The host, not yet started.</returns>
    internal static GameHost NewHost(IGameTitleRuntime runtime, string gamePath, LogRouter logger, RecordingGraphicsDevice device, params string[] hostArgs)
    {
        VirtualFileSystem fileSystem = new(logger);
        fileSystem.RegisterDevice(new LocalFileDevice(gamePath));
        FileSystemConfig.LoadOrDefault(Path.Combine(gamePath, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(fileSystem);
        HostOptions options = HostOptions.Parse([.. hostArgs, "-lockstep", "-frames", "2000000000", "-nosave"]) with { SimulationOnly = true };
        return new GameHost(
            new HeadlessPlatformHost(),
            new HeadlessGameWindow(),
            device,
            NullAudioDeviceFactory.Instance,
            InlineWorkScheduler.Instance,
            AlwaysReadyLoadGate.Instance,
            TitleCatalog.Catalog,
            runtime.Title,
            fileSystem,
            options,
            gamePath,
            logger);
    }

    // The run played again in one installation of its game.
    private static ReplayVerdict Check(ReplayFile replay, string path, IGameTitleRuntime runtime, string gamePath, LogRouter logger)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        using GameHost host = NewHost(runtime, gamePath, logger, device, "-playreplay", path);
        return ReplayVerification.Run(host, replay);
    }

    // The verdict as one line of JSON, and the exit code it goes with: with the run's header, what it says it is of and who
    // recorded it, which the boards hold against who submitted it.
    private static int Write(Stream stdout, int code, string reason, ReplayVerdict? verdict, ReplayHeader? header = null)
    {
        using (Utf8JsonWriter json = new(stdout))
        {
            json.WriteStartObject();
            json.WriteBoolean("verified", code == Verified);
            json.WriteString("reason", reason);
            json.WriteBoolean("checked", code != Unchecked);
            if (header is not null)
            {
                json.WriteString("title", header.Title);
                json.WriteNumber("mission", header.Mission);
                json.WriteNumber("difficulty", header.Difficulty);
                json.WriteString("player", header.Player);
                json.WriteString("version", header.GameVersion);

                // When the run began by the player's clock: a run that reaches the boards sooner after it than it lasts was not
                // played as it ran, which the boards flag for a person to look at.
                json.WriteNumber("recordedAt", header.RecordedAt);
            }

            if (verdict is not null)
            {
                json.WriteString("ruleset", verdict.Ruleset);
                json.WriteNumber("endTick", verdict.EndTick);
                json.WriteNumber("rank", verdict.Rank);
                if (verdict.PartedAt is { } parted)
                {
                    json.WriteNumber("partedAt", parted);
                }

                if (verdict.Score is { } score)
                {
                    json.WriteStartObject("score");
                    json.WriteNumber("ticks", score.Ticks);
                    json.WriteNumber("timesSpotted", score.TimesSpotted);
                    json.WriteNumber("health", score.Health);
                    json.WriteNumber("shots", score.Shots);
                    json.WriteNumber("hits", score.Hits);
                    json.WriteNumber("kills", score.Kills);
                    json.WriteNumber("alarms", score.TimesAlarmTriggered);
                    json.WriteNumber("difficulty", score.Difficulty);
                    json.WriteEndObject();
                }
            }

            json.WriteEndObject();
        }

        stdout.WriteByte((byte)'\n');
        stdout.Flush();
        return code;
    }

    private static string? Value(string[] args, string name)
    {
        int at = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}

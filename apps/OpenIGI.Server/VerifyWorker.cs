using System.Diagnostics;
using System.Net;
using System.Text;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Net.Identity;
using OpenIGI.Releases;

namespace OpenIGI.Server;

/// <summary>
/// The server as the boards' checker (<c>-verify-worker</c>): it asks the master for the oldest run waiting, checks it in a
/// process of its own (<see cref="VerifyMode"/>, bounded in time, so a run that hangs or a level that throws takes nothing else
/// down), and sends the verdict back with the run written again carrying its ghost. Every request is signed by the checker's
/// own key, which an admin registers with the master (<c>/v1/admin/run-verifiers</c>); its public key is logged as it starts.
/// </summary>
/// <remarks>
/// A run this machine cannot check -- its game not installed here -- gets no verdict: it waits for another checker, or is let go
/// after a day. One run at a time.
/// </remarks>
internal static class VerifyWorker
{
    private const string LogCategory = "Checker";

    // How long the checker waits when nothing is waiting, and how long one check may take.
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(20);

    /// <summary>Gets whether the arguments ask for the checker.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>Whether they do.</returns>
    public static bool Asked(string[] args) => Array.Exists(args, static a => a.Equals("-verify-worker", StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks the master's runs until stopped.</summary>
    /// <param name="args">The command line: <c>-verify-worker [-master &lt;url&gt;] [-key &lt;file&gt;]</c>.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        using LogRouter logger = new();
        logger.AddSink(new ConsoleSink(minimumLevel: LogLevel.Information));
        Uri master = new(Value(args, "-master") ?? "https://master.openigi.com/");
        using EcdsaPlayerIdentity key = EcdsaPlayerIdentity.LoadOrCreate(Value(args, "-key") ?? "verifier.key");
        PlayerRequestSigner signer = new(key);
        logger.Information(LogCategory, $"Checking runs for {master} as {signer.PlayerId}: {signer.PublicKey}");

        // Ctrl+C at a terminal, or the service manager's stop: the run being checked is left for another checker.
        using CancellationTokenSource stop = new();
        using System.Runtime.InteropServices.PosixSignalRegistration interrupt = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGINT, context => { context.Cancel = true; stop.Cancel(); });
        using System.Runtime.InteropServices.PosixSignalRegistration terminate = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, context => { context.Cancel = true; stop.Cancel(); });

        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
        string scratch = Path.Combine(Path.GetTempPath(), $"openigi-checker-{Environment.ProcessId}");
        Directory.CreateDirectory(scratch);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (!await CheckNextAsync(http, signer, master, scratch, logger, stop.Token).ConfigureAwait(false))
                {
                    await Task.Delay(Idle, stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                logger.Warning(LogCategory, $"The master could not be reached: {ex.Message}");
                await Task.Delay(Idle, stop.Token).ConfigureAwait(false);
            }
        }

        return 0;
    }

    // One run taken and checked, if one is waiting: whether one was.
    private static async Task<bool> CheckNextAsync(HttpClient http, PlayerRequestSigner signer, Uri master, string scratch, IOpenIgiLogger logger, CancellationToken stop)
    {
        using HttpRequestMessage ask = await SignedRequest.CreateAsync(signer, HttpMethod.Get, master, "/v1/verifier/next", []).ConfigureAwait(false);
        using HttpResponseMessage next = await http.SendAsync(ask, stop).ConfigureAwait(false);
        if (next.StatusCode == HttpStatusCode.NoContent)
        {
            return false;
        }

        next.EnsureSuccessStatusCode();
        string id = next.Headers.TryGetValues("x-openigi-run", out IEnumerable<string>? ids) ? ids.First() : string.Empty;
        if (id.Length == 0 || !id.All(char.IsAsciiLetterOrDigit))
        {
            logger.Warning(LogCategory, "The master sent a run with no id.");
            return false;
        }

        string file = Path.Combine(scratch, id + ".oigr");
        string written = Path.Combine(scratch, id + ".checked.oigr");
        await File.WriteAllBytesAsync(file, await next.Content.ReadAsByteArrayAsync(stop).ConfigureAwait(false), stop).ConfigureAwait(false);
        try
        {
            (int code, string verdict) = await CheckAsync(file, written, stop).ConfigureAwait(false);
            if (code == VerifyMode.Unchecked)
            {
                logger.Warning(LogCategory, $"Run {id} could not be checked here ({verdict}); left for another checker.");
                return true;
            }

            byte[] replay = code == VerifyMode.Verified && File.Exists(written) ? await File.ReadAllBytesAsync(written, stop).ConfigureAwait(false) : [];
            using HttpRequestMessage send = await SignedRequest.CreateAsync(
                signer, HttpMethod.Post, master, $"/v1/verifier/runs/{id}", VerdictBody(verdict, replay), "application/octet-stream").ConfigureAwait(false);
            using HttpResponseMessage sent = await http.SendAsync(send, stop).ConfigureAwait(false);
            logger.Information(LogCategory, $"Run {id}: {(code == VerifyMode.Verified ? "verified" : "refused")}, the master said {(int)sent.StatusCode}.");
            return true;
        }
        finally
        {
            File.Delete(file);
            File.Delete(written);
        }
    }

    // A run checked in a process of its own, within the limit: its exit code and its verdict's JSON line.
    private static async Task<(int Code, string Verdict)> CheckAsync(string file, string written, CancellationToken stop)
    {
        ProcessStartInfo start = new(Environment.ProcessPath ?? "OpenIGI.Server")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-verify");
        start.ArgumentList.Add(file);
        start.ArgumentList.Add("-out");
        start.ArgumentList.Add(written);

        using Process check = Process.Start(start) ?? throw new IOException("The check could not be started.");
        Task<string> output = check.StandardOutput.ReadToEndAsync(stop);
        _ = check.StandardError.ReadToEndAsync(stop);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(Limit);
        try
        {
            await check.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            check.Kill(entireProcessTree: true);
            return (VerifyMode.Refused, """{"verified":false,"reason":"its check ran out of time","checked":true}""");
        }

        string line = (await output.ConfigureAwait(false)).Trim();
        return (check.ExitCode, line.Length > 0 ? line : """{"verified":false,"reason":"its check said nothing","checked":false}""");
    }

    /// <summary>The body a verdict goes to the master in: its JSON's length (four bytes, little-endian), the JSON, the replay.</summary>
    /// <param name="verdict">The verdict's JSON.</param>
    /// <param name="replay">The run written again with its ghost, or nothing for a run refused.</param>
    /// <returns>The body.</returns>
    public static byte[] VerdictBody(string verdict, byte[] replay)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(replay);
        byte[] json = Encoding.UTF8.GetBytes(verdict);
        byte[] body = new byte[4 + json.Length + replay.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body, json.Length);
        json.CopyTo(body, 4);
        replay.CopyTo(body, 4 + json.Length);
        return body;
    }

    private static string? Value(string[] args, string name)
    {
        int at = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}

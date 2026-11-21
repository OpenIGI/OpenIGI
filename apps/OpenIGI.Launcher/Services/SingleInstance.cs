using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

namespace OpenIGI.Launcher.Services;

/// <summary>
/// One launcher at a time for each user: a launcher started while one already runs -- most often by an <c>openigi://</c> link
/// clicked on the website while the first sits in the tray -- hands what it was started with to the one running and closes,
/// so the game starts from the launcher already open instead of a second one popping up beside it.
/// </summary>
/// <remarks>
/// The launchers speak over a local pipe named for the user; what is passed is one line, the link or <c>show</c>, which the
/// running launcher reads as a link again (<see cref="InviteLinks.Read"/>) and acts on only if it is one. Any other process of
/// the user's could write to the pipe as well, and could only ever start the game into a room, a party or a server.
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    // The longest a line passed on may be: an openigi:// link is far shorter.
    private const int LineLimit = 512;

    // How long a second launcher waits for the first to take its line, in milliseconds.
    private const int ConnectTimeout = 800;

    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _received = new();

    private SingleInstance()
    {
    }

    /// <summary>Gets the pipe's name, the user's own.</summary>
    private static string PipeName => $"OpenIGI.Launcher.{Environment.UserName}";

    /// <summary>
    /// Hands the arguments to a launcher already running, or becomes the one running: <see langword="null"/> when another took
    /// them (this launcher should close), else the listener that takes lines from later launchers.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>The listener, or <see langword="null"/> when a launcher already runs.</returns>
    public static SingleInstance? Claim(string[] args)
    {
        string line = args.FirstOrDefault(static arg => arg.StartsWith(InviteLinks.Scheme + "://", StringComparison.OrdinalIgnoreCase)) ?? "show";
        try
        {
            using NamedPipeClientStream client = new(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeout);
            byte[] bytes = Encoding.UTF8.GetBytes(line[..Math.Min(line.Length, LineLimit)] + "\n");
            client.Write(bytes);
            client.Flush();
            return null;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // Nobody listening: this is the launcher.
        }

        SingleInstance instance = new();
        _ = instance.ListenAsync();
        return instance;
    }

    /// <summary>Takes the next line a later launcher passed on, or <see langword="null"/> for none. Called each frame.</summary>
    /// <returns>The line: a link, or <c>show</c>.</returns>
    public string? Take() => _received.TryDequeue(out string? line) ? line : null;

    /// <inheritdoc/>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    // A line from each launcher that comes, for as long as this one runs.
    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using NamedPipeServerStream server = new(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using StreamReader reader = new(server, Encoding.UTF8);
                char[] buffer = new char[LineLimit];
                int read = await reader.ReadBlockAsync(buffer.AsMemory(), _stop.Token).ConfigureAwait(false);
                string line = new string(buffer, 0, read).Split('\n')[0].Trim();
                if (line.Length > 0)
                {
                    _received.Enqueue(line);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A launcher that left mid-line: wait for the next.
            }
        }
    }
}

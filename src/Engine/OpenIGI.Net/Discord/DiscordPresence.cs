using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Net.Discord;

/// <summary>What Discord said in one frame, put as a line of the log.</summary>
/// <param name="Accepted">Whether it took what it was sent.</param>
/// <param name="Closed">Whether it closed the connection with it.</param>
/// <param name="Text">What it said.</param>
public readonly record struct PresenceReply(bool Accepted, bool Closed, string Text);

/// <summary>A link button on the player's Discord activity.</summary>
/// <param name="Label">What it says, 32 characters at most.</param>
/// <param name="Url">Where it goes.</param>
public sealed record PresenceButton(string Label, string Url);

/// <summary>What the player is doing, as their Discord profile shows it.</summary>
/// <param name="Details">The first line, such as "Team Deathmatch · Trainyard".</param>
/// <param name="State">The second line, such as "Official OpenIGI EU".</param>
/// <param name="StartedAt">When it began, for Discord's clock; <see langword="null"/> for none.</param>
/// <param name="PartySize">How many are playing, with <paramref name="PartyMax"/>; 0 for no count.</param>
/// <param name="PartyMax">How many may.</param>
/// <param name="Buttons">Up to two link buttons: join the game, play in the browser.</param>
/// <remarks>
/// Equal when everything shown is: the buttons compared one by one, not as the list holding them, since the game builds a
/// new list each time it asks. Compared by reference, every ask was a change, and Discord was sent the same activity
/// every few seconds.
/// </remarks>
public sealed record PresenceActivity(
    string Details, string State, DateTimeOffset? StartedAt, int PartySize, int PartyMax, IReadOnlyList<PresenceButton> Buttons)
{
    /// <inheritdoc/>
    public bool Equals(PresenceActivity? other) =>
        other is not null
        && Details == other.Details
        && State == other.State
        && StartedAt == other.StartedAt
        && PartySize == other.PartySize
        && PartyMax == other.PartyMax
        && Buttons.SequenceEqual(other.Buttons);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Details, State, StartedAt, PartySize, PartyMax, Buttons.Count);
}

/// <summary>
/// Discord's local connection, as far as showing an activity goes: a named pipe (Windows) or a Unix socket the Discord app
/// keeps open as <c>discord-ipc-0</c> to <c>-9</c>, carrying frames of an opcode, a length and JSON. The game says who it is
/// once (the handshake), then what the player is doing whenever that changes. No Discord SDK, nothing to install; with
/// Discord not running it simply tries again now and then.
/// </summary>
/// <remarks>
/// Its connection runs on the work scheduler, beside the frame: the game only hands it the activity wanted, and nothing here
/// can stall a frame. Started only where the scheduler is concurrent (never in a browser, which has no Discord to talk to).
/// </remarks>
public sealed class DiscordPresence : IDisposable
{
    /// <summary>The opcode of the handshake frame.</summary>
    public const int Handshake = 0;

    /// <summary>The opcode of a command frame.</summary>
    public const int Frame = 1;

    /// <summary>The opcode of a closing frame.</summary>
    public const int Close = 2;

    /// <summary>The opcode of Discord's ping, answered with <see cref="Pong"/> and the same body.</summary>
    public const int Ping = 3;

    /// <summary>The opcode of the answer to a <see cref="Ping"/>.</summary>
    public const int Pong = 4;

    /// <summary>The category the connection logs under: connecting, what Discord took, and what it refused.</summary>
    public const string LogCategory = "Discord";

    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(20);

    private readonly string _clientId;
    private readonly IOpenIgiLogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _running;
    private readonly object _gate = new();
    private PresenceActivity? _wanted;
    private int _nonce;

    // The last refusal logged, so one Discord repeats on every reconnect is logged once; touched only by the connection.
    private string? _lastRefusal;

    /// <summary>Initializes a new instance of the <see cref="DiscordPresence"/> class and starts its connection.</summary>
    /// <param name="clientId">The Discord application the activity is shown as.</param>
    /// <param name="work">Where the connection runs; it must run beside the caller.</param>
    /// <param name="logger">Where Discord's answers are logged, under <see cref="LogCategory"/>.</param>
    public DiscordPresence(string clientId, Core.Threading.IWorkScheduler work, IOpenIgiLogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(logger);
        if (!work.IsConcurrent)
        {
            throw new ArgumentException("The presence's connection waits on Discord, so it needs a scheduler that runs beside the frame.", nameof(work));
        }

        _clientId = clientId;
        _logger = logger;
        _running = work.Run(Run);
    }

    /// <summary>Shows an activity, or clears it with <see langword="null"/>; sent as soon as Discord is there to take it.</summary>
    /// <param name="activity">The activity.</param>
    public void Show(PresenceActivity? activity)
    {
        lock (_gate)
        {
            if (!Equals(activity, _wanted))
            {
                _wanted = activity;
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stop.Cancel();
        lock (_gate)
        {
            Monitor.PulseAll(_gate);
        }

        _running.Wait(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    /// <summary>A frame: the opcode and the JSON's length, little-endian, then the JSON.</summary>
    /// <param name="opcode">The opcode.</param>
    /// <param name="json">The JSON.</param>
    /// <returns>The frame's bytes.</returns>
    public static byte[] Encode(int opcode, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        return frame;
    }

    /// <summary>The handshake's JSON.</summary>
    /// <param name="clientId">The application.</param>
    /// <returns>The JSON.</returns>
    public static string HandshakeJson(string clientId)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("client_id", clientId);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The SET_ACTIVITY command's JSON.</summary>
    /// <param name="processId">This process, which Discord ties the activity to.</param>
    /// <param name="activity">The activity, or <see langword="null"/> to clear it.</param>
    /// <param name="nonce">The command's own number.</param>
    /// <returns>The JSON.</returns>
    public static string SetActivityJson(int processId, PresenceActivity? activity, int nonce)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("cmd", "SET_ACTIVITY");
            json.WriteString("nonce", nonce.ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.WriteStartObject("args");
            json.WriteNumber("pid", processId);
            if (activity is null)
            {
                json.WriteNull("activity");
            }
            else
            {
                json.WriteStartObject("activity");
                json.WriteString("details", Clip(activity.Details, 128));
                json.WriteString("state", Clip(activity.State, 128));
                if (activity.StartedAt is { } started)
                {
                    json.WriteStartObject("timestamps");
                    json.WriteNumber("start", started.ToUnixTimeSeconds());
                    json.WriteEndObject();
                }

                json.WriteStartObject("assets");
                json.WriteString("large_image", "logo");
                json.WriteString("large_text", "OpenIGI");
                json.WriteEndObject();

                if (activity.PartySize > 0 && activity.PartyMax >= activity.PartySize)
                {
                    json.WriteStartObject("party");
                    json.WriteStartArray("size");
                    json.WriteNumberValue(activity.PartySize);
                    json.WriteNumberValue(activity.PartyMax);
                    json.WriteEndArray();
                    json.WriteEndObject();
                }

                if (activity.Buttons.Count > 0)
                {
                    json.WriteStartArray("buttons");
                    foreach (PresenceButton button in activity.Buttons.Take(2))
                    {
                        json.WriteStartObject();
                        json.WriteString("label", Clip(button.Label, 32));
                        json.WriteString("url", button.Url);
                        json.WriteEndObject();
                    }

                    json.WriteEndArray();
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>What a frame from Discord says: the handshake taken, the activity it now shows, a refusal, or a close.</summary>
    /// <param name="opcode">The frame's opcode.</param>
    /// <param name="json">The frame's JSON.</param>
    /// <returns>The reply.</returns>
    public static PresenceReply Interpret(int opcode, string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (opcode == Close)
            {
                return new PresenceReply(false, true, $"Discord closed the connection: {Reason(root)}");
            }

            string? command = Text(root, "cmd");
            string? evt = Text(root, "evt");
            JsonElement data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement d) ? d : default;
            if (evt == "ERROR")
            {
                return new PresenceReply(false, false, $"Discord refused {command ?? "a command"}: {Reason(data)}");
            }

            if (evt == "READY")
            {
                return new PresenceReply(true, false, "Connected; Discord took the handshake.");
            }

            if (command == "SET_ACTIVITY")
            {
                return new PresenceReply(
                    true, false,
                    data.ValueKind == JsonValueKind.Object
                        ? $"Discord shows the activity \"{Text(data, "details")}\" / \"{Text(data, "state")}\"."
                        : "Discord cleared the activity.");
            }

            return new PresenceReply(true, false, $"Discord answered {command ?? $"opcode {opcode}"}{(evt is null ? string.Empty : $" ({evt})")}.");
        }
        catch (JsonException)
        {
            return new PresenceReply(false, opcode == Close, $"Discord sent a frame (opcode {opcode}) that is not JSON.");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Discord's reason, as its close and error frames give one: a message and a code.
    private static string Reason(JsonElement element)
    {
        string message = Text(element, "message") ?? "no reason given";
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty("code", out JsonElement code) && code.ValueKind == JsonValueKind.Number
            ? $"{message} (code {code.GetRawText()})"
            : message;
    }

    // Discord wants two characters at least in a line it shows, and no more than its limit.
    private static string Clip(string text, int max) =>
        text.Length < 2 ? (text + "  ")[..2] : text.Length > max ? text[..max] : text;

    private void Run()
    {
        CancellationToken stop = _stop.Token;
        bool missing = false;
        while (!stop.IsCancellationRequested)
        {
            Stream? connection = Connect(out string pipe);
            if (connection is null)
            {
                // Said once for as long as Discord stays away, not on every look.
                if (!missing)
                {
                    _logger.Information(LogCategory, $"Discord is not running; looking for it every {RetryEvery.TotalSeconds:0} s.");
                    missing = true;
                }

                Wait(RetryEvery);
                continue;
            }

            missing = false;
            using (connection)
            {
                Converse(connection, pipe, stop);
            }

            Wait(RetryEvery);
        }
    }

    // One connection, from the handshake until Discord goes, closes it, or the game stops.
    private void Converse(Stream connection, string pipe, CancellationToken stop)
    {
        try
        {
            Write(connection, Handshake, HandshakeJson(_clientId));
            if (!Heard(Answer(connection), pipe))
            {
                return;
            }

            // Whatever is wanted is sent at once, then again on each change, until the connection goes.
            PresenceActivity? sent = null;
            bool first = true;
            while (!stop.IsCancellationRequested)
            {
                PresenceActivity? wanted;
                lock (_gate)
                {
                    while (!first && Equals(_wanted, sent) && !stop.IsCancellationRequested)
                    {
                        Monitor.Wait(_gate, TimeSpan.FromSeconds(30));
                    }

                    wanted = _wanted;
                }

                if (stop.IsCancellationRequested)
                {
                    break;
                }

                first = false;
                Write(connection, Frame, SetActivityJson(Environment.ProcessId, wanted, Interlocked.Increment(ref _nonce)));
                if (!Heard(Answer(connection), pipe))
                {
                    return;
                }

                sent = wanted;
            }

            Write(connection, Close, "{}");
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or SocketException or InvalidDataException)
        {
            if (!stop.IsCancellationRequested)
            {
                _logger.Information(LogCategory, $"Lost Discord on {pipe} ({error.Message}); looking again in {RetryEvery.TotalSeconds:0} s.");
            }
        }
    }

    // A reply logged: what Discord took as information, a refusal as a warning (once, however often Discord repeats it).
    // False when Discord closed the connection with it.
    private bool Heard(PresenceReply reply, string pipe)
    {
        if (reply.Accepted)
        {
            _lastRefusal = null;
            _logger.Information(LogCategory, $"{reply.Text} ({pipe})");
        }
        else if (reply.Text != _lastRefusal)
        {
            _lastRefusal = reply.Text;
            _logger.Warning(LogCategory, $"{reply.Text} ({pipe})");
        }

        return !reply.Closed;
    }

    private void Wait(TimeSpan time)
    {
        lock (_gate)
        {
            if (!_stop.IsCancellationRequested)
            {
                Monitor.Wait(_gate, time);
            }
        }
    }

    private static void Write(Stream connection, int opcode, string json)
    {
        connection.Write(Encode(opcode, json));
        connection.Flush();
    }

    // Discord's answer to the frame just sent; a ping on the way is answered and read past.
    private static PresenceReply Answer(Stream connection)
    {
        while (true)
        {
            (int opcode, string json) = ReadFrame(connection);
            if (opcode != Ping)
            {
                return Interpret(opcode, json);
            }

            Write(connection, Pong, json);
        }
    }

    private static (int Opcode, string Json) ReadFrame(Stream connection)
    {
        Span<byte> head = stackalloc byte[8];
        connection.ReadExactly(head);
        int opcode = BinaryPrimitives.ReadInt32LittleEndian(head);
        int length = BinaryPrimitives.ReadInt32LittleEndian(head[4..]);
        if (length is < 0 or > 1 << 20)
        {
            throw new InvalidDataException("Discord sent a frame no frame is.");
        }

        byte[] body = new byte[length];
        connection.ReadExactly(body);
        return (opcode, Encoding.UTF8.GetString(body));
    }

    // The first of discord-ipc-0..9 that answers, and its name for the log.
    private static Stream? Connect(out string name)
    {
        for (int i = 0; i < 10; i++)
        {
            name = $"discord-ipc-{i}";
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    NamedPipeClientStream pipe = new(".", name, PipeDirection.InOut, PipeOptions.None);
                    try
                    {
                        pipe.Connect(200);
                        return pipe;
                    }
                    catch
                    {
                        pipe.Dispose();
                        throw;
                    }
                }

                foreach (string folder in SocketFolders())
                {
                    string path = Path.Combine(folder, $"discord-ipc-{i}");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        socket.Connect(new UnixDomainSocketEndPoint(path));
                        name = path;
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            }
            catch (Exception error) when (error is IOException or TimeoutException or SocketException or UnauthorizedAccessException)
            {
                // Not this one.
            }
        }

        name = string.Empty;
        return null;
    }

    // Where Discord puts its socket on Linux and macOS, flatpak and snap included.
    private static IEnumerable<string> SocketFolders()
    {
        string? runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        string temp = Environment.GetEnvironmentVariable("TMPDIR") ?? Environment.GetEnvironmentVariable("TMP") ?? Environment.GetEnvironmentVariable("TEMP") ?? "/tmp";
        foreach (string root in new[] { runtime, temp, "/tmp" }.Where(static r => !string.IsNullOrEmpty(r)).Distinct()!)
        {
            yield return root!;
            yield return Path.Combine(root!, "app", "com.discordapp.Discord");
            yield return Path.Combine(root!, "snap.discord");
        }
    }
}

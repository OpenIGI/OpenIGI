using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OpenIGI.Net.Discord;

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
public sealed record PresenceActivity(
    string Details, string State, DateTimeOffset? StartedAt, int PartySize, int PartyMax, IReadOnlyList<PresenceButton> Buttons);

/// <summary>
/// Discord's local connection, as far as showing an activity goes: a named pipe (Windows) or a Unix socket the Discord app
/// keeps open as <c>discord-ipc-0</c> to <c>-9</c>, carrying frames of an opcode, a length and JSON. The game says who it is
/// once (the handshake), then what the player is doing whenever that changes. No Discord SDK, nothing to install; with
/// Discord not running it simply tries again now and then.
/// </summary>
/// <remarks>
/// It runs on a thread of its own: the game only hands it the activity wanted, and nothing here can stall a frame.
/// </remarks>
public sealed class DiscordPresence : IDisposable
{
    /// <summary>The opcode of the handshake frame.</summary>
    public const int Handshake = 0;

    /// <summary>The opcode of a command frame.</summary>
    public const int Frame = 1;

    /// <summary>The opcode of a closing frame.</summary>
    public const int Close = 2;

    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(20);

    private readonly string _clientId;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly object _gate = new();
    private PresenceActivity? _wanted;
    private int _nonce;

    /// <summary>Initializes a new instance of the <see cref="DiscordPresence"/> class and starts its thread.</summary>
    /// <param name="clientId">The Discord application the activity is shown as.</param>
    public DiscordPresence(string clientId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        _clientId = clientId;
        _thread = new Thread(Run) { IsBackground = true, Name = "Discord presence" };
        _thread.Start();
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

        _thread.Join(TimeSpan.FromSeconds(2));
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

    // Discord wants two characters at least in a line it shows, and no more than its limit.
    private static string Clip(string text, int max) =>
        text.Length < 2 ? (text + "  ")[..2] : text.Length > max ? text[..max] : text;

    private void Run()
    {
        CancellationToken stop = _stop.Token;
        while (!stop.IsCancellationRequested)
        {
            using Stream? connection = Connect();
            if (connection is null)
            {
                Wait(RetryEvery);
                continue;
            }

            try
            {
                Write(connection, Handshake, HandshakeJson(_clientId));
                Read(connection);

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

                    first = false;
                    Write(connection, Frame, SetActivityJson(Environment.ProcessId, wanted, Interlocked.Increment(ref _nonce)));
                    Read(connection);
                    sent = wanted;
                }

                Write(connection, Close, "{}");
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or SocketException or InvalidDataException)
            {
                // Discord went away: tried again later.
            }

            Wait(RetryEvery);
        }
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

    // One frame read and let go: Discord's answer to each frame the game sends.
    private static void Read(Stream connection)
    {
        Span<byte> head = stackalloc byte[8];
        connection.ReadExactly(head);
        int length = BinaryPrimitives.ReadInt32LittleEndian(head[4..]);
        if (length is < 0 or > 1 << 20)
        {
            throw new InvalidDataException("Discord sent a frame no frame is.");
        }

        byte[] body = new byte[length];
        connection.ReadExactly(body);
    }

    // The first of discord-ipc-0..9 that answers.
    private static Stream? Connect()
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    NamedPipeClientStream pipe = new(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.None);
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

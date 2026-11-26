using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Net.Rooms;
using Xunit;

namespace OpenIGI.Net.Tests.Rooms;

/// <summary>
/// A room that carries the game itself, as a Discord Activity's must: a host opens it for an instance, a joiner is welcomed
/// into a relayed room, and datagrams go both ways through the room's sockets with no WebRTC at all. The master here is a
/// stand-in speaking the room protocol (tools/master-server/src/rooms.js; its relay is tested in test/relay.test.mjs).
/// </summary>
public sealed class RelayedRoomTests
{
    /// <summary>Host and joiner trade datagrams through the room, each told apart by its peer on the host's side.</summary>
    [Fact]
    public async Task Datagrams_go_both_ways_through_a_relayed_room()
    {
        await using FakeMaster master = FakeMaster.Start();
        RoomRequest request = new("igi1", "p", "Ana's game", "1", "coop", "0.7", 4, Public: false, Passworded: false, Instance: "i-1-gc-2");
        using RoomTransport host = RoomTransport.Host(master.Uri, request, new NoRtc(), NullLogger.Instance);
        Assert.True(host.RoomRelays);
        await Until(() => { host.Poll(); return host.Status == RoomStatus.Open; });

        using RoomTransport joiner = RoomTransport.Join(master.Uri, host.Code!, new NoRtc(), NullLogger.Instance);
        await Until(() => { joiner.Poll(); host.Poll(); return joiner.Status == RoomStatus.Open; });
        Assert.True(joiner.RoomRelays);

        joiner.Send(NetAddress.Peer(0), [1, 2, 3]);
        byte[] buffer = new byte[64];
        NetAddress from = default;
        int length = 0;

        // Both sides polled while waiting, as a game polls every frame: the joiner is welcomed before the host is told it
        // joined, so on a slow machine the host may not have taken the join in yet when the joiner is already open.
        await Until(() => { host.Poll(); joiner.Poll(); return host.TryReceive(buffer, out from, out length); });
        Assert.Equal([1, 2, 3], buffer[..length]);
        Assert.Equal(NetAddressKind.Peer, from.Kind);

        host.Send(from, [9, 8]);
        await Until(() => { host.Poll(); joiner.Poll(); return joiner.TryReceive(buffer, out _, out length); });
        Assert.Equal([9, 8], buffer[..length]);
        Assert.Equal("room relay", host.Route(from));
    }

    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 400; i++)
        {
            if (done())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("It never happened.");
    }

    // No WebRTC here: a relayed room must not ask for any.
    private sealed class NoRtc : IRtcPeerFactory
    {
        public IRtcPeer Create(IReadOnlyList<IceServer> iceServers, bool offerer) => throw new InvalidOperationException("WebRTC was asked for");
    }

    private sealed class NullLogger : IOpenIgiLogger
    {
        public static readonly NullLogger Instance = new();

        public bool IsEnabled(LogLevel level, string category) => false;

        public void Log(in LogRecord record)
        {
        }
    }

    // The room protocol, as far as a relayed room goes: made, the host's socket, a joiner welcomed, datagrams carried.
    private sealed class FakeMaster : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentDictionary<int, WebSocket> _joiners = new();
        private WebSocket? _host;
        private int _nextPeer = 1;
        private Task? _serving;

        public Uri Uri { get; private set; } = null!;

        public static FakeMaster Start()
        {
            FakeMaster master = new();
            int port = FreePort();
            master.Uri = new Uri($"http://127.0.0.1:{port}/");
            master._listener.Prefixes.Add(master.Uri.ToString());
            master._listener.Start();
            master._serving = master.ServeAsync();
            return master;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            _listener.Close();
            try
            {
                await _serving!;
            }
            catch (Exception)
            {
                // Stopped.
            }

            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (_stop.IsCancellationRequested)
                {
                    return;
                }

                _ = Task.Run(() => AnswerAsync(context));
            }
        }

        private async Task AnswerAsync(HttpListenerContext context)
        {
            string path = context.Request.Url!.AbsolutePath;
            if (context.Request.HttpMethod == "POST" && path == "/v1/rooms")
            {
                JsonObject asked = (JsonObject)JsonNode.Parse(await new StreamReader(context.Request.InputStream).ReadToEndAsync())!;
                Assert.Equal("i-1-gc-2", (string?)asked["instance"]);
                byte[] made = Encoding.UTF8.GetBytes("""{"code":"ABCDEF","hostToken":"t","ice":[]}""");
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(made);
                context.Response.Close();
                return;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            WebSocket socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            if (context.Request.QueryString["role"] == "host")
            {
                _host = socket;
                await SendText(socket, """{"type":"hosting","code":"ABCDEF"}""");
                await PumpAsync(socket, peer: null);
                return;
            }

            int peer = _nextPeer++;
            _joiners[peer] = socket;
            await SendText(socket, $$"""{"type":"welcome","peer":{{peer}},"room":{"room":"ABCDEF","relayed":true},"ice":[]}""");
            await SendText(_host!, $$"""{"type":"join","peer":{{peer}},"ice":[]}""");
            await PumpAsync(socket, peer);
        }

        // Each binary message carried as rooms.js carries it.
        private async Task PumpAsync(WebSocket socket, int? peer)
        {
            byte[] buffer = new byte[4096];
            while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, _stop.Token);
                }
                catch (Exception)
                {
                    return;
                }

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    continue;
                }

                if (peer is { } from)
                {
                    byte[] framed = [(byte)(from & 0xff), (byte)(from >> 8), .. buffer.AsSpan(0, result.Count)];
                    await _host!.SendAsync(framed, WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                else
                {
                    int to = buffer[0] | (buffer[1] << 8);
                    await _joiners[to].SendAsync(buffer.AsMemory(2, result.Count - 2), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
            }
        }

        private static Task SendText(WebSocket socket, string text) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

        private static int FreePort()
        {
            using TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
    }
}

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Net.WebRtc;

/// <summary>Makes libdatachannel connections: the desktop build's and the dedicated server's half of playing through a room.</summary>
public sealed class DataChannelPeerFactory : IRtcPeerFactory
{
    // The library logs on its own threads; the lines wait here for the game's thread (Drain), where the game's log is safe
    // to write. Bounded, in case nothing drains them.
    private static readonly ConcurrentQueue<string> s_log = new();
    private static IOpenIgiLogger? s_logger;
    private static int s_loggerSet;

    /// <summary>Initializes a new instance of the <see cref="DataChannelPeerFactory"/> class.</summary>
    /// <param name="logger">Where the library's own warnings go.</param>
    public DataChannelPeerFactory(IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        s_logger = logger;
        if (Interlocked.Exchange(ref s_loggerSet, 1) == 0)
        {
            unsafe
            {
                Native.InitLogger(Native.LogWarning, &OnLog);
            }
        }
    }

    /// <summary>Gets whether the native library can be loaded here.</summary>
    /// <returns><see langword="true"/> when it can.</returns>
    public static bool IsAvailable()
    {
        return NativeLibrary.TryLoad(Native.Library, typeof(Native).Assembly, null, out _);
    }

    /// <summary>Gets or sets whether connections only go through the TURN relay, never directly: for proving the relay works.</summary>
    public bool RelayOnly { get; set; }

    /// <inheritdoc/>
    public IRtcPeer Create(IReadOnlyList<IceServer> iceServers, bool offerer) => new DataChannelPeer(iceServers, offerer, RelayOnly);

    /// <summary>Writes what the library logged since the last call, on the calling (the network's) thread.</summary>
    internal static void Drain()
    {
        while (s_log.TryDequeue(out string? line))
        {
            s_logger?.Warning("WebRTC", line);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnLog(int level, byte* message)
    {
        try
        {
            // A player's own LAN addresses are offered as candidates too, and the relay refuses to relay into private
            // address space by design: those refusals are expected, not news.
            string text = Native.Text(message) ?? string.Empty;
            if (!text.Contains("CreatePermission error response, code=403", StringComparison.Ordinal) && s_log.Count < 256)
            {
                s_log.Enqueue(text);
            }
        }
        catch (Exception)
        {
            // Nothing may escape into the library's thread.
        }
    }
}

/// <summary>One libdatachannel connection with its one data channel, unordered and never resent.</summary>
internal sealed unsafe class DataChannelPeer : IRtcPeer
{
    // Datagrams waiting for the network's thread: a second's worth for a busy server. Past it the oldest are dropped, as a
    // full socket buffer drops them.
    private const int MaxQueued = 512;

    // The live connections by the token each gives the library as its user pointer. A callback that comes for one already
    // gone finds nothing, where a pointer to a freed handle would take the process down with it.
    private static readonly ConcurrentDictionary<nint, DataChannelPeer> s_live = new();
    private static long s_nextToken;

    private readonly ConcurrentQueue<RtcSignal> _signals = new();
    private readonly ConcurrentQueue<byte[]> _received = new();
    private readonly nint _token;
    private readonly int _pc;
    private volatile int _dc = -1;
    private volatile RtcPeerState _state = RtcPeerState.Connecting;
    private volatile string? _failure;
    private bool _disposed;

    public DataChannelPeer(IReadOnlyList<IceServer> iceServers, bool offerer, bool relayOnly = false)
    {
        _token = (nint)Interlocked.Increment(ref s_nextToken);
        s_live[_token] = this;
        List<string> urls = [.. iceServers.SelectMany(Urls)];
        nint[] strings = [.. urls.Select(Marshal.StringToCoTaskMemUTF8)];
        try
        {
            fixed (nint* servers = strings)
            {
                Native.Configuration config = new()
                {
                    IceServers = (nint)servers,
                    IceServersCount = strings.Length,
                    IceTransportPolicy = relayOnly ? 1 : 0,
                };
                _pc = Native.CreatePeerConnection(&config);
            }
        }
        finally
        {
            foreach (nint s in strings)
            {
                Marshal.FreeCoTaskMem(s);
            }
        }

        if (_pc < 0)
        {
            s_live.TryRemove(_token, out _);
            throw new InvalidOperationException($"libdatachannel could not make a connection ({_pc}).");
        }

        Native.SetUserPointer(_pc, _token);
        Check(Native.SetLocalDescriptionCallback(_pc, &OnDescription));
        Check(Native.SetLocalCandidateCallback(_pc, &OnCandidate));
        Check(Native.SetStateChangeCallback(_pc, &OnState));

        if (offerer)
        {
            // Making the channel starts the offer.
            Native.DataChannelInit init = new() { Reliability = new Native.Reliability { Unordered = 1, Unreliable = 1, MaxRetransmits = 0 } };
            Attach(Native.CreateDataChannel(_pc, "game", &init));
        }
        else
        {
            Check(Native.SetDataChannelCallback(_pc, &OnDataChannel));
        }
    }

    public RtcPeerState State => _state;

    public string? Failure => _failure;

    public void SetRemoteDescription(string kind, string sdp) => Native.SetRemoteDescription(_pc, sdp, kind);

    public void AddRemoteCandidate(string candidate, string? mid) => Native.AddRemoteCandidate(_pc, candidate, mid);

    public bool TryTakeSignal(out RtcSignal signal) => _signals.TryDequeue(out signal);

    public void Send(ReadOnlySpan<byte> datagram)
    {
        int dc = _dc;
        if (_state == RtcPeerState.Open && dc >= 0)
        {
            fixed (byte* bytes = datagram)
            {
                _ = Native.SendMessage(dc, bytes, datagram.Length);
            }
        }
    }

    public bool TryReceive(Span<byte> buffer, out int length)
    {
        DataChannelPeerFactory.Drain();
        while (_received.TryDequeue(out byte[]? datagram))
        {
            if (datagram.Length <= buffer.Length)
            {
                datagram.CopyTo(buffer);
                length = datagram.Length;
                return true;
            }
        }

        length = 0;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _state = RtcPeerState.Closed;

        // Out of the table first, so a callback still in flight finds nothing; then the library's side.
        s_live.TryRemove(_token, out _);
        if (_dc >= 0)
        {
            _ = Native.DeleteDataChannel(_dc);
        }

        _ = Native.DeletePeerConnection(_pc);
    }

    // libjuice relays over UDP alone, so a TURN address over TCP or TLS is no use to it; a relay login goes in the URL.
    private static IEnumerable<string> Urls(IceServer server)
    {
        foreach (string url in server.Urls)
        {
            if (url.StartsWith("stun:", StringComparison.Ordinal))
            {
                yield return url;
            }
            else if (url.StartsWith("turn:", StringComparison.Ordinal) && !url.Contains("transport=tcp", StringComparison.Ordinal)
                && server.Username is { } user && server.Credential is { } password)
            {
                yield return $"turn:{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password)}@{url["turn:".Length..]}";
            }
        }
    }

    private void Attach(int dc)
    {
        if (dc < 0)
        {
            Fail($"libdatachannel could not open a channel ({dc})");
            return;
        }

        _dc = dc;
        Native.SetUserPointer(dc, _token);

        // Also reached from inside a native callback, where a throw would take the process down: a refusal fails the
        // connection instead.
        if (Native.SetOpenCallback(dc, &OnOpen) < 0 || Native.SetClosedCallback(dc, &OnClosed) < 0
            || Native.SetErrorCallback(dc, &OnError) < 0 || Native.SetMessageCallback(dc, &OnMessage) < 0)
        {
            Fail("libdatachannel refused the channel's callbacks");
        }
    }

    private static void Check(int result)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"libdatachannel refused a callback ({result}).");
        }
    }

    private void Fail(string why)
    {
        _failure ??= why;
        _state = RtcPeerState.Closed;
    }

    private static DataChannelPeer? From(nint pointer) => s_live.TryGetValue(pointer, out DataChannelPeer? peer) ? peer : null;

    // Every callback runs on the library's threads, where an exception escaping would end the process: each is run
    // through here, and whatever goes wrong fails that one connection instead.
    private static void Guard(nint pointer, Action<DataChannelPeer> work)
    {
        if (From(pointer) is not { } peer)
        {
            return;
        }

        try
        {
            work(peer);
        }
        catch (Exception ex)
        {
            peer.Fail($"the connection broke ({ex.GetType().Name})");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDescription(int pc, byte* sdp, byte* type, nint pointer)
    {
        string? kind = Native.Text(type);
        string text = Native.Text(sdp) ?? string.Empty;
        Guard(pointer, peer => peer._signals.Enqueue(new RtcSignal(kind ?? "offer", text)));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCandidate(int pc, byte* candidate, byte* mid, nint pointer)
    {
        string line = Native.Text(candidate) ?? string.Empty;
        string? media = Native.Text(mid);
        Guard(pointer, peer => peer._signals.Enqueue(new RtcSignal("candidate", line, media)));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnState(int pc, int state, nint pointer)
    {
        if (state is Native.StateFailed or Native.StateClosed or Native.StateDisconnected)
        {
            Guard(pointer, peer => peer.Fail(state == Native.StateFailed ? "no way through to the other player" : "the connection dropped"));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDataChannel(int pc, int dc, nint pointer) => Guard(pointer, peer => peer.Attach(dc));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnOpen(int id, nint pointer) => Guard(pointer, peer =>
    {
        if (peer._state == RtcPeerState.Connecting)
        {
            peer._state = RtcPeerState.Open;
        }
    });

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClosed(int id, nint pointer) => Guard(pointer, peer => peer.Fail("the other player closed the channel"));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnError(int id, byte* error, nint pointer)
    {
        string why = Native.Text(error) ?? "channel error";
        Guard(pointer, peer => peer.Fail(why));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnMessage(int id, byte* message, int size, nint pointer)
    {
        // A negative size is a text message; the game sends none.
        if (size < 0 || size > NetConstants.MaxPacketSize * 2 || From(pointer) is not { } peer)
        {
            return;
        }

        try
        {
            peer._received.Enqueue(new ReadOnlySpan<byte>(message, size).ToArray());
            while (peer._received.Count > MaxQueued)
            {
                peer._received.TryDequeue(out _);
            }
        }
        catch (Exception)
        {
            // Nothing may escape into the library's thread; a datagram lost is one the network might have lost.
        }
    }
}

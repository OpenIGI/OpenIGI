using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Net.WebRtc;

/// <summary>Makes libdatachannel connections: the desktop build's and the dedicated server's half of playing through a room.</summary>
public sealed class DataChannelPeerFactory : IRtcPeerFactory
{
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnLog(int level, byte* message) =>
        s_logger?.Warning("WebRTC", Native.Text(message) ?? string.Empty);
}

/// <summary>One libdatachannel connection with its one data channel, unordered and never resent.</summary>
internal sealed unsafe class DataChannelPeer : IRtcPeer
{
    // Datagrams waiting for the network's thread: a second's worth for a busy server. Past it the oldest are dropped, as a
    // full socket buffer drops them.
    private const int MaxQueued = 512;

    private readonly ConcurrentQueue<RtcSignal> _signals = new();
    private readonly ConcurrentQueue<byte[]> _received = new();
    private readonly GCHandle _self;
    private readonly int _pc;
    private volatile int _dc = -1;
    private volatile RtcPeerState _state = RtcPeerState.Connecting;
    private volatile string? _failure;
    private bool _disposed;

    public DataChannelPeer(IReadOnlyList<IceServer> iceServers, bool offerer, bool relayOnly = false)
    {
        _self = GCHandle.Alloc(this);
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
            _self.Free();
            throw new InvalidOperationException($"libdatachannel could not make a connection ({_pc}).");
        }

        Native.SetUserPointer(_pc, GCHandle.ToIntPtr(_self));
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

        // Deleting waits out any callback in flight, after which none come; only then may the handle they use go.
        if (_dc >= 0)
        {
            _ = Native.DeleteDataChannel(_dc);
        }

        _ = Native.DeletePeerConnection(_pc);
        _self.Free();
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
        Native.SetUserPointer(dc, GCHandle.ToIntPtr(_self));
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

    private static DataChannelPeer? From(nint pointer) =>
        pointer == 0 ? null : GCHandle.FromIntPtr(pointer).Target as DataChannelPeer;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDescription(int pc, byte* sdp, byte* type, nint pointer) =>
        From(pointer)?._signals.Enqueue(new RtcSignal(Native.Text(type) ?? "offer", Native.Text(sdp) ?? string.Empty));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCandidate(int pc, byte* candidate, byte* mid, nint pointer) =>
        From(pointer)?._signals.Enqueue(new RtcSignal("candidate", Native.Text(candidate) ?? string.Empty, Native.Text(mid)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnState(int pc, int state, nint pointer)
    {
        if (state is Native.StateFailed or Native.StateClosed or Native.StateDisconnected && From(pointer) is { } peer)
        {
            peer.Fail(state == Native.StateFailed ? "no way through to the other player" : "the connection dropped");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDataChannel(int pc, int dc, nint pointer) => From(pointer)?.Attach(dc);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnOpen(int id, nint pointer)
    {
        if (From(pointer) is { _state: RtcPeerState.Connecting } peer)
        {
            peer._state = RtcPeerState.Open;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClosed(int id, nint pointer) => From(pointer)?.Fail("the other player closed the channel");

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnError(int id, byte* error, nint pointer) => From(pointer)?.Fail(Native.Text(error) ?? "channel error");

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnMessage(int id, byte* message, int size, nint pointer)
    {
        // A negative size is a text message; the game sends none.
        if (size < 0 || From(pointer) is not { } peer)
        {
            return;
        }

        peer._received.Enqueue(new ReadOnlySpan<byte>(message, size).ToArray());
        while (peer._received.Count > MaxQueued)
        {
            peer._received.TryDequeue(out _);
        }
    }
}

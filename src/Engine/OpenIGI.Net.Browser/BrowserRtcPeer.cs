using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Net.Browser;

/// <summary>Makes the page's WebRTC connections: the browser build's half of playing through a room.</summary>
/// <remarks>
/// The page registers rtc.js's imports under <see cref="ModuleName"/> before the game starts (<c>runtime.setModuleImports</c>),
/// so nothing here loads a module or waits for one.
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserRtcPeerFactory : IRtcPeerFactory
{
    /// <summary>The name the page registers rtc.js's imports under.</summary>
    public const string ModuleName = "openigi-rtc";

    /// <summary>Gets or sets whether connections only go through the TURN relay, never directly: for proving the relay works.</summary>
    public bool RelayOnly { get; set; }

    /// <inheritdoc/>
    public IRtcPeer Create(IReadOnlyList<IceServer> iceServers, bool offerer)
    {
        ArgumentNullException.ThrowIfNull(iceServers);
        JsonArray servers = [];
        foreach (IceServer server in iceServers)
        {
            JsonObject entry = new() { ["urls"] = new JsonArray([.. server.Urls.Select(u => (JsonNode?)JsonValue.Create(u))]) };
            if (server.Username is { } user && server.Credential is { } password)
            {
                entry["username"] = user;
                entry["credential"] = password;
            }

            servers.Add((JsonNode)entry);
        }

        return new BrowserRtcPeer(CreatePeer(servers.ToJsonString(), offerer, RelayOnly));
    }

    [JSImport("create", ModuleName)]
    private static partial int CreatePeer(string iceJson, bool offerer, bool relayOnly);
}

/// <summary>One of the page's connections, by its number in rtc.js.</summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserRtcPeer(int id) : IRtcPeer
{
    private bool _disposed;

    public RtcPeerState State => _disposed ? RtcPeerState.Closed : (RtcPeerState)PeerState(id);

    public string? Failure => PeerFailure(id);

    /// <inheritdoc/>
    public bool Relayed => !_disposed && PeerRelayed(id);

    /// <inheritdoc/>
    public int Backlog => _disposed ? 0 : PeerBacklog(id);

    public void SetRemoteDescription(string kind, string sdp) => SetRemote(id, kind, sdp);

    public void AddRemoteCandidate(string candidate, string? mid) => AddCandidate(id, candidate, mid);

    public bool TryTakeSignal(out RtcSignal signal)
    {
        if (TakeSignal(id) is { } json && JsonNode.Parse(json) is JsonObject s)
        {
            signal = new RtcSignal((string?)s["kind"] ?? string.Empty, (string?)s["value"] ?? string.Empty, (string?)s["mid"]);
            return true;
        }

        signal = default;
        return false;
    }

    public void Send(ReadOnlySpan<byte> datagram)
    {
        // The page only reads it.
        Span<byte> bytes = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(datagram), datagram.Length);
        SendDatagram(id, bytes);
    }

    public bool TryReceive(Span<byte> buffer, out int length)
    {
        length = ReceiveDatagram(id, buffer);
        if (length < 0)
        {
            length = 0;
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Close(id);
        }
    }

    [JSImport("setRemote", BrowserRtcPeerFactory.ModuleName)]
    private static partial void SetRemote(int id, string kind, string sdp);

    [JSImport("addCandidate", BrowserRtcPeerFactory.ModuleName)]
    private static partial void AddCandidate(int id, string candidate, string? mid);

    [JSImport("takeSignal", BrowserRtcPeerFactory.ModuleName)]
    private static partial string? TakeSignal(int id);

    [JSImport("state", BrowserRtcPeerFactory.ModuleName)]
    private static partial int PeerState(int id);

    [JSImport("relayed", BrowserRtcPeerFactory.ModuleName)]
    private static partial bool PeerRelayed(int id);

    [JSImport("backlog", BrowserRtcPeerFactory.ModuleName)]
    private static partial int PeerBacklog(int id);

    [JSImport("failure", BrowserRtcPeerFactory.ModuleName)]
    private static partial string? PeerFailure(int id);

    [JSImport("send", BrowserRtcPeerFactory.ModuleName)]
    private static partial void SendDatagram(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> datagram);

    [JSImport("receive", BrowserRtcPeerFactory.ModuleName)]
    private static partial int ReceiveDatagram(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> buffer);

    [JSImport("close", BrowserRtcPeerFactory.ModuleName)]
    private static partial void Close(int id);
}

using OpenIGI.Game.Multiplayer;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Net;
using OpenIGI.Net.Identity;

namespace OpenIGI.Host.Multiplayer;

/// <content>
/// The host's own recording: a watcher joined to its own server over a loopback line in this process, given everything a
/// player is sent and keeping all of it, so a hosted session is saved as a demo as a joined one is -- and plays back the same.
/// </content>
public sealed partial class HostedSession
{
    // The loopback ports the host's server answers its recorder on, and the recorder sends from: fixed, and far from any other
    // loopback line's, so the recorder's address is never a player's (an in-process host and joiner are loopback lines too).
    private const int RecorderPort = 64998;
    private const int RecorderClientPort = 64999;

    private LoopbackNetwork? _loop;
    private LoopbackTransport? _recorderLine;
    private ClientSession? _recorder;
    private float[] _noCommand = [];

    /// <summary>Gets a value indicating whether the session keeps a recording of itself (<c>sv_recorddemo</c>).</summary>
    public bool Records => _loop is not null;

    // The transport the server listens on: the one given, and beside it the recorder's loopback line when it records.
    private INetTransport WithRecorder(INetTransport transport)
    {
        if (!NetCVars.HostRecord.Value)
        {
            return transport;
        }

        _loop = new LoopbackNetwork();
        LoopbackTransport serverSide = _loop.Open(RecorderPort);
        _recorderLine = _loop.Open(RecorderClientPort);
        return new MultiTransport(transport, serverSide);
    }

    // The recorder joins once the level is up, as a watcher, with an identity of its own.
    private void StartRecorder()
    {
        if (_recorderLine is null || _recorder is not null || _world is null)
        {
            return;
        }

        IPlayerIdentity? identity = EcdsaPlayerIdentity.IsSupported ? EcdsaPlayerIdentity.CreateEphemeral() : null;
        ulong protocol = MultiplayerProtocol.IdFor(_titleId);
        _recorder = new ClientSession(new NetClient(_recorderLine, protocol, MultiplayerProtocol.Version), _world.ChannelCount, identity, protocol)
        {
            Recording = new DemoRecording(),
        };
        _noCommand = new float[_world.ChannelCount];
        _recorder.Connect(
            NetAddress.Loopback(RecorderPort),
            new JoinRequest("demo", "recorder", _options.Password, _options.Content, Watch: true),
            _clock.Elapsed.TotalSeconds);
    }

    // The recorder's turn, after the server's: what it was sent read and kept, every snapshot acknowledged with an empty
    // command, each level the server goes on to reported loaded at once (it draws none).
    private void PumpRecorder()
    {
        if (_recorder is not { } recorder)
        {
            return;
        }

        double now = _clock.Elapsed.TotalSeconds;
        recorder.Receive(now);
        if (recorder.Behind)
        {
            recorder.Arrived();
        }

        recorder.ReportReady();
        _ = recorder.SendCommand(_noCommand, now);
        recorder.Flush(now);
    }

    /// <inheritdoc/>
    public string? SaveDemo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_recorder is not { Recording: { Messages.Count: > 0 } recording } recorder)
        {
            return Records ? "nothing has been recorded yet" : "this session does not record (sv_recorddemo 0)";
        }

        string player = _options.IncludesHost ? _options.HostName : _options.Name;
        _logger.Information(
            LogCategory,
            DemoFile.Save(path, new DemoHeader(MultiplayerProtocol.Version, _titleId, recorder.Map ?? _options.Map, _mode, player), recording));
        return null;
    }
}

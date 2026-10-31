using Concentus;
using Concentus.Enums;
using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;

namespace OpenIGI.Host;

/// <summary>
/// Co-op voice: this player heard by the others while its key is held (or whenever it speaks, with the open mic), and each of
/// them heard from where it stands -- louder near, fainter far, and muffled and quieter through whatever stands between, as
/// the rain is through a roof.
/// </summary>
public sealed partial class GameHost
{
    // Opus's own rate, and a twentieth of a second of it: one packet, and one buffer a voice is played in.
    private const int VoiceRate = 48000;
    private const int VoiceFrame = VoiceRate / 50;

    // How loud the open mic must hear this player before it sends: a root mean square, of full scale.
    private const float OpenMicLevel = 0.02f;

    // A voice is near up to here and gone past there, in metres; a heard voice is said to be talking for this long after.
    private const float VoiceNearMetres = 3f;
    private const float VoiceFarMetres = 45f;
    private const int TalkingMilliseconds = 300;

    // Through a wall a voice keeps this little of its top, and this much of its loudness.
    private const float VoiceWallCutoffHz = 700f;
    private const float VoiceWallGain = 0.4f;

    // Where a voice comes from over a speaker's feet, and how far either side of it the two side rays pass. Metres.
    private const double VoiceMouthMetres = 1.6;
    private const double VoiceSideMetres = 0.9;

    private readonly Dictionary<int, HeardVoice> _heardVoices = [];
    private readonly List<short> _micPending = [];
    private readonly short[] _micRead = new short[VoiceRate / 10];
    private readonly short[] _voicePcm = new short[VoiceFrame];
    private readonly byte[] _voicePacket = new byte[SessionTalk.MaxVoiceBytes];
    private IMicrophone? _microphone;
    private IOpusEncoder? _encoder;
    private int _ownTalkingAt = int.MinValue;

    // Once a frame, before the game reads its input: this player's voice out, the others' in and placed where they stand.
    private void UpdateCoopVoice()
    {
        int now = _loop.TimeSource.Milliseconds;
        if (Talk is not { } talk || _session.Mission is null || CoopVoiceCVars.Mode.Value == 0)
        {
            _touchControls.TalkShown = false;
            EndCoopVoice();
            return;
        }

        _touchControls.TalkShown = CoopVoiceCVars.Mode.Value == 1;
        bool held = CoopVoiceCVars.Mode.Value == 1
            && !_menuFlow.MenuMode
            && _chatEntry is null
            && ((BindKeyNames.TryParse(CoopVoiceCVars.Key.Value, out KeyCode key) && _window.Input.Keyboard.IsDown(key))
                || _touchControls.Held.Contains(Presentation.Hud.Remastered.Touch.TouchButton.Talk));
        bool listening = CoopVoiceCVars.Mode.Value == 2 || held;

        // The microphone is opened the first time it is wanted -- in a browser, the press that asks is what lets it be asked.
        if (listening && _microphone is null)
        {
            _microphone = _audioOut.Device.OpenMicrophone(VoiceRate);
            _encoder ??= OpusCodecFactory.CreateEncoder(VoiceRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        }

        if (_microphone is { } mic)
        {
            for (int read; (read = mic.Read(_micRead)) > 0;)
            {
                _micPending.AddRange(_micRead.AsSpan(0, read));
            }

            while (_micPending.Count >= VoiceFrame)
            {
                _micPending.CopyTo(0, _voicePcm, 0, VoiceFrame);
                _micPending.RemoveRange(0, VoiceFrame);
                if (listening && (held || Loudness(_voicePcm) >= OpenMicLevel) && _encoder is { } encoder)
                {
                    int length = encoder.Encode(_voicePcm, VoiceFrame, _voicePacket, _voicePacket.Length);
                    if (length > 0)
                    {
                        talk.Speak(_voicePacket.AsSpan(0, length));
                        _ownTalkingAt = now;
                    }
                }
            }
        }

        foreach (VoicePacket packet in talk.TakeVoices())
        {
            if (!_heardVoices.TryGetValue(packet.Entity, out HeardVoice? heard))
            {
                LiveVoice stream = new(VoiceRate, VoiceFrame);
                heard = new HeardVoice(OpusCodecFactory.CreateDecoder(VoiceRate, 1), stream);
                heard.Voice = _audioOut.Device.PlayStream(stream, VoiceParameters.Default with { Category = SoundCategory.Speech, Priority = 250 });
                _heardVoices[packet.Entity] = heard;
            }

            int decoded = heard.Decoder.Decode(packet.Packet, _voicePcm, VoiceFrame, false);
            if (decoded > 0)
            {
                heard.Stream.Push(_voicePcm.AsSpan(0, decoded));
                heard.HeardAt = now;
            }
        }

        foreach ((int entity, HeardVoice heard) in _heardVoices)
        {
            PlaceVoice(talk, entity, heard);
        }
    }

    // A voice where its speaker stands: near or far, and through what. Three lines from the ear to the speaker's mouth --
    // straight, and passing either side of it -- and the share of them that arrive is how open the way is: all of them in the
    // clear, some round a doorway's edge, none through a wall. Open, the voice is itself; closed, it is low and quiet.
    private void PlaceVoice(ISessionTalk talk, int entity, HeardVoice heard)
    {
        if (talk.WhereIs(entity) is not { } feet)
        {
            _audioOut.Device.SetVoiceParameters(heard.Voice, VoiceParameters.Default with { Category = SoundCategory.Speech, Gain = 0f });
            return;
        }

        Vector3d mouth = (feet.ToVector() / WorldScene.UnitsPerMetre) + new Vector3d(0, 0, VoiceMouthMetres);
        Vector3d ear = _inspectorEye.Position;
        Vector3d along = mouth - ear;
        Vector3d side = new Vector3d(-along.Y, along.X, 0);
        double sideLength = Math.Sqrt((side.X * side.X) + (side.Y * side.Y));
        side = sideLength > 1e-6 ? side * (VoiceSideMetres / sideLength) : default;
        int open = 0;
        foreach (Vector3d to in (ReadOnlySpan<Vector3d>)[mouth, mouth + side, mouth - side])
        {
            if (!Blocked(ear, to))
            {
                open++;
            }
        }

        float openness = open / 3f;
        heard.Stream.CutoffHz = MathF.Exp(float.Lerp(MathF.Log(VoiceWallCutoffHz), MathF.Log(SmoothedLowPass.OpenCutoffHz), openness));
        _audioOut.Device.SetVoiceParameters(heard.Voice, VoiceParameters.Default with
        {
            Category = SoundCategory.Speech,
            Priority = 250,
            IsPositional = true,
            Position = mouth,
            MinimumDistance = VoiceNearMetres,
            MaximumDistance = VoiceFarMetres,
            Gain = CoopVoiceCVars.Volume.Value / 100f * float.Lerp(VoiceWallGain, 1f, openness),
        });
    }

    // Whether the level stands between two points, in metres.
    private bool Blocked(Vector3d fromMetres, Vector3d toMetres) =>
        _mission.Collision.EffectWalls is { } walls
        && walls(fromMetres * WorldScene.UnitsPerMetre, toMetres * WorldScene.UnitsPerMetre, out WorldRayHit hit)
        && hit.Distance < Vector3d.Distance(fromMetres, toMetres) * WorldScene.UnitsPerMetre * 0.98;

    // Who is talking now, by entity, this player's own included as -1, for the HUD to show.
    private IEnumerable<int> Talking()
    {
        int now = _loop.TimeSource.Milliseconds;
        if (now - _ownTalkingAt < TalkingMilliseconds)
        {
            yield return -1;
        }

        foreach ((int entity, HeardVoice heard) in _heardVoices)
        {
            if (now - heard.HeardAt < TalkingMilliseconds)
            {
                yield return entity;
            }
        }
    }

    private void EndCoopVoice()
    {
        _microphone?.Dispose();
        _microphone = null;
        _micPending.Clear();
        foreach (HeardVoice heard in _heardVoices.Values)
        {
            _audioOut.Device.StopVoice(heard.Voice);
        }

        _heardVoices.Clear();
    }

    private static float Loudness(ReadOnlySpan<short> samples)
    {
        double sum = 0;
        foreach (short sample in samples)
        {
            sum += (double)sample * sample;
        }

        return (float)(Math.Sqrt(sum / Math.Max(1, samples.Length)) / short.MaxValue);
    }

    // One teammate heard: its decoder, the stream it plays through, the voice playing it, and when it last said anything.
    private sealed class HeardVoice(IOpusDecoder decoder, LiveVoice stream)
    {
        public IOpusDecoder Decoder { get; } = decoder;

        public LiveVoice Stream { get; } = stream;

        public AudioVoiceHandle Voice { get; set; }

        public int HeardAt { get; set; } = int.MinValue;
    }
}

/// <summary>This player's co-op voice: how it goes out, the key held to talk, and how loud the others are.</summary>
[CVarContainer]
public static class CoopVoiceCVars
{
    /// <summary>How this player's voice goes out: off, while a key is held, or whenever it speaks.</summary>
    public static readonly NumericCVar<int> Mode = CVar.Int(
        "coop_voice", 1, 0, 2, CVarOptions.Archive, "Co-op voice: 0 off, 1 push to talk, 2 open mic.");

    /// <summary>The key held to talk, by its bind name.</summary>
    public static readonly StringCVar Key = CVar.Text(
        "coop_voice_key", "g", CVarOptions.Archive, "The key held to talk in co-op, by its bind name.");

    /// <summary>How loud the others' voices are, in percent.</summary>
    public static readonly NumericCVar<int> Volume = CVar.Int(
        "coop_voice_volume", 100, 0, 200, CVarOptions.Archive, "How loud teammates' voices are, in percent.");
}

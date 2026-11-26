using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A spoken line is said at the microphone (<c>StatusMessage_PlaySound</c> sets it relative to it, which clears each sound's
/// positional flag, <c>SoundDefSound_SetRelativeToMicrophoneHandler</c>): heard flat, never placed in the world, so a cutscene
/// camera flying with a plane cannot leave it behind the ear between ticks.
/// </summary>
public sealed class Igi2MicrophoneSoundTests
{
    [RequiresTitleDataFact("igi2")]
    public void A_line_is_said_flat_and_stays_flat_while_the_camera_flies()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Recorder audio = new();
        Igi2Sounds sounds = new(m.Sounds.Definitions, audio);

        // Mission 1's intro: White's line, heard from a camera moving at a plane's speed, metres a tick.
        Igi2StatusMessageTask line = m.Tree.OfType<Igi2StatusMessageTask>().Single(static t => t.Id == 2992);
        Vector3d camera = new(0, 0, 0);
        line.Sounds = sounds;
        line.Listener = () => camera;
        line.IsForceSend = true;

        for (int tick = 0; tick < 60; tick++)
        {
            _ = line.Step(tick, static () => false);
            sounds.Run();
            camera += new Vector3d(4096 * 3, 0, 0);
        }

        (string name, Vector3d? at) = Assert.Single(audio.Played);
        Assert.Equal("cv11_intro_01_white", name, ignoreCase: true);
        Assert.Null(at);
        Assert.Equal(0, audio.Moves);
    }

    // A mixer that records where each voice was started and counts the moves made to them.
    private sealed class Recorder : ITitleAudio
    {
        public List<(string Name, Vector3d? At)> Played { get; } = [];

        public int Moves { get; private set; }

        public void UseDefinitions(ISoundDefinitionLookup definitions, IReadOnlyList<string> bankDirectories)
        {
        }

        public ITitleVoice? Play(string definition, Vector3d? positionInUnits, bool? looping = null)
        {
            Played.Add((definition, positionInUnits));
            return new Voice(this);
        }

        private sealed class Voice(Recorder owner) : ITitleVoice
        {
            public bool IsPlaying => true;

            public void MoveTo(Vector3d positionInUnits) => owner.Moves++;

            public void Silence()
            {
            }
        }
    }
}

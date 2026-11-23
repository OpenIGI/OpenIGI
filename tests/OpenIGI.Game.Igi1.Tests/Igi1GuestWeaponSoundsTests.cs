using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.Igi1.Systems;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Project I.G.I.'s weapons sound through another game's audio where its humans stand in that game's level.</summary>
public sealed class Igi1GuestWeaponSoundsTests
{
    [Fact]
    public void A_soldiers_single_shot_plays_its_report_where_the_soldier_stands()
    {
        Recorder audio = new();
        Igi1GuestWeaponSounds sounds = new(audio, static () => default);
        WeaponType pistol = new() { FireSound = "glock", MaximumRoundsPerBurst = 1 };
        Vector3d at = new(4096, 8192, 0);

        sounds.TickSoldier(7, fired: true, pistol, at);
        sounds.TickSoldier(7, fired: false, pistol, at);

        (string name, Vector3d where, bool looping) = Assert.Single(audio.Played);
        Assert.Equal("glock", name);
        Assert.Equal(at, where);
        Assert.False(looping);
    }

    [Fact]
    public void An_automatics_loop_runs_while_it_fires_and_ends_on_its_tail()
    {
        Recorder audio = new();
        Igi1GuestWeaponSounds sounds = new(audio, static () => default);
        WeaponType rifle = new() { FireSound = "ak_loop", FireLoopEndSound = "ak_end", MaximumRoundsPerBurst = 30, RoundsPerMinute = 600 };

        sounds.TickSoldier(3, fired: true, rifle, default);
        sounds.TickSoldier(3, fired: true, rifle, default);
        Assert.Equal(("ak_loop", default(Vector3d), true), Assert.Single(audio.Played));
        Assert.Equal(0, audio.Silenced);

        for (int tick = 0; tick < 10; tick++)
        {
            sounds.TickSoldier(3, fired: false, rifle, default);
        }

        Assert.Equal(1, audio.Silenced);
        Assert.Equal("ak_end", audio.Played[^1].Name);
    }

    // The host's audio, keeping what it was asked to play.
    private sealed class Recorder : ITitleAudio
    {
        public List<(string Name, Vector3d At, bool Looping)> Played { get; } = [];

        public int Silenced { get; private set; }

        public void UseDefinitions(ISoundDefinitionLookup definitions, IReadOnlyList<string> bankDirectories)
        {
        }

        public ITitleVoice? Play(string definition, Vector3d? positionInUnits, bool? looping = null)
        {
            Played.Add((definition, positionInUnits ?? default, looping == true));
            return new Voice(this);
        }

        private sealed class Voice(Recorder owner) : ITitleVoice
        {
            public bool IsPlaying => true;

            public void MoveTo(Vector3d positionInUnits)
            {
            }

            public void Silence() => owner.Silenced++;
        }
    }
}

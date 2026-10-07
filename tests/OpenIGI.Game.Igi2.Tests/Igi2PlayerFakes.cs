using OpenIGI.Game.Igi2.Player;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Test doubles for the parts of the engine the player's states reach into.</summary>
internal static class Igi2PlayerFakes
{
    /// <summary>Builds a player with recording doubles.</summary>
    /// <param name="weapon">The weapon controls; none when omitted.</param>
    /// <returns>The player and its doubles.</returns>
    public static (Igi2HumanPlayer Player, FakeAnimation Animation, FakeSurroundings Surroundings) Player(
        IIgi2PlayerWeaponControl? weapon = null)
    {
        FakeAnimation animation = new();
        FakeSurroundings surroundings = new();
        return (new Igi2HumanPlayer(weapon ?? Igi2NoWeaponControl.Instance, animation, surroundings), animation, surroundings);
    }

    /// <summary>Feeds one frame of held actions through the player's own reader.</summary>
    /// <param name="player">The player.</param>
    /// <param name="held">The actions held this tick.</param>
    public static void Feed(Igi2HumanPlayer player, Igi2PlayerAction held)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        new Igi2PlayerInputReader().Read(new Igi2PlayerInputState(0f, 0f, held, 0), frame);
        player.Channels.Accept(frame);
    }

    /// <summary>An animation layer that records what the states ask it to play.</summary>
    internal sealed class FakeAnimation : IIgi2HumanAnimation
    {
        public List<(int State, int Time, float Blend)> Played { get; } = [];

        public int MainTime { get; set; }

        public int MainTotalTime { get; set; }

        public int MainTimeStep { get; set; }

        public bool MainEnded { get; set; }

        public bool Looping { get; private set; }

        public void LoopMain() => Looping = true;

        public void PlayState(int state, int time, float blendSeconds)
        {
            Played.Add((state, time, blendSeconds));
            Looping = false;
        }
    }

    /// <summary>Surroundings with a settable fit and a count of walk events.</summary>
    internal sealed class FakeSurroundings : IIgi2HumanSurroundings
    {
        public bool Fits { get; set; } = true;

        public int WalkEvents { get; private set; }

        public bool FitsLyingFlat() => Fits;

        public void SendWalkEvent() => WalkEvents++;
    }
}

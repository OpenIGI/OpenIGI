using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// The level's doors as a snapshot carries them: two bits each, in the tree's order -- whether it is told to stand open, and
/// whether a two-way door swings the other way -- so each client's door is told what the server's was and swings there itself.
/// </summary>
internal static class Igi2DoorCommands
{
    /// <summary>Gets the level's doors, in its tree's order.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The doors.</returns>
    public static Igi2DoorTask[] Of(Igi2TitlePlayer level) => [.. level.LevelTasks().OfType<Igi2DoorTask>()];

    /// <summary>Writes the doors' commands, on the server.</summary>
    /// <param name="doors">The level's doors (<see cref="Of"/>).</param>
    /// <returns>The bits, or <see langword="null"/> for a level without doors.</returns>
    public static byte[]? Capture(Igi2DoorTask[] doors)
    {
        if (doors.Length == 0)
        {
            return null;
        }

        byte[] bits = new byte[((doors.Length * 2) + 7) / 8];
        for (int i = 0; i < doors.Length; i++)
        {
            Set(bits, i * 2, doors[i].IsCommandedOpen);
            Set(bits, (i * 2) + 1, doors[i].SwingsBack);
        }

        return bits;
    }

    /// <summary>Reads one door's command.</summary>
    /// <param name="bits">The bits.</param>
    /// <param name="door">The door's place.</param>
    /// <returns>Whether it is told open and whether it swings back, or <see langword="null"/> past the bits' end.</returns>
    public static (bool Open, bool SwingsBack)? Read(byte[] bits, int door) =>
        ((door * 2) + 1) >> 3 < bits.Length ? (Get(bits, door * 2), Get(bits, (door * 2) + 1)) : null;

    private static void Set(byte[] bits, int bit, bool value)
    {
        if (value)
        {
            bits[bit >> 3] |= (byte)(1 << (bit & 7));
        }
    }

    private static bool Get(byte[] bits, int bit) => (bits[bit >> 3] & (1 << (bit & 7))) != 0;
}

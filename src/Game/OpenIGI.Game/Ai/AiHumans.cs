using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>A human an AI may see, fight, wait for or be told of, whichever game's human it is.</summary>
/// <remarks>
/// Both engines' AI reads the same few things of the humans it perceives: where they stand, whose side they are on, whether
/// they are dead, and where their eyes are for their stance. A brain that tests for this rather than for its own game's
/// human class fights another game's humans as it fights its own.
/// </remarks>
public interface IAiHuman
{
    /// <summary>Gets where the human stands: its feet, in world units.</summary>
    Vector3d Position { get; }

    /// <summary>Gets the side the human is on.</summary>
    int Team { get; }

    /// <summary>Gets a value indicating whether the human is dead.</summary>
    bool IsDead { get; }

    /// <summary>
    /// Gets where the human's eyes are by stance alone, as a shot it fired is traced back to (I.G.I. 2's
    /// <c>HumanView_GetOriginalViewPosition</c>).
    /// </summary>
    Vector3d OriginalViewPosition { get; }
}

/// <summary>The player, as an AI treats it apart from other humans.</summary>
public interface IAiPlayer : IAiHuman
{
    /// <summary>
    /// Gets a value indicating whether the player keeps low: settled crouching or lying flat, which hides it from a soldier
    /// past ten metres who only glimpses it.
    /// </summary>
    bool IsKeepingLow { get; }

    /// <summary>A squad has detected the player for the first time, which the player's tally counts.</summary>
    void NoteDetected();
}

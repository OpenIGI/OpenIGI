using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Host.Multiplayer;

/// <summary>A session its players talk through: lines of chat, and places marked for the others.</summary>
public interface ISessionTalk
{
    /// <summary>Says a line to every player.</summary>
    /// <param name="text">The line.</param>
    void Say(string text);

    /// <summary>Marks a place for every player.</summary>
    /// <param name="position">Where, in world units.</param>
    void Mark(Int3 position);

    /// <summary>Takes what has been said since the last call, this player's own lines included.</summary>
    /// <returns>The lines, oldest first.</returns>
    IReadOnlyList<ChatLine> TakeChat();

    /// <summary>Takes what has been marked since the last call, this player's own marks included.</summary>
    /// <returns>The marks, oldest first.</returns>
    IReadOnlyList<WorldMark> TakeMarks();

    /// <summary>Sends a moment of this player's voice to the others.</summary>
    /// <param name="packet">The encoded voice.</param>
    void Speak(ReadOnlySpan<byte> packet);

    /// <summary>Takes the others' voices heard since the last call.</summary>
    /// <returns>The moments, oldest first.</returns>
    IReadOnlyList<VoicePacket> TakeVoices();

    /// <summary>Gets where a player stands, by entity, as the session last had it.</summary>
    /// <param name="entity">The player's entity.</param>
    /// <returns>Its feet, in world units, or <see langword="null"/> when it is not in the level.</returns>
    Int3? WhereIs(int entity);
}

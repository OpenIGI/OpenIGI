using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>Plays the sounds a <c>RadioRuntime</c> asks for.</summary>
/// <remarks>
/// The world layer decides when a radio talks; the host owns the voices. A name that resolves to no sound
/// definition answers <see langword="null"/>, which is <c>sub_4E6B00</c> finding nothing: the radio carries
/// on as though it had spoken, silently.
/// </remarks>
public interface IRadioSoundPlayer
{
    /// <summary>Starts a sound at a point.</summary>
    /// <param name="sound">The authored sound name; may be empty.</param>
    /// <param name="positionUnits">Where it plays, in world units.</param>
    /// <returns>The voice, or <see langword="null"/> when nothing was started.</returns>
    object? Play(string sound, Vector3d positionUnits);

    /// <summary>Gets whether a voice <see cref="Play"/> returned is still sounding.</summary>
    /// <param name="voice">The voice.</param>
    /// <returns><see langword="true"/> while it plays.</returns>
    bool IsPlaying(object voice);

    /// <summary>Silences a voice <see cref="Play"/> returned.</summary>
    /// <param name="voice">The voice.</param>
    void Silence(object voice);
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>What a title's cries and impact sounds are played through.</summary>
public interface IMissionSoundHost
{
    /// <summary>Gets the running game, whose clock the impact sounds' quiet windows are timed on.</summary>
    GameLoop Game { get; }

    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets a value indicating whether the level's weather is on, which wets every footstep.</summary>
    bool IsRaining { get; }

    /// <summary>Gets where sounds play, or <see langword="null"/> for silence.</summary>
    /// <remarks>Silence draws nothing from the sound stream: every cry and impact returns before it picks.</remarks>
    SoundSystem? SoundOutput { get; }
}

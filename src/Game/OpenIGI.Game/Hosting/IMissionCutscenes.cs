using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Hosting;

/// <summary>A mission's cut-scenes as the host that frames them reads them: whether one plays, which, and a way to break it.</summary>
/// <remarks>
/// The original's <c>CutScene</c> task takes the camera while it runs and gives it back when its script ends or the player
/// breaks it. The host only needs to know that the camera is taken, so the HUD and the player's input stand down.
/// </remarks>
public interface IMissionCutscenes
{
    /// <summary>Gets a value indicating whether a scene is playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Gets the scene that is playing, or <see langword="null"/> when none is.</summary>
    GameTask? ActiveScene { get; }

    /// <summary>Gets how many ticks the playing scene has run, or zero when none is.</summary>
    int ActiveSceneTicks { get; }

    /// <summary>Gets a value indicating whether the playing scene listens for the player's skip.</summary>
    bool CanBreak { get; }

    /// <summary>Asks the playing scene to end at its next tick, as the player's skip does.</summary>
    void RequestBreak();
}

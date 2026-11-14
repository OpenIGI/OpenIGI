namespace OpenIGI.Game.Hosting;

/// <summary>
/// What an editor previews a mission's cutscene with on the level itself: how its scenes stand, and one scene made to play from
/// its start whatever its own scripts say.
/// </summary>
/// <remarks>
/// The level is stepped by its host, a tick at a time, as it is in play, and loaded afresh to go back: this only says how a scene
/// stands and starts one, so what the preview shows is the game's own result -- the vehicles driving, the soldiers moving, the
/// camera riding.
/// </remarks>
public interface IMissionCutscenePreview
{

    /// <summary>Gets whether a scene is the one playing.</summary>
    /// <param name="sceneId">The scene's task id.</param>
    /// <returns>Whether it runs.</returns>
    bool IsPlaying(int sceneId);

    /// <summary>Gets a scene's clock, in ticks (its <c>nTick</c>).</summary>
    /// <param name="sceneId">The scene's task id.</param>
    /// <returns>The ticks; -1 for no such scene.</returns>
    int SceneTicks(int sceneId);

    /// <summary>Gets whether a scene has played to its end.</summary>
    /// <param name="sceneId">The scene's task id.</param>
    /// <returns>Whether it is finished.</returns>
    bool IsFinished(int sceneId);

    /// <summary>
    /// Makes a scene play from its start whatever its scripts say -- the containers it stands in let in, its clock at nought --
    /// with no other scene taking the camera; or, with null, lets the level's scripts run its scenes again.
    /// </summary>
    /// <param name="sceneId">The scene's task id, or null.</param>
    /// <returns>Whether there is such a scene.</returns>
    bool Force(int? sceneId);

    /// <summary>Lets the level's scenes play rather than be skipped: no break queued, the intro not waiting to be skipped.</summary>
    /// <returns>What puts the skip back as it was, for when the preview ends.</returns>
    Action AllowScenes();
}

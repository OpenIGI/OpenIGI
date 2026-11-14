using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Saves;
using OpenIGI.Game.Saves;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// How a Project I.G.I. level's scenes stand for an editor's cutscene preview, and one scene made to play whatever its scripts say.
/// </summary>
/// <param name="game">The mission.</param>
/// <remarks>Not retail: the original has no editor.</remarks>
public sealed class Igi1CutscenePreview(Igi1Gameplay game) : IMissionCutscenePreview
{
    // The containers a forced scene stands in, let in for it.
    private readonly List<ConditionalContainerTask> _opened = [];

    private MissionWorld World => game.World;

    /// <inheritdoc/>
    public bool IsPlaying(int sceneId) => Scene(sceneId) is { } scene && ReferenceEquals(World.Cutscenes?.ActiveScene, scene);

    /// <inheritdoc/>
    public int SceneTicks(int sceneId) => Scene(sceneId)?.Ticks ?? -1;

    /// <inheritdoc/>
    public bool IsFinished(int sceneId) => Scene(sceneId)?.IsFinished == true;

    /// <inheritdoc/>
    public bool Force(int? sceneId)
    {
        if (World.Cutscenes is not { } runtime)
        {
            return false;
        }

        foreach (ConditionalContainerTask container in _opened)
        {
            container.IsForcedOpen = false;
        }

        _opened.Clear();
        runtime.Previewed = null;
        if (sceneId is not { } id)
        {
            return true;
        }

        if (Scene(id) is not { } scene)
        {
            return false;
        }

        // Every container above the scene lets it in, as the level's story would by the time it plays: its next tick opens
        // them, running what each runs as it opens.
        for (GameTask? at = scene.Parent; at is not null; at = at.Parent)
        {
            if (at is ConditionalContainerTask container)
            {
                _opened.Add(container);
                container.IsForcedOpen = true;
            }
        }

        scene.Ticks = 0;
        scene.IsFinished = false;
        runtime.Previewed = scene;
        return true;
    }

    /// <inheritdoc/>
    public Action AllowScenes()
    {
        bool skipping = World.IntroSkipPending;
        bool breaking = World.Cutscenes?.IsBreakRequested == true;
        World.IntroSkipPending = false;
        World.Cutscenes?.FinishExpressionTick();
        return () =>
        {
            World.IntroSkipPending = skipping;
            if (breaking)
            {
                World.Cutscenes?.RequestBreak();
            }
        };
    }

    private CutSceneTask? Scene(int id) => World.LevelRoot?.Descendants().OfType<CutSceneTask>().FirstOrDefault(s => s.Id == id);
}

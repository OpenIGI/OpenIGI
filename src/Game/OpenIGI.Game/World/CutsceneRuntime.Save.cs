using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Saves;

namespace OpenIGI.Game.World;

/// <content>
/// What a save game keeps of the level's walk: which containers are open, each timer's and animation's run and tick (and an
/// animation's channels, as I.G.I. 2's <c>AnimQTask</c> list keeps them), each one-off sound, each cut-scene's verdict, and where
/// everything the animations and the vehicles move has got to. Not retail: Project I.G.I. kept no save games.
/// </content>
public sealed partial class CutsceneRuntime : ISavedStateSource
{
    /// <inheritdoc/>
    /// <remarks>
    /// A vehicle's body is put back where it stood and as it was turned, with its speed; what its physics was in the middle of --
    /// its springs, its wheels -- settles again from there.
    /// </remarks>
    public IEnumerable<KeyValuePair<GameTask, ISavedState>> SavedStates(ObjectCollisionIndex? solids)
    {
        foreach (ConditionalContainerTask container in _containers)
        {
            yield return new(container, container);
        }

        foreach (CutsceneSceneState scene in _scenes)
        {
            yield return new(scene.Task, new ExchangedState(exchange =>
            {
                scene.Task.IsRun = exchange.Bool(scene.Task.IsRun);
                scene.Task.IsFinished = exchange.Bool(scene.Task.IsFinished);
                scene.Task.Ticks = exchange.Int(scene.Task.Ticks);
                scene.LastShotIndex = exchange.Int(scene.LastShotIndex);
                scene.FrameTicks = exchange.Int(scene.FrameTicks);
            }));
        }

        foreach (CutsceneTimerState timer in _timers)
        {
            yield return new(timer.Task, new ExchangedState(exchange =>
            {
                timer.IsRun = exchange.Bool(timer.IsRun);
                timer.Tick = exchange.Int(timer.Tick);
            }));
        }

        foreach (CutsceneAnimationState animation in _animations)
        {
            yield return new(animation.Task, new ExchangedState(exchange =>
            {
                animation.Run = exchange.Text(animation.Run);
                animation.InitialRun = exchange.Bool(animation.InitialRun);
                animation.IsRun = exchange.Bool(animation.IsRun);
                animation.RanThisTick = exchange.Bool(animation.RanThisTick);
                animation.Tick = exchange.Int(animation.Tick);
                float[] channels = exchange.Run(animation.Program.Channels.ToArray(), static (e, value) => e.Real(value));
                if (exchange.IsReading)
                {
                    for (int i = 0; i < channels.Length && i < AnimDataProgram.ChannelCount; i++)
                    {
                        animation.Program.WriteChannel(i, channels[i]);
                    }
                }
            }));
        }

        foreach (CutsceneSoundState sound in _sounds)
        {
            yield return new(sound.Task, new ExchangedState(exchange =>
            {
                sound.IsRun = exchange.Bool(sound.IsRun);
                sound.HasPlayed = exchange.Bool(sound.HasPlayed);
            }));
        }

        foreach ((GameTask task, CutsceneTransformState transform) in _context.TaskTransforms)
        {
            yield return new(task, transform);
        }
    }

    /// <summary>Gets what a save keeps of the walk as a whole: its clock.</summary>
    public ISaveSection SaveSection => new ExchangedSection(
        "Igi1LevelWalk",
        exchange => _context.SimulationTick = exchange.Int(_context.SimulationTick));
}

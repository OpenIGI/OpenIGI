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

        foreach (Flow.LevelTimerTask timer in _timers)
        {
            yield return new(timer, timer);
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

                // The programme's own count of its steps: without it a programme read back as never run seeks to its start
                // on its next step, and whatever it flies or drives begins its course again.
                int programTick = exchange.Int(animation.Program.Tick);
                float[] channels = exchange.Run(animation.Program.Channels.ToArray(), static (e, value) => e.Real(value));
                if (exchange.IsReading)
                {
                    animation.Program.RestoreTick(programTick);
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
                bool wasRun = sound.IsRun, hadPlayed = sound.HasPlayed;
                bool run = exchange.Bool(sound.IsRun);
                bool played = exchange.Bool(sound.HasPlayed);
                if (!exchange.IsReading)
                {
                    return;
                }

                sound.IsRun = run;
                sound.HasPlayed = played;

                // A sound that holds its voice while its condition is true -- an alarm's siren -- has its voice follow the
                // save: one sounding on the level the save is read onto and silent in the save is stopped, as its condition's
                // falling edge stops it, and one the save has sounding is started. Written straight back, the run byte would
                // show no edge to the next update, and the siren of an alarm raised since the save rang on.
                if (sound.Simple)
                {
                    return;
                }

                if (wasRun && hadPlayed && !run)
                {
                    _soundChanged?.Invoke(new CutsceneSoundEvent(
                        sound.Task, sound.Sound, sound.RelativeToMicrophone ? null : sound.Position, Playing: false, Simple: false));
                }
                else if (!wasRun && run && played && !(_held && IsSceneSound(sound)))
                {
                    _soundChanged?.Invoke(new CutsceneSoundEvent(
                        sound.Task, sound.Sound, sound.RelativeToMicrophone ? null : sound.Position, Playing: true, Simple: false));
                }
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

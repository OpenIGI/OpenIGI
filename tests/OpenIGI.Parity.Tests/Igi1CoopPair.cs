using OpenIGI.Audio;
using OpenIGI.Core.Titles;
using OpenIGI.Game.Igi1.World;
using OpenIGI.TestSupport;

namespace OpenIGI.Parity.Tests;

/// <summary>Two I.G.I. games playing one mission together (<see cref="CoopPair{TLevel}"/>).</summary>
internal sealed class Igi1CoopPair : CoopPair<Igi1Gameplay>
{
    /// <summary>Starts both games on a mission; the joiner connects once the host's level is up.</summary>
    /// <param name="mission">The mission both play.</param>
    /// <param name="frames">How many frames each may run.</param>
    /// <param name="hostArgs">More of the host's command line, such as a <c>-runat</c>.</param>
    public Igi1CoopPair(int mission = 1, int frames = 20000, params string[] hostArgs)
        : this(null, null, mission, frames, hostArgs)
    {
    }

    /// <summary>
    /// Starts both games on a mission, each heard and listening through the audio it is given; given audio, the games talk
    /// through the session themselves -- what is said and spoken is theirs to take, not the test's.
    /// </summary>
    /// <param name="hostAudio">The host's audio.</param>
    /// <param name="joinerAudio">The joiner's audio.</param>
    /// <param name="mission">The mission both play.</param>
    /// <param name="frames">How many frames each may run.</param>
    /// <param name="hostArgs">More of the host's command line.</param>
    public Igi1CoopPair(IAudioDeviceFactory? hostAudio, IAudioDeviceFactory? joinerAudio, int mission = 1, int frames = 20000, params string[] hostArgs)
        : base(GameDataSource.Igi1, GameTitle.Igi1, "igi1", hostAudio, joinerAudio, mission, frames, hostArgs, joinerArgs: [], joinLater: false)
    {
    }

    /// <summary>
    /// Starts the host on a mission, and the joiner either alongside or not until <see cref="CoopPair{TLevel}.StartJoiner"/>: a
    /// late joiner, coming to a level the host has played into. The host skips its intro; the joiner plays with no extras but its own.
    /// </summary>
    /// <param name="hostArgs">More of the host's command line.</param>
    /// <param name="joinerArgs">The joiner's extras, in place of the host's.</param>
    /// <param name="joinLater">Whether the joiner waits.</param>
    /// <param name="mission">The mission both play.</param>
    /// <param name="hostSkipsIntro">Whether the host skips its intro (the joiner alongside skips its own with it).</param>
    public Igi1CoopPair(string[] hostArgs, string[] joinerArgs, bool joinLater, int mission = 1, bool hostSkipsIntro = true)
        : base(GameDataSource.Igi1, GameTitle.Igi1, "igi1", null, null, mission, 20000, hostArgs, joinerArgs, joinLater, hostSkipsIntro)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Igi1CoopPair"/> class playing a mode other than co-op.</summary>
    /// <param name="mode">The mode's id, as <c>tdm</c>.</param>
    /// <param name="mission">The mission both play.</param>
    /// <param name="joinerMode">The mode the joiner believes it is joining, as a stale listing would say; the host's by default.</param>
    /// <param name="hostSkipsIntro">Whether the host skips the level's intro, as a capture does; a player's game plays it.</param>
    /// <param name="dedicated">Whether the server plays no part itself, as a dedicated server: its mission's player is nobody's.</param>
    public Igi1CoopPair(string mode, int mission, string? joinerMode = null, bool hostSkipsIntro = true, bool dedicated = false)
        : base(GameDataSource.Igi1, GameTitle.Igi1, "igi1", null, null, mission, 20000, [], [], joinLater: false, hostSkipsIntro, mode, joinerMode, dedicated)
    {
    }

    /// <summary>
    /// Finds a way ahead from a spot along which a round flies five metres clear: nothing solid crosses it, the ground is under
    /// its end, and nothing stands between an eye there and a target anywhere up to <paramref name="sweepMetres"/> to either
    /// side of that end -- or a round aimed at a target running across stops on a wall beside the shooter instead.
    /// </summary>
    /// <param name="feet">The shooter's feet.</param>
    /// <param name="sweepMetres">How far either side the target runs; nought for a standing one.</param>
    /// <returns>The way, flat; <see langword="null"/> when there is none.</returns>
    public Core.Numerics.Vector3d? ClearAhead(Core.Numerics.Vector3d feet, double sweepMetres)
    {
        if (Host?.World.Solids?.Caster() is not { } cast)
        {
            return null;
        }

        Core.Numerics.Vector3d from = feet + new Core.Numerics.Vector3d(0, 0, 1.6 * 4096);
        bool Clear(Core.Numerics.Vector3d along)
        {
            if (cast(from, from + (along * 7 * 4096), out _)
                || cast(from + (along * 5 * 4096), feet + (along * 5 * 4096) + new Core.Numerics.Vector3d(0, 0, 256), out _))
            {
                return false;
            }

            Core.Numerics.Vector3d across = new(along.Y, -along.X, 0);
            for (double metres = -sweepMetres; sweepMetres > 0 && metres <= sweepMetres; metres += sweepMetres / 4)
            {
                if (cast(from, feet + (along * 5 * 4096) + (across * metres * 4096) + new Core.Numerics.Vector3d(0, 0, 1.3 * 4096), out _))
                {
                    return false;
                }
            }

            return true;
        }

        for (int i = 0; i < 16; i++)
        {
            Core.Numerics.Vector3d along = new(Math.Sin(i * Math.PI / 8), Math.Cos(i * Math.PI / 8), 0);
            if (Clear(along))
            {
                return along;
            }
        }

        return null;
    }
}

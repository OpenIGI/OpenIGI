using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Audio;

/// <summary>The level's sounds: the ones <c>SoundSys_NewSoundFn</c> (<c>igi2 0x42F6F0</c>) makes, each following what it hangs off.</summary>
/// <remarks>
/// <para>
/// Whether a sound is made is the definitions' answer alone: there is one for every name they define, whatever the
/// mixer does with it, so the game decides the same whether or not anything is heard. Retail's own answer differs only
/// when it has no sound at all (<c>SoundSys_isSoundDisabled</c>), which a played game never is.
/// </para>
/// <para>
/// A sound task asks its parent where it is every tick (<c>Attach_eAttachEvent</c>); a sound here is told by the
/// position it was made to follow, once a tick in <see cref="Run"/>.
/// </para>
/// </remarks>
/// <param name="definitions">The level's definitions.</param>
/// <param name="audio">The mixer, or <see langword="null"/> where nothing is heard.</param>
public sealed class Igi2Sounds(Igi2SoundDefinitions definitions, ITitleAudio? audio)
{
    private readonly List<Igi2Sound> _playing = [];

    /// <summary>Gets the level's definitions.</summary>
    public Igi2SoundDefinitions Definitions { get; } = definitions ?? throw new ArgumentNullException(nameof(definitions));

    /// <summary>Gets the sounds made and not yet ended or deleted.</summary>
    public IReadOnlyList<Igi2Sound> Playing => _playing;

    /// <summary>Makes a sound, <c>SoundSys_NewSoundFn</c> then <c>SoundSys_eSetPositionQTaskEvent</c>.</summary>
    /// <param name="name">The definition's name.</param>
    /// <param name="position">Where it starts.</param>
    /// <param name="follow">Where it is each tick after, or <see langword="null"/> to stay put.</param>
    /// <param name="looping">Whether it loops, or <see langword="null"/> for as its definition says.</param>
    /// <returns>The sound, or <see langword="null"/> for an empty name or one nothing defines.</returns>
    public Igi2Sound? New(string name, Vector3d position, Func<Vector3d>? follow = null, bool? looping = null)
    {
        if (!Definitions.TryResolve(name, out _))
        {
            return null;
        }

        Igi2Sound sound = new(this, name, audio?.Play(name, position, looping), follow);
        _playing.Add(sound);
        return sound;
    }

    /// <summary>Runs the sounds one tick: each follows what it hangs off, and counts the time it has played.</summary>
    /// <remarks>One ended or deleted is let go.</remarks>
    public void Run()
    {
        for (int i = _playing.Count - 1; i >= 0; i--)
        {
            Igi2Sound sound = _playing[i];
            if (!sound.IsAlive)
            {
                _playing.RemoveAt(i);
                continue;
            }

            sound.Tick();
        }
    }

    /// <summary>Lets go of a deleted sound.</summary>
    internal void Forget(Igi2Sound sound) => _playing.Remove(sound);
}

/// <summary>One sound the level made: its voice, and what it follows.</summary>
public sealed class Igi2Sound : IIgi2SoundInstance
{
    /// <summary>The length of a tick, in seconds.</summary>
    private const float TickSeconds = 1f / 30f;

    private readonly Igi2Sounds _owner;
    private readonly ITitleVoice? _voice;
    private readonly Func<Vector3d>? _follow;
    private bool _deleted;

    internal Igi2Sound(Igi2Sounds owner, string name, ITitleVoice? voice, Func<Vector3d>? follow)
    {
        _owner = owner;
        Name = name;
        _voice = voice;
        _follow = follow;
    }

    /// <summary>Gets the definition's name.</summary>
    public string Name { get; }

    /// <summary>Gets how long it has played, in seconds.</summary>
    public float Seconds { get; private set; }

    /// <inheritdoc/>
    /// <remarks>Until it is deleted, and while the mixer still plays it: one never heard has nothing to end but its deletion.</remarks>
    public bool IsAlive => !_deleted && _voice is { IsPlaying: true };

    /// <inheritdoc/>
    public void MoveToHuman()
    {
        if (_follow is not null)
        {
            _voice?.MoveTo(_follow());
        }
    }

    /// <inheritdoc/>
    public void Delete()
    {
        if (_deleted)
        {
            return;
        }

        _deleted = true;
        _voice?.Silence();
        _owner.Forget(this);
    }

    /// <summary>One tick: it follows, and its time goes on.</summary>
    internal void Tick()
    {
        MoveToHuman();
        Seconds += TickSeconds;
    }
}

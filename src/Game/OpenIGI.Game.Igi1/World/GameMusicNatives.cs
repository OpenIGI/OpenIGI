using OpenIGI.Engine.Audio;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The three script natives that control the score.
/// </summary>
/// <remarks>
/// <para>
/// <c>Game_EnableMusic</c>, <c>Game_DisableMusic</c> and <c>Game_SetMusicVolume</c>. The options menu's
/// music slider previews live through the third — its script is literally
/// <c>Game_SetMusicVolume(this.vValue)</c> — so without these registered, dragging that slider does
/// nothing until the profile is saved and re-read.
/// </para>
/// <para>
/// Registered against a <see cref="MusicPlayer"/> rather than owning any state themselves, so the menu
/// and the mission agree about what is playing.
/// </para>
/// </remarks>
public sealed class GameMusicNatives
{
    /// <summary>The native that turns the score on.</summary>
    public const string EnableNative = "Game_EnableMusic";

    /// <summary>The native that turns the score off.</summary>
    public const string DisableNative = "Game_DisableMusic";

    /// <summary>The native that sets the score's volume.</summary>
    public const string SetVolumeNative = "Game_SetMusicVolume";

    private readonly MusicPlayer _music;

    /// <summary>Initializes a new instance of the <see cref="GameMusicNatives"/> class.</summary>
    /// <param name="music">The player the natives drive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="music"/> is <see langword="null"/>.</exception>
    public GameMusicNatives(MusicPlayer music)
    {
        ArgumentNullException.ThrowIfNull(music);
        _music = music;
    }

    /// <summary>Serves the three natives to the menu's own expression evaluator.</summary>
    /// <param name="name">The native being called.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The result, or <see langword="null"/> when the name is not one of these.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A menu expression is evaluated by the menu's evaluator rather than by the script VM, so the
    /// registration below does not reach it — the same second door <c>Game_SetMission</c> needs.
    /// </remarks>
    public QvmValue? TryInvokeFromMenu(string name, ScriptArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        switch (name)
        {
            case EnableNative:
                _music.Enabled = true;
                return QvmValue.FromInt32(1);

            case DisableNative:
                _music.Enabled = false;
                return QvmValue.FromInt32(1);

            case SetVolumeNative:
                _music.Volume = (float)arguments.GetReal(0);
                return QvmValue.FromInt32(1);

            default:
                return null;
        }
    }

    /// <summary>Registers all three natives.</summary>
    /// <param name="natives">The script namespace the scripts resolve against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        natives.RegisterFunction(EnableNative, _ => { _music.Enabled = true; return QvmValue.FromInt32(1); }, 0);
        natives.RegisterFunction(DisableNative, _ => { _music.Enabled = false; return QvmValue.FromInt32(1); }, 0);
        natives.RegisterFunction(
            SetVolumeNative,
            call =>
            {
                _music.Volume = call.GetSingle(0);
                return QvmValue.FromInt32(1);
            },
            1);
    }
}

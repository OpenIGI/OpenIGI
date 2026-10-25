using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>Lines the game itself says to the player, beside the level's own: a weapon picked up, ammunition taken.</summary>
public sealed partial class Igi2TitlePlayer
{
    /// <summary>
    /// Says a line to the player, <c>StatusMessage_New</c> (<c>igi2 0x50FEB0</c>): a status message made at run time under the
    /// level's dynamic task, forced on the screen and shown once, then gone.
    /// </summary>
    /// <param name="key">The line's text: a key looked up in the language.</param>
    /// <param name="sprite">The picture shown with it, or none.</param>
    /// <param name="sound">The sound it plays, or none.</param>
    /// <remarks>
    /// Shown by the level flow's clock in a level of I.G.I. 2's own; by the player's where it stands in another game's level,
    /// whose tick also runs the screen it prints on.
    /// </remarks>
    public void ShowStatusMessage(string key, string sprite = "", string sound = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Igi2StatusMessageTask message = new()
        {
            Text = key,
            Sprite = sprite,
            Sound = sound,
            IsLocalized = true,
            IsForceSend = true,
            IsSendOnce = true,
            Strings = Strings,
            Screen = StatusMessages,
            Sounds = Sounds,
            Listener = () => Human.View.Position,
        };
        message.InitText();
        (Level?.Dynamic ?? Dynamic).Add(new SaidLine(message, () => Level?.LevelFlow?.Tick ?? (int)_ticks));
    }

    // A line said at run time, run among the level's dynamic tasks until it has been shown.
    private sealed class SaidLine(Igi2StatusMessageTask message, Func<int> clock) : IIgi2DynamicTask
    {
        public bool Run() => message.Step(clock(), static () => false) != StatusMessageStep.Done;
    }
}

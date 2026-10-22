using OpenIGI.Core.Titles;
using OpenIGI.Presentation;

namespace OpenIGI.Modding.Presentation;

/// <summary>What a mod draws: presenters of its own beside the game's, said in its <see cref="IOpenIgiMod.Configure"/>.</summary>
public static class PresentationMods
{
    // Where a mod's builder keeps what it draws.
    private const string Key = "OpenIGI.Modding.Presentation.Presenters";

    extension(IModBuilder mod)
    {
        /// <summary>Adds to the game's presenters: a task type drawn, the level drawn over, a HUD overlay laid over the view.</summary>
        /// <param name="configure">What the mod adds, run once the game's own presenters are registered.</param>
        /// <returns>The builder.</returns>
        /// <remarks>
        /// <see cref="PresenterRegistry.ForTask{TTask}"/> draws a task type, <see cref="PresenterRegistry.ForLevel"/> draws over
        /// the level, and <see cref="PresenterRegistry.ForHud"/> lays an overlay over the player's view after the game's HUD.
        /// A build that draws nothing never runs it.
        /// </remarks>
        public IModBuilder Presenters(Action<PresenterRegistry> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            if (!mod.Properties.TryGetValue(Key, out object? kept) || kept is not List<Action<PresenterRegistry>> list)
            {
                list = [];
                mod.Properties[Key] = list;
            }

            list.Add(configure);
            return mod;
        }
    }

    /// <summary>Adds what every mod for a game draws to its presenters, in the order the mods were loaded.</summary>
    /// <param name="mods">The mods, already configured into the game (<see cref="ModSet.Extend"/>).</param>
    /// <param name="title">The game.</param>
    /// <param name="presenters">The game's presenters.</param>
    /// <returns>How many mods added to them.</returns>
    public static int Apply(ModSet mods, GameTitle title, PresenterRegistry presenters)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(presenters);
        int applied = 0;
        foreach (InstalledMod mod in mods.For(title))
        {
            if (mods.PropertiesOf(mod, title).TryGetValue(Key, out object? kept) && kept is List<Action<PresenterRegistry>> draws)
            {
                foreach (Action<PresenterRegistry> draw in draws)
                {
                    draw(presenters);
                }

                applied++;
            }
        }

        return applied;
    }
}

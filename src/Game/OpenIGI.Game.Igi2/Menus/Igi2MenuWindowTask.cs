using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Menus;

/// <summary>
/// A window of I.G.I. 2's menu system, <c>MenuWindow</c> (menuwindow.c): the base every menu task derives from, which hands its data
/// events on to its children. A screen being opened takes its data in (<c>MenuWindow_eInitDataEvent</c>); one being accepted gives it
/// back (<c>MenuWindow_eChangeDataEvent</c>).
/// </summary>
/// <remarks>
/// Layout, drawing, fonts and input (<c>MenuWindow_eFormatEvent</c>, the draw and run handlers) are the presentation's: the OpenIGI
/// shell presents the in-game menu, so only the menu's data and its state are kept here.
/// </remarks>
public abstract class Igi2MenuWindowTask : Igi2Task
{
    /// <summary>Takes the window's data in, as it is opened: <c>MenuWindow_InitDataHandler</c>, its children in turn.</summary>
    /// <param name="level">The level the window's scripts are evaluated in.</param>
    public virtual void InitData(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        foreach (Igi2MenuWindowTask child in Children.OfType<Igi2MenuWindowTask>())
        {
            child.InitData(level);
        }
    }

    /// <summary>Gives the window's data back, as it is accepted: <c>MenuWindow_ChangeDataHandler</c>, its children in turn.</summary>
    /// <param name="level">The level the window's scripts are evaluated in.</param>
    public virtual void ChangeData(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        foreach (Igi2MenuWindowTask child in Children.OfType<Igi2MenuWindowTask>())
        {
            child.ChangeData(level);
        }
    }

    /// <summary>
    /// Runs one of the window's scripts, <c>Level_RunLevelExpression</c>: compiled the first time against the window's name, so a
    /// script's <c>this.</c> names the window.
    /// </summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <param name="compiled">The compiled script, kept between runs.</param>
    /// <param name="source">The script's text.</param>
    /// <param name="value">Receives its value.</param>
    /// <returns><see langword="false"/> when there is no script to run.</returns>
    protected bool TryRun(Igi2LevelRun level, ref LevelExpression? compiled, string source, out double value)
    {
        ArgumentNullException.ThrowIfNull(level);
        compiled ??= Igi2LevelExpressions.Create(source, Name, level.Natives);
        if (compiled.IsEmpty)
        {
            value = 0.0;
            return false;
        }

        value = level.Evaluate(compiled);
        return true;
    }
}

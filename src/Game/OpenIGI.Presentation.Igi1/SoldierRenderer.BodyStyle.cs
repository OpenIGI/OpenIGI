using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Presentation.Igi1;

/// <content>
/// A mod's own way of drawing the soldiers (<see cref="ISoldierBodyStyle"/>): the bodies it makes, ticked, posed, told of a
/// fall and let go of. With no style set none of this runs. <b>Not retail.</b>
/// </content>
public sealed partial class SoldierRenderer
{
    private ISoldierBodyStyle? _bodyStyle;
    private readonly List<ISoldierBody> _styledBodies = [];

    /// <summary>
    /// Gets or sets a mod's own way of drawing the soldiers, or <see langword="null"/> to draw them as the game does. Setting it
    /// lets go of every body the last style made; the new one is asked for each soldier on the next tick. <b>Not retail.</b>
    /// </summary>
    public ISoldierBodyStyle? BodyStyle
    {
        get => _bodyStyle;
        set
        {
            if (ReferenceEquals(value, _bodyStyle))
            {
                return;
            }

            foreach (SoldierGroup group in _groups)
            {
                foreach (SoldierMember member in group.Soldiers)
                {
                    DropStyledBody(member);
                    member.StyleFailed = false;
                    member.StyleDeclined = false;
                }
            }

            _bodyStyle = value;
        }
    }

    // A tick of a soldier's styled body, made for him first if the style will; once the game has posed his own model. A
    // soldier the style declined, or whose body failed, is drawn as the game draws him until the style is set again.
    private void TickStyledBody(SoldierGroup group, SoldierMember member)
    {
        if (_bodyStyle is not { } style || member.StyleDeclined)
        {
            return;
        }

        if (member.StyleFailed)
        {
            DropStyledBody(member);
            return;
        }

        if (member.Styled is null)
        {
            if (!RunStyled(
                _logger,
                member,
                "A mod's soldier body style's Create",
                (style, Soldier: new SoldierBodySoldier(group, member, _sim, _device)),
                static made => made.style.Create(made.Soldier),
                out ISoldierBody? body))
            {
                return;
            }

            if (body is null)
            {
                member.StyleDeclined = true;
                return;
            }

            member.Styled = body;
            _styledBodies.Add(body);
        }

        if (member.Styled is { } styled && !RunStyled(_logger, member, "A mod's soldier body's Tick", styled, static body => body.Tick()))
        {
            DropStyledBody(member);
        }
    }

    // The style poses its bodies for the frame, before any is drawn: between ticks when frames are smoothed, on the tick when not.
    private void FrameStyledBodies(float tickFraction, bool smooth)
    {
        if (_bodyStyle is { } style && _styledBodies.Count > 0)
        {
            Contained.Run(
                _logger,
                LogCategory,
                "A mod's soldier body style's Frame",
                (style, _styledBodies, Alpha: smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1f, _drawer.LastEye),
                static frame => frame.style.Frame(frame._styledBodies, frame.Alpha, frame.LastEye));
        }
    }

    private void DropStyledBody(SoldierMember member)
    {
        if (member.Styled is { } body)
        {
            _styledBodies.Remove(body);
            member.Styled = null;
            Contained.Run(_logger, LogCategory, "A mod's soldier body's Dispose", body, static dropped => dropped.Dispose());
        }
    }

    /// <summary>Runs a call of a mod's soldier body; one that throws is logged, and the body is let go of on the next tick.</summary>
    /// <typeparam name="TState">What the call needs.</typeparam>
    /// <param name="logger">Where the failure is logged.</param>
    /// <param name="member">The soldier whose body it is.</param>
    /// <param name="what">The call, as the log names it.</param>
    /// <param name="state">What the call needs.</param>
    /// <param name="call">The call.</param>
    /// <returns>Whether it ran without throwing.</returns>
    internal static bool RunStyled<TState>(IOpenIgiLogger logger, SoldierMember member, string what, TState state, Action<TState> call)
    {
        if (Contained.Run(logger, LogCategory, what, state, call))
        {
            return true;
        }

        member.StyleFailed = true;
        return false;
    }

    /// <summary>Runs a call of a mod's soldier body for a result; one that throws is logged, and the body is let go of on the next tick.</summary>
    /// <typeparam name="TState">What the call needs.</typeparam>
    /// <typeparam name="TResult">What it gives.</typeparam>
    /// <param name="logger">Where the failure is logged.</param>
    /// <param name="member">The soldier whose body it is.</param>
    /// <param name="what">The call, as the log names it.</param>
    /// <param name="state">What the call needs.</param>
    /// <param name="call">The call.</param>
    /// <param name="result">What it gave, or the default when it threw.</param>
    /// <returns>Whether it ran without throwing.</returns>
    internal static bool RunStyled<TState, TResult>(
        IOpenIgiLogger logger, SoldierMember member, string what, TState state, Func<TState, TResult> call, out TResult result)
    {
        if (Contained.Run(logger, LogCategory, what, state, call, out result))
        {
            return true;
        }

        member.StyleFailed = true;
        return false;
    }
}

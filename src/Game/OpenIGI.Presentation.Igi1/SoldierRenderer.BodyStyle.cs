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
                }
            }

            _bodyStyle = value;
        }
    }

    // A tick of a soldier's styled body, made for him first if the style will; once the game has posed his own model.
    private void TickStyledBody(SoldierGroup group, SoldierMember member)
    {
        if (_bodyStyle is not { } style)
        {
            return;
        }

        // A body that failed is let go of, and the soldier is drawn as the game draws him until the style is set again.
        if (member.StyleFailed)
        {
            DropStyledBody(member);
            return;
        }

        if (member.Styled is null)
        {
            ISoldierBody? made = null;
            if (!TryMod(_logger, "Create", () => made = style.Create(new SoldierBodySoldier(group, member, _sim, _device))))
            {
                member.StyleFailed = true;
                return;
            }

            if (made is not null)
            {
                member.Styled = made;
                _styledBodies.Add(made);
            }
        }

        if (member.Styled is { } body && !TryMod(_logger, "Tick", body.Tick))
        {
            member.StyleFailed = true;
            DropStyledBody(member);
        }
    }

    // The style poses its bodies for the frame, before any is drawn: between ticks when frames are smoothed, on the tick when not.
    private void FrameStyledBodies(float tickFraction, bool smooth)
    {
        if (_bodyStyle is { } style && _styledBodies.Count > 0)
        {
            TryMod(_logger, "Frame", () => style.Frame(_styledBodies, smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1f, _drawer.LastEye));
        }
    }

    private void DropStyledBody(SoldierMember member)
    {
        if (member.Styled is { } body)
        {
            _styledBodies.Remove(body);
            member.Styled = null;
            TryMod(_logger, "Dispose", body.Dispose);
        }
    }

    /// <summary>Runs a mod body's code; its failure is its own, so it is logged and the game goes on.</summary>
    /// <param name="logger">Where the failure is logged.</param>
    /// <param name="step">Which of the body's calls it is, for the log.</param>
    /// <param name="call">The call.</param>
    /// <returns>Whether it ran without throwing.</returns>
    internal static bool TryMod(IOpenIgiLogger logger, string step, Action call)
    {
        try
        {
            call();
            return true;
        }
#pragma warning disable CA1031 // A mod's failure is its own; the game and the other soldiers go on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.Error(LogCategory, $"A mod's soldier body failed in {step}: {ex.Message}");
            return false;
        }
    }
}

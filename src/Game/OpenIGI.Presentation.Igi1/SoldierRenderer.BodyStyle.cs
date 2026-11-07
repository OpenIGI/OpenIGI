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

        if (member.Styled is null && style.Create(new SoldierBodySoldier(group, member, _sim, _device)) is { } made)
        {
            member.Styled = made;
            _styledBodies.Add(made);
        }

        member.Styled?.Tick();
    }

    // The style poses its bodies for the frame, before any is drawn: between ticks when frames are smoothed, on the tick when not.
    private void FrameStyledBodies(float tickFraction, bool smooth)
    {
        if (_bodyStyle is { } style && _styledBodies.Count > 0)
        {
            style.Frame(_styledBodies, smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1f, _drawer.LastEye);
        }
    }

    private void DropStyledBody(SoldierMember member)
    {
        if (member.Styled is { } body)
        {
            _styledBodies.Remove(body);
            member.Styled = null;
            body.Dispose();
        }
    }
}

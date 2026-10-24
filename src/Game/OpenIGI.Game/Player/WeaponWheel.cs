namespace OpenIGI.Game.Player;

/// <summary>
/// The remaster's weapon wheel: held open, the mouse steers a pointer round a ring of the weapons carried, and letting go takes
/// the one pointed at. <b>Not retail</b>: neither game has one; each switches weapons by its keys alone.
/// </summary>
/// <remarks>
/// <para>
/// The host owns the input: it opens the wheel on its key, steers it with the mouse while it is open, and closes it when the key
/// is let go, handing the pick to the player (<see cref="IWeaponWheelPlayer.ChooseFromWheel"/>). The player takes it on its own
/// next tick, as it would a weapon key. A title's display draws the wheel from <see cref="Openness"/>, <see cref="Count"/> and
/// <see cref="Hovered"/>.
/// </para>
/// <para>
/// The ring's first sector is straight up and the rest follow clockwise. The pointer is the mouse's travel since opening, held
/// within <see cref="Reach"/>; inside <see cref="DeadZone"/> it points at nothing new, so the wheel opens on the weapon in hand
/// and a twitch does not move it.
/// </para>
/// </remarks>
public sealed class WeaponWheel
{
    /// <summary>How far the pointer travels before it points at a sector, in mouse counts.</summary>
    public const float DeadZone = 40f;

    /// <summary>How far the pointer can travel from the middle, in mouse counts: further travel turns it without lengthening it.</summary>
    public const float Reach = 160f;

    /// <summary>How long the wheel takes to open or close, in seconds of real time.</summary>
    public const float OpenSeconds = 0.16f;

    private float _pointerX;
    private float _pointerY;

    /// <summary>Gets a value indicating whether the wheel is held open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets how far the wheel has opened, nought (shut) to one (open), eased in real time.</summary>
    public float Openness { get; private set; }

    /// <summary>Gets how many sectors the ring has: the choices the player offered when it opened.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the sector pointed at, or -1 for none.</summary>
    public int Hovered { get; private set; } = -1;

    /// <summary>Gets the sector of the weapon in hand when the wheel opened, or -1 for none.</summary>
    public int Current { get; private set; } = -1;

    /// <summary>Gets the way the pointer points, in radians clockwise from straight up, or <see langword="null"/> inside the dead zone.</summary>
    public float? PointerAngle =>
        (_pointerX * _pointerX) + (_pointerY * _pointerY) >= DeadZone * DeadZone ? MathF.Atan2(_pointerX, -_pointerY) : null;

    /// <summary>Opens the wheel on a ring of choices.</summary>
    /// <param name="count">How many choices there are.</param>
    /// <param name="current">The choice in hand, which it opens pointing at, or -1.</param>
    public void Open(int count, int current)
    {
        IsOpen = true;
        Count = Math.Max(0, count);
        Current = current >= 0 && current < Count ? current : -1;
        Hovered = Current;
        _pointerX = 0f;
        _pointerY = 0f;
    }

    /// <summary>Moves the pointer by the mouse's travel.</summary>
    /// <param name="dx">The travel across, right positive.</param>
    /// <param name="dy">The travel down, down positive.</param>
    /// <returns>Whether the sector pointed at changed.</returns>
    public bool Steer(float dx, float dy)
    {
        if (!IsOpen || Count == 0)
        {
            return false;
        }

        _pointerX += dx;
        _pointerY += dy;
        float length = MathF.Sqrt((_pointerX * _pointerX) + (_pointerY * _pointerY));
        if (length > Reach)
        {
            _pointerX *= Reach / length;
            _pointerY *= Reach / length;
        }

        if (PointerAngle is not { } angle)
        {
            return false;
        }

        int sector = SectorAt(angle, Count);
        if (sector == Hovered)
        {
            return false;
        }

        Hovered = sector;
        return true;
    }

    /// <summary>Closes the wheel.</summary>
    /// <returns>The sector chosen, or -1 when it closed on nothing or on the weapon already in hand.</returns>
    public int Close()
    {
        if (!IsOpen)
        {
            return -1;
        }

        IsOpen = false;
        return Hovered >= 0 && Hovered != Current ? Hovered : -1;
    }

    /// <summary>Opens or shuts the wheel a step towards where it is held.</summary>
    /// <param name="seconds">The real time since the last step.</param>
    public void Animate(float seconds) =>
        Openness = Math.Clamp(Openness + ((IsOpen ? 1f : -1f) * Math.Max(0f, seconds) / OpenSeconds), 0f, 1f);

    /// <summary>Shuts the wheel at once, choosing nothing: for when the player can no longer use it.</summary>
    public void Cancel()
    {
        IsOpen = false;
        Openness = 0f;
        Hovered = -1;
    }

    /// <summary>The sector of a ring a way points into.</summary>
    /// <param name="angle">The way, in radians clockwise from straight up.</param>
    /// <param name="count">How many sectors the ring has.</param>
    /// <returns>The sector, the first centred straight up.</returns>
    public static int SectorAt(float angle, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        float step = MathF.Tau / count;
        float turned = (angle + (step * 0.5f)) % MathF.Tau;
        if (turned < 0f)
        {
            turned += MathF.Tau;
        }

        return Math.Min(count - 1, (int)(turned / step));
    }
}

/// <summary>A player whose weapons the remaster's wheel can choose between.</summary>
public interface IWeaponWheelPlayer
{
    /// <summary>Gets the player's wheel, which the host steers and the display draws.</summary>
    WeaponWheel Wheel { get; }

    /// <summary>The weapons the wheel offers now, in its order, and which is in hand.</summary>
    /// <returns>Each weapon's own identity in the title's terms, and the index of the one in hand or -1.</returns>
    (IReadOnlyList<string> Choices, int Current) WheelChoices();

    /// <summary>Takes a weapon from the wheel: the player switches to it on its next tick, as it would on its key.</summary>
    /// <param name="choice">The weapon's identity, as <see cref="WheelChoices"/> named it.</param>
    void ChooseFromWheel(string choice);
}

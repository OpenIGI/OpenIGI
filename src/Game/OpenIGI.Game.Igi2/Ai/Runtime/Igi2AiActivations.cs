using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// What soldiers are on their way to use, <c>HumanAI_tActivationList</c>: an entry per thing, so that no two soldiers make for the
/// same alarm or gun.
/// </summary>
public sealed class Igi2AiActivations
{
    private readonly List<(object Thing, Igi2HumanAi Holder)> _entries = [];

    /// <summary>Whether a thing is marked in use, <c>HumanAI_GetActivationStatus</c>.</summary>
    /// <param name="thing">The thing.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsInUse(object thing) => _entries.Exists(e => ReferenceEquals(e.Thing, thing));

    /// <summary>Marks a thing in use, <c>HumanAI_SetActivationStatus</c> on.</summary>
    /// <param name="thing">The thing.</param>
    /// <param name="holder">The soldier using it.</param>
    /// <returns>The entry, to let go of.</returns>
    public object Mark(object thing, Igi2HumanAi holder)
    {
        _entries.Add((thing, holder));
        return thing;
    }

    /// <summary>Lets go of the first entry for a thing, <c>HumanAI_ClearActivationEntry</c>.</summary>
    /// <param name="entry">The entry.</param>
    public void Release(object entry)
    {
        int at = _entries.FindIndex(e => ReferenceEquals(e.Thing, entry));
        if (at >= 0)
        {
            _entries.RemoveAt(at);
        }
    }

    /// <summary>Lets go of everything one soldier held.</summary>
    /// <param name="holder">The soldier.</param>
    public void Release(Igi2HumanAi holder) => _entries.RemoveAll(e => ReferenceEquals(e.Holder, holder));

    /// <summary>Forgets every entry, as a level's end does.</summary>
    public void Clear() => _entries.Clear();
}

/// <summary>The distance the AI measures, as its many inline copies do.</summary>
public static class Igi2AiMath
{
    /// <summary>The distance between two points, the first two differences stored as floats and the third left in the register.</summary>
    /// <param name="a">One point.</param>
    /// <param name="b">The other.</param>
    /// <returns>The distance.</returns>
    public static float Distance(Vector3d a, Vector3d b)
    {
        float dx = ((Fpu)a.X - b.X).F, dy = ((Fpu)a.Y - b.Y).F;
        Fpu dz = (Fpu)a.Z - b.Z;
        return Fpu.Sqrt((((Fpu)dx * dx) + ((Fpu)dy * dy)) + (dz * dz)).F;
    }
}

/// <summary>A mounted gun, as soldiers and squads see it (<c>StationaryGun</c>).</summary>
public interface IIgi2AiMountedGun
{
    /// <summary>Gets its task id.</summary>
    int TaskId { get; }

    /// <summary>Gets where it stands.</summary>
    Vector3d Position { get; }

    /// <summary>Gets the way it points.</summary>
    Vector3f Forward { get; }

    /// <summary>Gets a value indicating whether someone mans it (<c>StationaryGun+0x250</c>).</summary>
    bool IsManned { get; }

    /// <summary>Gets a value indicating whether it is wrecked or disabled (<c>StationaryGun+0x24C</c> bits 1 and 2).</summary>
    bool IsWrecked { get; }

    /// <summary>Gets the seat the gunner sees from.</summary>
    Igi2SeatView Seat { get; }

    /// <summary>Lets its trigger go.</summary>
    void ReleaseTrigger();
}

/// <summary>Where a seated gunner's eyes are and how they face.</summary>
/// <param name="Position">The eyes.</param>
/// <param name="Orientation">Their frame, row by row.</param>
public sealed record Igi2SeatView(Vector3d Position, float[] Orientation);

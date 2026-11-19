using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>A door a walking soldier opens on its way, whichever game's door it is.</summary>
/// <remarks>
/// Both games' walkers do alike at a door step: they find the level's door nearest it, and by how the door stands open it
/// when it is closed or closing, wait while it opens, and walk through it open (Project I.G.I. <c>0x451540</c>, I.G.I. 2
/// <c>HumanAI_MoveCharacter</c>). Both number how a door stands alike, too, so a soldier of either game opens either's doors.
/// </remarks>
public interface IAiDoor
{
    /// <summary>The door is open: walk through.</summary>
    const int Open = 0;

    /// <summary>The door is closed: open it.</summary>
    const int Closed = 1;

    /// <summary>The door is opening: wait.</summary>
    const int Opening = 2;

    /// <summary>The door is closing: open it again.</summary>
    const int Closing = 3;

    /// <summary>Gets where the door is, as its game finds the nearest one to a step.</summary>
    Vector3d DoorPosition { get; }

    /// <summary>Gets how it stands: <see cref="Open"/>, <see cref="Closed"/>, <see cref="Opening"/> or <see cref="Closing"/>.</summary>
    int UseState { get; }

    /// <summary>Opens it, as a soldier at it does.</summary>
    void OpenForSoldier();

    /// <summary>The door nearest a point, as a walking soldier asks once a hop (<c>sub_446470</c>): of all of them, with no radius cap.</summary>
    /// <param name="doors">The level's doors.</param>
    /// <param name="near">Where to search from, in world units.</param>
    /// <returns>The nearest door, or <see langword="null"/> when the level holds none.</returns>
    static IAiDoor? Nearest(IEnumerable<IAiDoor> doors, Vector3d near)
    {
        ArgumentNullException.ThrowIfNull(doors);
        IAiDoor? best = null;
        double bestDistance = -1.0;
        foreach (IAiDoor door in doors)
        {
            Vector3d at = door.DoorPosition;
            double dx = at.X - near.X;
            double dy = at.Y - near.Y;
            double dz = at.Z - near.Z;
            double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            // The sentinel starts negative, so the first answer always wins -- sub_446470's test.
            if (bestDistance < 0.0 || distance < bestDistance)
            {
                bestDistance = distance;
                best = door;
            }
        }

        return best;
    }
}

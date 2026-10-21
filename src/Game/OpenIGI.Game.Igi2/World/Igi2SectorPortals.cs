using OpenIGI.Game.Igi2.Collision;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Which of each sector's portals are open, <c>Sector_s.atPortalStatus</c>: a byte a portal, which the doors standing in
/// them count up and down and the sector walk reads.
/// </summary>
/// <remarks>
/// <para>
/// Every sector starts with all sixty-four bytes at one (<c>memset(..., 1, 0x40)</c> in the sector object's create). A door
/// linked to a portal writes its own state there as it attaches and then adds one as it is told to open and takes one
/// away, not below nought, as it comes to rest shut (<c>Door_RunHandler</c>). Two doors in one portal therefore keep it
/// open until both are shut.
/// </para>
/// <para>
/// <c>Sector_IsPortalVisible</c> is the only reader: a portal whose byte is nought is not looked through. So the state
/// decides what the sector walk reaches and nothing else -- no line, no sound, no soldier reads it.
/// </para>
/// </remarks>
public sealed class Igi2SectorPortals
{
    /// <summary>How many portals a sector has bytes for, <c>atPortalStatus[64]</c>.</summary>
    public const int Capacity = 64;

    private readonly Dictionary<Igi2RigidObject, byte[]> _status = new(ReferenceEqualityComparer.Instance);
    private readonly List<Igi2RigidObject?> _bySector = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2SectorPortals"/> class: every sector of a level, every portal open.</summary>
    /// <param name="world">The collision world, which holds each sector object's solid.</param>
    /// <param name="graph">The level's sectors.</param>
    public Igi2SectorPortals(Igi2CollisionWorld world, Igi2SectorGraph graph)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(graph);
        // A sector is any object whose mesh carries portals: the placement's own model, or one of the parts a building places.
        // Of the placement's solids, its own model's if that is a sector, and otherwise the first that is.
        foreach (Igi2Sector sector in graph.Sectors)
        {
            Igi2RigidObject? found = null;
            foreach (Igi2RigidObject solid in world.SolidsOf(sector.Placement.TypeName, sector.Placement.LevelId))
            {
                if (!IsSector(solid))
                {
                    continue;
                }

                Bytes(solid, create: true);
                if (found is null
                    || (solid.Owner is Igi2CollisionOwner owner && string.Equals(owner.Model, sector.Placement.Model, StringComparison.OrdinalIgnoreCase)))
                {
                    found = solid;
                }
            }

            _bySector.Add(found);
        }
    }

    /// <summary>Gets whether a solid is a sector's, the test <c>Door_TraceForPortals</c> makes of what its line struck.</summary>
    /// <param name="solid">The solid.</param>
    /// <returns><see langword="true"/> when its mesh carries a portal.</returns>
    public static bool IsSector(Igi2RigidObject solid) =>
        solid?.Mesh(0) is { } mesh && Array.Exists(mesh.Portals, static p => p != Igi2MeshSearch.None);

    /// <summary>Gets a portal's byte.</summary>
    /// <param name="sector">The sector's solid.</param>
    /// <param name="portal">The portal.</param>
    /// <returns>The count, nought for shut.</returns>
    public byte this[Igi2RigidObject sector, int portal] => _status.TryGetValue(sector, out byte[]? bytes) && (uint)portal < Capacity ? bytes[portal] : (byte)1;

    /// <summary>Gets whether a portal of a sector of the graph is open, the answer <see cref="Igi2PortalCulling.Visit"/> asks for.</summary>
    /// <param name="sector">The sector's index in <see cref="Igi2SectorGraph.Sectors"/>.</param>
    /// <param name="portal">The portal.</param>
    /// <returns><see langword="true"/> unless its byte is nought.</returns>
    public bool IsOpen(int sector, int portal) =>
        (uint)sector >= (uint)_bySector.Count || _bySector[sector] is not { } solid || this[solid, portal] != 0;

    /// <summary>Sets a portal's byte, as a door attaching writes it.</summary>
    /// <param name="sector">The sector's solid.</param>
    /// <param name="portal">The portal.</param>
    /// <param name="value">The byte.</param>
    public void Set(Igi2RigidObject sector, int portal, byte value)
    {
        if (Bytes(sector) is { } bytes && (uint)portal < Capacity)
        {
            bytes[portal] = value;
        }
    }

    /// <summary>Adds one to a portal's byte, a door told to open.</summary>
    /// <param name="sector">The sector's solid.</param>
    /// <param name="portal">The portal.</param>
    public void Open(Igi2RigidObject sector, int portal)
    {
        if (Bytes(sector) is { } bytes && (uint)portal < Capacity)
        {
            bytes[portal]++;
        }
    }

    /// <summary>Takes one from a portal's byte, not below nought: a door come to rest shut.</summary>
    /// <param name="sector">The sector's solid.</param>
    /// <param name="portal">The portal.</param>
    /// <returns>Whether the byte was above nought.</returns>
    public bool Close(Igi2RigidObject sector, int portal)
    {
        if (Bytes(sector) is not { } bytes || (uint)portal >= Capacity)
        {
            return false;
        }

        if (bytes[portal] > 0)
        {
            bytes[portal]--;
            return true;
        }

        bytes[portal] = 0;
        return false;
    }

    // Every sector object's bytes start at one; one met first through a door's line is a sector all the same.
    private byte[]? Bytes(Igi2RigidObject sector, bool create = false)
    {
        if (_status.TryGetValue(sector, out byte[]? bytes))
        {
            return bytes;
        }

        if (!create && !IsSector(sector))
        {
            return null;
        }

        bytes = new byte[Capacity];
        Array.Fill(bytes, (byte)1);
        _status[sector] = bytes;
        return bytes;
    }
}

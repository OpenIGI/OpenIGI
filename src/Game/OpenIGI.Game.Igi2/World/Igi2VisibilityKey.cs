using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>What an I.G.I. 2 placement's visibility is decided by: see <see cref="Igi2SectorVisibility"/>.</summary>
public enum Igi2VisibilityKind
{
    /// <summary>A building's shell and every part of it that is not a room: drawn while the shell is reached.</summary>
    Shell,

    /// <summary>One of a building's rooms, with everything it carries: drawn while the room is reached.</summary>
    Room,

    /// <summary>
    /// Any other rigid object: drawn by the room its centre stands in when its type registers into rooms, and otherwise only
    /// while the outside is seen, unless its type is drawn whatever the camera stands in (<see cref="Igi2VisibilityKey.IsShownUnseen"/>).
    /// </summary>
    Object,
}

/// <summary>
/// The key an I.G.I. 2 placement carries in <see cref="OpenIGI.Game.World.WorldPlacement.VisibilityKey"/>, one per culled object
/// and shared by all of its parts.
/// </summary>
/// <param name="Kind">What decides it.</param>
/// <param name="LevelId">The placing task's level id.</param>
/// <param name="Model">The placing task's model: a building's shell model for a shell or a room.</param>
/// <param name="Position">Where the placing task's object stands, in world units.</param>
/// <param name="Room">The room, counted in the building's attachment order, or <see cref="Igi2Sector.Shell"/>.</param>
/// <param name="Centre">The centre of the object's bounding sphere, in world units: its model's render sphere, turned and placed.</param>
/// <param name="Radius">The radius of that sphere, in world units.</param>
/// <remarks>
/// A reference type on purpose: level ids repeat in the shipped levels, so a key is told apart from another by identity, and the
/// visibility resolves each to the sector it names once, by model and position.
/// </remarks>
public sealed record Igi2VisibilityKey(
    Igi2VisibilityKind Kind,
    int LevelId,
    string Model,
    Vector3d Position,
    int Room,
    Vector3d Centre,
    float Radius)
{
    /// <summary>Gets a value indicating whether the object's type registers into the room its centre stands in.</summary>
    /// <remarks>A <c>SectorInteriorObject</c> (<see cref="Igi2SectorInteriorTypes.RegistersInRooms"/>).</remarks>
    public bool RegistersInRooms { get; init; }

    /// <summary>
    /// Gets a value indicating whether the object is drawn, standing in no room, even while the camera is in a building that does
    /// not see out: a door or a conveyor belt (<see cref="Igi2SectorInteriorTypes.IsShownUnseen"/>).
    /// </summary>
    public bool IsShownUnseen { get; init; }

    /// <inheritdoc/>
    public bool Equals(Igi2VisibilityKey? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>What I.G.I. 2's sector cull makes of a task type.</summary>
/// <remarks>
/// <para>
/// <c>Direct3DRender_DrawRigidMesh</c> (<c>e/3/pc/pc_direct3drender.c</c>) decides every rigid mesh: one registered in a room
/// (<c>DynCubeObj_s.ptSector</c>) is drawn only while that room is reached and through its windows; any other, through the
/// outside's windows while the outside is seen, and -- while the camera stands in a building that does not see out -- not at all,
/// unless it is a <c>ConveyorBelt</c> or a <c>Door</c>.
/// </para>
/// <para>
/// Only a <c>SectorInteriorObject</c> registers in a room (<c>SectorInteriorObject_RegisterHandler</c>): every type the client
/// registers under it, directly or through <c>EditRigidObj</c>, <c>ExplodeObject</c>, <c>GenericTBA</c> and <c>Terminal</c>
/// (<c>QTask_NewQTaskType</c>'s parent argument).
/// </para>
/// </remarks>
public static class Igi2SectorInteriorTypes
{
    private static readonly HashSet<string> Interior = new(StringComparer.Ordinal)
    {
        "EditRigidObj",
        "ConveyorBelt",
        "AlarmLight",
        "AmmoPickup",
        "Cabinet",
        "ExplodeObject",
        "Bomb",
        "Generator",
        "Mine",
        "Radio",
        "GenericTBA",
        "PlaceExplosiveTBA",
        "Terminal",
        "AlarmControl",
        "SCameraControl",
        "GenericPickup",
        "AIStationaryGunHolder",
        "SCamera",
        "SCameraLens",
        "StationaryGun",
        "Switch",
    };

    /// <summary>Gets a value indicating whether objects of a task type register into the room their centre stands in.</summary>
    /// <param name="typeName">The task type's name.</param>
    /// <returns><see langword="true"/> for a <c>SectorInteriorObject</c> type.</returns>
    public static bool RegistersInRooms(string typeName) => Interior.Contains(typeName);

    /// <summary>
    /// Gets a value indicating whether objects of a task type are drawn, standing in no room, while the camera is in a building
    /// that does not see out: the two <c>Direct3DRender_DrawRigidMesh</c> names.
    /// </summary>
    /// <param name="typeName">The task type's name.</param>
    /// <returns><see langword="true"/> for a <c>ConveyorBelt</c> or a <c>Door</c>.</returns>
    public static bool IsShownUnseen(string typeName) => typeName is "ConveyorBelt" or "Door";
}

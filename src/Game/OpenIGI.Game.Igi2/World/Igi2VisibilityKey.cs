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
    /// Any other rigid object: left to the view alone unless its type is tested against the rooms
    /// (<see cref="Igi2VisibilityKey.SkipsRoomTest"/>); one that is, is drawn by the room its centre stands in, and standing in
    /// none only while the outside is seen.
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
    /// Gets a value indicating whether the object is left to the view alone, whatever room it or the camera stands in: a door,
    /// a lift, a conveyor belt (<see cref="Igi2SectorInteriorTypes.SkipsRoomTest"/>).
    /// </summary>
    public bool SkipsRoomTest { get; init; }

    /// <inheritdoc/>
    public bool Equals(Igi2VisibilityKey? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>What I.G.I. 2's sector cull makes of a task type.</summary>
/// <remarks>
/// <para>
/// <c>RigidDynCubeObj_EnterHandler</c> (<c>e/3/pc/pc_rigiddyncubeobj.c</c>) decides a rigid object before it is drawn, and only a
/// <c>SectorInteriorObject</c> is tested against the rooms there -- a <c>ConveyorBelt</c> and a <c>Mine</c> excepted. One
/// registered in a room (<c>DynCubeObj_s.ptSector</c>) is drawn only while that room is reached and through its windows; one in
/// no room, through the outside's windows while the outside is seen, and -- while the camera stands in a building that does not
/// see out -- not at all.
/// </para>
/// <para>
/// Every other type is tested against the view and nothing else, and handed on with the test marked as passed, which is what
/// <c>Direct3DRender_DrawRigidMesh</c> reads before its own room test: a <c>Lift</c>, a <c>Door</c> and a
/// <c>RotatingObject</c> derive from <c>RigidDynCubeObj</c> itself. A lift's car left out from inside its own shaft was the
/// floor the player rides showing the fog through it.
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
    /// Gets a value indicating whether objects of a task type are left to the view alone, whatever room they or the camera
    /// stand in: every type that is not a <c>SectorInteriorObject</c>, and the two of them the handler names.
    /// </summary>
    /// <param name="typeName">The task type's name.</param>
    /// <returns><see langword="false"/> for a <c>SectorInteriorObject</c> other than a <c>ConveyorBelt</c> or a <c>Mine</c>.</returns>
    public static bool SkipsRoomTest(string typeName) => !Interior.Contains(typeName) || typeName is "ConveyorBelt" or "Mine";
}

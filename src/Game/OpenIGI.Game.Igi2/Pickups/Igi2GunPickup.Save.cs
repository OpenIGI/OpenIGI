using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Pickups;

/// <content>
/// Its save state, <c>GunPickup_ptSerializeParameterList</c>: a model object's, then the rounds it gives, where it falls from and
/// how fast, the weapon by number and by name, its respawn, and whether it is taken, falling and landed.
/// </content>
/// <remarks>
/// <para>
/// <c>GunPickup_GetSerializeInfoHandler</c> answers both of <c>SerializeInfo</c>'s questions yes: a pickup is written against the
/// nearest task above it that has a level id, whether or not it has one, and a load that finds no pickup at a save's place makes
/// one there. <c>Game_LoadFromSlot</c> deletes every pickup first, so each the save holds is made again.
/// </para>
/// <para>
/// What a multiplayer game keeps of it -- its game item and its respawn -- is written as retail's own saves of a campaign level
/// hold it: item nought, <c>10 * GAME_FREQUENCY</c>, three hundred ticks, none left.
/// </para>
/// </remarks>
public sealed partial class Igi2GunPickup : ISavedState
{
    /// <summary>The type's name, <c>GunPickup</c>.</summary>
    public const string TypeName = Igi2GunPickupTask.TypeName;

    private const string DefaultRespawnTime = "10 * GAME_FREQUENCY";
    private const int DefaultRespawnTicks = 300;

    /// <summary>
    /// Gets or sets the task a pickup made by a load stands beneath, or <see langword="null"/> for one beneath the level's
    /// <c>Dynamic</c> task, where the game makes a dropped weapon.
    /// </summary>
    public GameTask? Holder { get; set; }

    /// <summary>
    /// Gets or sets the level id a pickup made by a load keeps, as its save named it (<c>Level_DefineQTaskID</c> in
    /// <c>Serialize_GetChildTaskFromArgList</c>); <c>-1</c> for one made at run time.
    /// </summary>
    public int LevelId { get; set; } = GameTask.NoId;

    /// <inheritdoc/>
    public bool IsSavedWithParent => true;

    /// <inheritdoc/>
    /// <remarks>One that was taken is deleted, and is no longer in the level to be written.</remarks>
    public bool IsSaved => !IsDelete;

    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Igi2ModelObjState.Write(writer, Position, Orientation);
        writer.Int(AmmoInClip);
        writer.Array((ReadOnlySpan<int>)_ammoForFireType);
        writer.Real64x3(Position);
        writer.Real3(Speed);
        writer.Real64(0.0);
        writer.Int(WeaponNumber);
        writer.Text(WeaponType.WeaponId);
        writer.Int(0);
        writer.Text(Task is { RespawnTime.Length: > 0 } task ? task.RespawnTime : DefaultRespawnTime);
        writer.Int(DefaultRespawnTicks);
        writer.Int(0);
        writer.Bool(IsDelete);
        writer.Bool(IsMoving);
        writer.Bool(IsLanded);
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader) => Apply(Saved.Read(reader));

    /// <summary>Takes a saved pickup's state: where it lies and how, what it gives, and how it moves.</summary>
    /// <param name="saved">The state.</param>
    /// <remarks>
    /// <c>GunPickup_DeSerializeHandler</c> copies the model's place over the place it falls from, so the one read for that is not
    /// used.
    /// </remarks>
    public void Apply(in Saved saved)
    {
        Position = saved.Position;
        saved.Orientation.CopyTo(Orientation, 0);
        AmmoInClip = saved.AmmoInClip;
        for (int i = 0; i < _ammoForFireType.Length; i++)
        {
            _ammoForFireType[i] = i < saved.AmmoForFireType.Length ? saved.AmmoForFireType[i] : 0;
        }

        Speed = saved.Speed;
        IsDelete = saved.IsDelete;
        IsMoving = saved.IsMoving;
        IsLanded = saved.IsLanded;
    }

    /// <summary>Takes the pickup out of the level, as <c>Game_LoadFromSlot</c> deletes each before a load.</summary>
    public void Delete() => IsDelete = true;

    /// <summary>A pickup's state as a save holds it.</summary>
    /// <param name="Position">Where it lies.</param>
    /// <param name="Orientation">Its orientation, row by row.</param>
    /// <param name="AmmoInClip">The rounds its load holds.</param>
    /// <param name="AmmoForFireType">The rounds it gives each fire type.</param>
    /// <param name="Speed">Its speed a tick.</param>
    /// <param name="WeaponId">The weapon it gives, by its <c>WEAPON_ID_*</c> name.</param>
    /// <param name="IsDelete">Whether it was taken.</param>
    /// <param name="IsMoving">Whether it is falling.</param>
    /// <param name="IsLanded">Whether it has landed.</param>
    public readonly record struct Saved(
        Vector3d Position,
        float[] Orientation,
        int AmmoInClip,
        int[] AmmoForFireType,
        Vector3f Speed,
        string WeaponId,
        bool IsDelete,
        bool IsMoving,
        bool IsLanded)
    {
        /// <summary>Reads a pickup's state, in its list's order.</summary>
        /// <param name="reader">The reader.</param>
        /// <returns>The state.</returns>
        public static Saved Read(StateReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            (Vector3d position, float[] orientation, _) = Igi2ModelObjState.Read(reader);
            int ammoInClip = reader.Int();
            int[] ammo = reader.IntArray();
            reader.Real64x3();
            Vector3f speed = reader.Real3();
            reader.Real64();
            reader.Int();
            string weaponId = reader.Text();
            reader.Int();
            reader.Text();
            reader.Int();
            reader.Int();
            bool isDelete = reader.Bool(), isMoving = reader.Bool(), isLanded = reader.Bool();
            return new Saved(position, orientation, ammoInClip, ammo, speed, weaponId, isDelete, isMoving, isLanded);
        }
    }
}

/// <content>Its save state: its pickup's (<see cref="Igi2GunPickup.WriteState"/>).</content>
public sealed partial class Igi2GunPickupTask : ISavedState
{
    /// <inheritdoc/>
    public bool IsSavedWithParent => true;

    /// <inheritdoc/>
    public bool IsSaved => Pickup is { IsDelete: false };

    /// <inheritdoc/>
    public void WriteState(StateWriter writer) => Pickup?.WriteState(writer);

    /// <inheritdoc/>
    public void ReadState(StateReader reader) => Pickup?.ReadState(reader);
}

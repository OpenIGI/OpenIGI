using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <content>
/// Its save state, <c>Mine_ptSerializeParameterList</c> (mine.c): an exploding object's (<c>ExplodeObject_ptSerializeParameterList</c>
/// -- a model object's, then whether it has exploded or is to, its damage and its delay), then its flags, the ticks before it arms
/// and before it explodes, that time in seconds, its detection radius, and whether it is armed, can be, and is a remote one.
/// </content>
/// <remarks>
/// <para>
/// <c>Mine_GetSerializeInfoHandler</c> answers both of <c>SerializeInfo</c>'s questions yes: a mine is written against the nearest
/// task above it with a level id -- the level's <c>Dynamic</c> task, beneath which it is laid -- and a load makes one where a save
/// names one. <c>Game_LoadFromSlot</c> deletes every mine first.
/// </para>
/// <para>
/// Its flags word is not read from the original; here its first bit keeps whether the fuse has been set off.
/// </para>
/// </remarks>
public sealed partial class Igi2Mine : ISavedState
{
    /// <summary>Gets a value indicating whether it was deleted, as a load deletes every mine before it reads its save.</summary>
    public bool IsDeleted { get; private set; }

    /// <inheritdoc/>
    public bool IsSavedWithParent => true;

    /// <inheritdoc/>
    public bool IsSaved => !IsDeleted && !_explosive.IsExploded;

    /// <summary>Takes the mine out of the level: its next run ends it.</summary>
    public void Delete() => IsDeleted = true;

    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _explosive.WriteState(writer);
        writer.Int(IsTriggered ? 1 : 0);
        writer.Int(_fuse.ArmTicks);
        writer.Int(_fuse.CountdownTicks);
        writer.Real(TimeBeforeExplode);
        writer.Real(DetectionRadius);
        writer.Bool(IsArmed);
        writer.Bool(!IsRemote);
        writer.Bool(IsRemote);
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader) => Apply(Saved.Read(reader));

    /// <summary>Takes a saved mine's state: what it has taken, and how far its fuse has run.</summary>
    /// <param name="saved">The state.</param>
    public void Apply(in Saved saved)
    {
        _explosive.Restore(saved.IsExploded, saved.IsExplode, saved.Damage, saved.Delay);
        _fuse.Restore(saved.ArmTicks, saved.CountdownTicks, saved.IsArmed, (saved.Flags & 1) != 0);
    }

    /// <summary>A mine's state as a save holds it.</summary>
    /// <param name="Position">Where it lies.</param>
    /// <param name="Frame">Its frame, row by row.</param>
    /// <param name="IsExploded">Whether it has exploded.</param>
    /// <param name="IsExplode">Whether it is to explode.</param>
    /// <param name="Damage">The damage it has taken.</param>
    /// <param name="Delay">Its explosion's delay, in ticks.</param>
    /// <param name="Flags">Its flags.</param>
    /// <param name="ArmTicks">The ticks before it arms.</param>
    /// <param name="CountdownTicks">The ticks before it explodes, once set off.</param>
    /// <param name="TimeBeforeExplode">That time, in seconds.</param>
    /// <param name="DetectionRadius">How near a human sets it off, in metres.</param>
    /// <param name="IsArmed">Whether it is armed.</param>
    /// <param name="IsRemote">Whether it is set off from the map.</param>
    public readonly record struct Saved(
        Vector3d Position,
        float[] Frame,
        bool IsExploded,
        bool IsExplode,
        float Damage,
        int Delay,
        int Flags,
        int ArmTicks,
        int CountdownTicks,
        float TimeBeforeExplode,
        float DetectionRadius,
        bool IsArmed,
        bool IsRemote)
    {
        /// <summary>Reads a mine's state, in its list's order.</summary>
        /// <param name="reader">The reader.</param>
        /// <returns>The state.</returns>
        public static Saved Read(StateReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            (Vector3d position, float[] frame, _) = Saves.Igi2ModelObjState.Read(reader);
            bool exploded = reader.Bool(), explode = reader.Bool();
            float damage = reader.Real();
            int delay = reader.Int(), flags = reader.Int(), arm = reader.Int(), countdown = reader.Int();
            float time = reader.Real(), radius = reader.Real();
            bool armed = reader.Bool();
            reader.Bool();
            return new Saved(position, frame, exploded, explode, damage, delay, flags, arm, countdown, time, radius, armed, reader.Bool());
        }
    }
}

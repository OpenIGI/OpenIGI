using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.World;

/// <summary>
/// One record of a model's <c>ATTA</c> chunk: a named point on the model that another object hangs off.
/// </summary>
/// <remarks>
/// <para>
/// Attachments are how the world grows past what the level script places. <c>0x4DFA50</c> walks the
/// attachments of a placed model, looks each <see cref="Name"/> up in the magic-object table that
/// <c>magicobj/magicobj.qvm</c> fills through the <c>DefineMagicObj</c> native, and spawns a child object
/// carrying that definition's model at this attachment's position and orientation. The child's own model
/// is walked the same way, so the tree nests.
/// </para>
/// <para>
/// This is why a level-1 garage whose script node has no children at all still owns 67 baked lightmaps:
/// three for its own LOD chain and 64 for the crates, doors and lamps its model attaches.
/// </para>
/// </remarks>
/// <param name="Name">The magic-object type name, up to 16 bytes, NUL-padded on disk.</param>
/// <param name="Position">The attachment point in model space.</param>
/// <param name="Orientation">The attachment's rotation, stored row by row.</param>
/// <param name="Value">The trailing field, whose meaning is still open.</param>
public readonly record struct MefAttachment(string Name, Vector3f Position, Matrix3f Orientation, int Value)
{
    /// <summary>The size in bytes of one record.</summary>
    public const int Stride = 68;

    /// <summary>The size in bytes of the name field.</summary>
    public const int NameSize = 16;

    /// <inheritdoc/>
    public override string ToString() => $"{Name} at {Position}";
}

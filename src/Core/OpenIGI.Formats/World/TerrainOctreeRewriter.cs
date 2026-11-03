using System.Buffers.Binary;

namespace OpenIGI.Formats.World;

/// <summary>One node of the octree as the descent reaches it: where it is, how deep, and how it is turned.</summary>
/// <param name="Record">The record it is read from; one record serves many nodes.</param>
/// <param name="CentreX">The node's centre X, in engine units.</param>
/// <param name="CentreY">The node's centre Y.</param>
/// <param name="CentreZ">The node's centre Z.</param>
/// <param name="Exponent">Its depth below the root, which is its level-of-detail exponent.</param>
/// <param name="Orientation">The orientation the descent reached it with; see <see cref="TerrainOrientation"/>.</param>
public readonly record struct TerrainNodePlace(int Record, long CentreX, long CentreY, long CentreZ, int Exponent, int Orientation)
{
    /// <summary>Gets the node's half-width in engine units.</summary>
    public long Extent => (long)TerrainOctree.RootExtent >> Exponent;

    /// <summary>Gets how many engine units one step of a packed vertex coordinate is worth.</summary>
    public double Unit => Extent / (double)TerrainPackedVertex.UnitsPerExtent;
}

/// <summary>What to put in one octant of a node instead of what is there: a generated subtree, or nothing at all.</summary>
/// <param name="Node">The subtree, or <see langword="null"/> to take the octant's child away.</param>
public readonly record struct TerrainChildReplacement(TerrainGeneratedNode? Node);

/// <summary>
/// Changes the geometry of some nodes of a terrain octree, and can replace whole subtrees with generated ones, and writes
/// <c>terrain.ctr</c> and <c>terrain.cmd</c> again.
/// </summary>
/// <remarks>
/// <para>
/// A record serves every place the descent reaches it from (the file is a DAG), so a node whose block changes gets a copy
/// of its record pointing at the new block, and so does every node on the way down to it, each pointing at the copy below
/// it. The copies and the new blocks go after everything the files already hold; the root (record 1, where the descent
/// starts) is changed where it stands. Every record that did not change keeps its index and its bytes.
/// </para>
/// <para>
/// This is safe in the original because nothing there is keyed by record index: the walk (<c>0x4D14E0</c>) caches the
/// meshes it builds by the cell's world position, and reads only the child indices, orientation bytes, child mask and
/// block pointer of a record, all of which a copy carries.
/// </para>
/// </remarks>
public static class TerrainOctreeRewriter
{
    /// <summary>Rewrites the nodes an edit changes.</summary>
    /// <param name="ctr">The <c>terrain.ctr</c> bytes.</param>
    /// <param name="cmd">The <c>terrain.cmd</c> bytes.</param>
    /// <param name="reaches">Whether a node's cube can hold anything the edit changes; the descent stops where it cannot.</param>
    /// <param name="edit">A node's new block, or <see langword="null"/> to leave it as it is.</param>
    /// <param name="replace">
    /// What goes in an octant of a node the descent reaches, asked for all eight whether or not one holds a child; a
    /// <see langword="null"/> answer leaves the octant to the ordinary walk. Generated nodes are written turned to the
    /// identity, their parent's slot carrying whatever orientation undoes the parent's own.
    /// </param>
    /// <returns>The two files again, or <see langword="null"/> when no block changed.</returns>
    /// <exception cref="ArgumentNullException">A delegate is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">The files are not a usable octree, or the copies would need more records than an index can address.</exception>
    public static (byte[] Ctr, byte[] Cmd)? Rewrite(
        ReadOnlyMemory<byte> ctr,
        ReadOnlyMemory<byte> cmd,
        Func<TerrainNodePlace, bool> reaches,
        Func<TerrainNodePlace, TerrainCommandBlock, byte[]?> edit,
        Func<TerrainNodePlace, int, TerrainChildReplacement?>? replace = null)
    {
        ArgumentNullException.ThrowIfNull(reaches);
        ArgumentNullException.ThrowIfNull(edit);
        TerrainOctree tree = TerrainOctree.Read(ctr);
        if (!tree.IsUsable)
        {
            throw new PackFormatException($"The terrain octree cannot be rewritten: {string.Join("; ", tree.Defects)}.");
        }

        Writer writer = new(tree, ctr, TerrainCommands.Read(cmd), cmd.Length, reaches, edit, replace);
        writer.Visit(new TerrainNodePlace(TerrainOctree.RootIndex, 0, 0, 0, 0, TerrainOrientation.Identity));
        return writer.Changed ? writer.Finish(cmd) : null;
    }

    private sealed class Writer(
        TerrainOctree tree,
        ReadOnlyMemory<byte> ctr,
        TerrainCommands commands,
        int cmdLength,
        Func<TerrainNodePlace, bool> reaches,
        Func<TerrainNodePlace, TerrainCommandBlock, byte[]?> edit,
        Func<TerrainNodePlace, int, TerrainChildReplacement?>? replace)
    {
        private readonly byte[] _root = ctr.Slice(TerrainOctree.RootIndex * TerrainNode.Stride, TerrainNode.Stride).ToArray();
        private readonly List<byte[]> _records = [];
        private readonly List<byte[]> _blocks = [];
        private long _cmdLength = cmdLength;
        private long _emptyBlock = -1;
        private readonly Dictionary<TerrainGeneratedNode, int> _written = new(ReferenceEqualityComparer.Instance);

        public bool Changed { get; private set; }

        // The record the node is drawn from now: its own when nothing in or under it changed, else a changed copy.
        public int Visit(TerrainNodePlace place)
        {
            int index = place.Record;
            if (!reaches(place))
            {
                return index;
            }

            TerrainNode node = tree[index];
            byte[]? block = commands.TryGetBlock(node.CommandOffset, out TerrainCommandBlock found) ? edit(place, found) : null;
            Span<int> replaced = stackalloc int[TerrainOrientation.Count];
            replaced.Fill(-1);
            Span<int> turned = stackalloc int[TerrainOrientation.Count];
            turned.Fill(-1);
            int mask = node.ChildMask;
            bool childChanged = false;
            if ((!node.IsLeaf || replace is not null) && place.Exponent < 30)
            {
                int depth = place.Exponent + 1;
                long step = (long)TerrainOctree.RootExtent >> depth;
                for (int octant = 0; octant < TerrainOrientation.Count; octant++)
                {
                    int slot = TerrainOrientation.ChildSlot(place.Orientation, octant);
                    if (replace?.Invoke(place, octant) is { } replacement)
                    {
                        if (replacement.Node is { } generated)
                        {
                            replaced[slot] = WriteGenerated(generated);
                            turned[slot] = Undo(place.Orientation);
                            mask |= 1 << slot;
                            childChanged = true;
                        }
                        else if (node.HasChild(slot))
                        {
                            mask &= ~(1 << slot);
                            childChanged = true;
                        }

                        continue;
                    }

                    if (!node.HasChild(slot))
                    {
                        continue;
                    }

                    int child = node.Child(slot);
                    TerrainNodePlace below = new(
                        child,
                        place.CentreX + ((octant & 1) != 0 ? step : -step),
                        place.CentreY + ((octant & 2) != 0 ? step : -step),
                        place.CentreZ + ((octant & 4) != 0 ? step : -step),
                        depth,
                        TerrainOrientation.Compose(place.Orientation, node.Orientation(slot) & 7));
                    int now = Visit(below);
                    if (now != child)
                    {
                        replaced[slot] = now;
                        childChanged = true;
                    }
                }
            }

            if (block is null && !childChanged)
            {
                return index;
            }

            Changed = true;
            byte[] record = index == TerrainOctree.RootIndex ? _root : ctr.Slice(index * TerrainNode.Stride, TerrainNode.Stride).ToArray();
            for (int slot = 0; slot < TerrainOrientation.Count; slot++)
            {
                if (replaced[slot] >= 0)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(slot * 2), (ushort)replaced[slot]);
                }

                if (turned[slot] >= 0)
                {
                    record[16 + slot] = (byte)turned[slot];
                }

                if ((mask & (1 << slot)) == 0 && ((node.ChildMask >> slot) & 1) != 0)
                {
                    // A child taken away: the slot is cleared as an unused one is.
                    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(slot * 2), TerrainNode.NoChild);
                }
            }

            record[24] = (byte)mask;

            if (block is not null)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(28), Place(block));
            }

            if (index == TerrainOctree.RootIndex)
            {
                return index;
            }

            return Append(record);
        }

        // A generated subtree, children first so their indices are known, every node turned to the identity.
        private int WriteGenerated(TerrainGeneratedNode generated)
        {
            // A node shared by many places (the empty one) is one record.
            if (_written.TryGetValue(generated, out int known))
            {
                return known;
            }

            byte[] record = new byte[TerrainNode.Stride];
            int mask = 0;
            for (int octant = 0; octant < TerrainOrientation.Count; octant++)
            {
                int slot = TerrainOrientation.ChildSlot(TerrainOrientation.Identity, octant);
                ushort index = TerrainNode.NoChild;
                if (generated.Children[octant] is { } child)
                {
                    index = (ushort)WriteGenerated(child);
                    mask |= 1 << slot;
                }

                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(slot * 2), index);
            }

            record[24] = (byte)mask;

            // The two bytes that are the same across a whole level, whatever they mean, are the level's.
            record[25] = _root[25];
            record[26] = _root[26];
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(28), Place(generated.Block));
            Changed = true;
            int appended = Append(record);
            _written[generated] = appended;
            return appended;
        }

        // Puts a block after everything, sharing one copy of the empty block.
        private uint Place(byte[] block)
        {
            bool empty = BinaryPrimitives.ReadUInt16LittleEndian(block) == 0 && block.Length == TerrainCommandBlock.HeaderSize;
            if (empty && _emptyBlock >= 0)
            {
                return (uint)_emptyBlock;
            }

            if (_cmdLength + block.Length > uint.MaxValue)
            {
                throw new PackFormatException("The terrain geometry would outgrow what a node's offset can address.");
            }

            uint at = (uint)_cmdLength;
            _blocks.Add(block);
            _cmdLength += block.Length;
            _emptyBlock = empty ? at : _emptyBlock;
            return at;
        }

        private int Append(byte[] record)
        {
            int created = tree.Count + _records.Count;
            if (created >= TerrainNode.NoChild)
            {
                throw new PackFormatException("The changed terrain needs more octree records than a child index can address.");
            }

            _records.Add(record);
            return created;
        }

        // The orientation byte that, folded into a parent's, leaves its child at the identity.
        private static int Undo(int parent)
        {
            for (int b = 0; b < TerrainOrientation.Count; b++)
            {
                if (TerrainOrientation.Compose(parent, b) == TerrainOrientation.Identity)
                {
                    return b;
                }
            }

            throw new InvalidOperationException($"Orientation {parent} has no inverse.");
        }

        public (byte[] Ctr, byte[] Cmd) Finish(ReadOnlyMemory<byte> cmd)
        {
            byte[] newCtr = new byte[ctr.Length + (_records.Count * TerrainNode.Stride)];
            ctr.Span.CopyTo(newCtr);
            _root.CopyTo(newCtr.AsSpan(TerrainOctree.RootIndex * TerrainNode.Stride));
            for (int i = 0; i < _records.Count; i++)
            {
                _records[i].CopyTo(newCtr.AsSpan(ctr.Length + (i * TerrainNode.Stride)));
            }

            byte[] newCmd = new byte[_cmdLength];
            cmd.Span.CopyTo(newCmd);
            int cursor = cmd.Length;
            foreach (byte[] block in _blocks)
            {
                block.CopyTo(newCmd.AsSpan(cursor));
                cursor += block.Length;
            }

            return (newCtr, newCmd);
        }
    }
}

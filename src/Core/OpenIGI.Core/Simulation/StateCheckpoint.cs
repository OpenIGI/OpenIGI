using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenIGI.Core.Simulation;

/// <summary>
/// A simulation as it stood at a moment, held in memory and put back exactly: every object of its state recorded by identity, a
/// restore writing each one's fields back into the same object. Nothing is serialized or rebuilt, so whatever else holds a
/// reference to the simulation's objects -- a renderer, a sound -- still holds a live one; objects made since are simply no
/// longer reachable from it.
/// </summary>
/// <remarks>
/// <para>
/// What is state is the <see cref="StateBoundary"/>'s to say. A capture walks from the roots through every reference the state
/// holds (inside struct fields and struct arrays too, so a dictionary's entries and a list's items are reached as the plain
/// objects they are), copying each object shallowly and each array whole. An array of plain values whose content has not changed
/// since the previous checkpoint shares that one's copy, so data that never changes costs its memory once.
/// </para>
/// <para>
/// A checkpoint is never written into: it can be restored any number of times. Static fields are not recorded.
/// </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2072", Justification = StateTypePlan.Why)]
[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = StateTypePlan.Why)]
public sealed class StateCheckpoint
{
    private readonly object[] _roots;
    private readonly List<Entry> _entries;
    private readonly Dictionary<object, int> _index;

    private StateCheckpoint(object[] roots, List<Entry> entries, Dictionary<object, int> index, long bytes, long ownBytes)
    {
        _roots = roots;
        _entries = entries;
        _index = index;
        Bytes = bytes;
        OwnBytes = ownBytes;
    }

    /// <summary>Gets whether checkpoints can be made here: they compile code for each type, which needs the JIT.</summary>
    public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;

    /// <summary>Gets how many objects and arrays it holds.</summary>
    public int Objects => _entries.Count;

    /// <summary>Gets roughly how many bytes of state it holds, shared arrays included.</summary>
    public long Bytes { get; }

    /// <summary>Gets roughly how many bytes it holds that no earlier checkpoint shares.</summary>
    public long OwnBytes { get; }

    /// <summary>Records the state reachable from the roots.</summary>
    /// <param name="roots">Where the walk starts: the simulation's top objects.</param>
    /// <param name="boundary">What is state.</param>
    /// <param name="previous">An earlier checkpoint of the same simulation, whose unchanged arrays are shared.</param>
    /// <returns>The checkpoint.</returns>
    /// <exception cref="NotSupportedException">Where <see cref="IsSupported"/> is false.</exception>
    public static StateCheckpoint Capture(IEnumerable<object> roots, StateBoundary boundary, StateCheckpoint? previous = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(boundary);
        if (!IsSupported)
        {
            throw new NotSupportedException("State checkpoints need dynamic code.");
        }

        object[] rootList = [.. roots];
        List<Entry> entries = [];
        Dictionary<object, int> index = new(ReferenceEqualityComparer.Instance);
        HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
        Stack<object> stack = new();
        long bytes = 0;
        long own = 0;
        for (int i = rootList.Length - 1; i >= 0; i--)
        {
            stack.Push(rootList[i]);
        }

        while (stack.TryPop(out object? item))
        {
            if (!seen.Add(item))
            {
                continue;
            }

            if (item is Delegate bound)
            {
                // Never copied -- a delegate cannot change -- but what it is bound to may be state (a closure, a handler's owner).
                foreach (Delegate single in bound.GetInvocationList())
                {
                    if (single.Target is { } target)
                    {
                        stack.Push(target);
                    }
                }

                continue;
            }

            Type type = item.GetType();
            if (!boundary.IsState(type))
            {
                continue;
            }

            StateTypePlan plan = StateTypePlan.For(type);
            if (item is Array array)
            {
                long size = (long)array.Length * plan.ElementSize;
                object copy;
                if (!plan.ElementHoldsReferences && previous?.CopyOf(array) is Array earlier && SameBytes(earlier, array, size))
                {
                    copy = earlier;
                }
                else
                {
                    copy = array.Clone();
                    own += size;
                }

                bytes += size;
                index[item] = entries.Count;
                entries.Add(new Entry(item, copy, plan));
                if (plan.ElementIsReference)
                {
                    foreach (object? element in array)
                    {
                        if (element is not null and not string)
                        {
                            stack.Push(element);
                        }
                    }
                }
                else if (plan.ElementHoldsReferences)
                {
                    if (plan.Visit is { } visit)
                    {
                        visit(array, stack);
                    }
                    else
                    {
                        // An array of more than one rank: its struct elements walked boxed.
                        foreach (object? element in array)
                        {
                            if (element is not null)
                            {
                                StateTypePlan.For(element.GetType()).Visit?.Invoke(element, stack);
                            }
                        }
                    }
                }

                continue;
            }

            long objectSize = (plan.Fields.Length + 2L) * IntPtr.Size;
            bytes += objectSize;
            own += objectSize;
            index[item] = entries.Count;
            entries.Add(new Entry(item, plan.Clone!(item), plan));
            plan.Visit?.Invoke(item, stack);
        }

        return new StateCheckpoint(rootList, entries, index, bytes, own);
    }

    /// <summary>Puts every recorded object back as it stood.</summary>
    public void Restore()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Original is Array original)
            {
                Array.Copy((Array)entry.Copy, original, original.Length);
            }
            else
            {
                entry.Plan.CopyInto!(entry.Copy, entry.Original);
            }
        }
    }

    /// <summary>Gets whether an object is one this checkpoint recorded.</summary>
    /// <param name="value">The object.</param>
    /// <returns><see langword="true"/> when it is recorded here.</returns>
    public bool Holds(object value) => _index.ContainsKey(value);

    /// <summary>What it holds, by type: the most bytes first.</summary>
    /// <returns>Each type's name, how many objects of it, and their bytes.</returns>
    public IReadOnlyList<(string Type, int Count, long Bytes)> Census() =>
        [.. _entries
            .GroupBy(static e => e.Plan.Type)
            .Select(static g => (Name(g.Key), g.Count(), g.Sum(static e => e.Original is Array a ? (long)a.Length * e.Plan.ElementSize : (e.Plan.Fields.Length + 2L) * IntPtr.Size)))
            .OrderByDescending(static c => c.Item3)];

    /// <summary>
    /// Finds where another checkpoint's state differs from this one's, walking both from their roots in step, so objects made in two
    /// separate runs are compared where they stand rather than by identity.
    /// </summary>
    /// <param name="other">A checkpoint taken from the same roots.</param>
    /// <param name="limit">The most differences to report.</param>
    /// <returns>The paths that differ and how, or an empty list when the two are the same.</returns>
    /// <remarks>
    /// Values compare exactly (floats bit for bit through <see cref="object.Equals(object)"/>). Objects outside the state compare by
    /// type only, so two loads of the same level, whose resources are different objects, compare as equal.
    /// </remarks>
    public IReadOnlyList<string> Differences(StateCheckpoint other, int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(other);
        List<string> found = [];
        Dictionary<object, object> paired = new(ReferenceEqualityComparer.Instance);
        Queue<(object A, object B, string Path)> pending = new();
        for (int i = 0; i < Math.Min(_roots.Length, other._roots.Length); i++)
        {
            pending.Enqueue((_roots[i], other._roots[i], $"root{i}:{Name(_roots[i].GetType())}"));
        }

        while (pending.TryDequeue(out (object A, object B, string Path) pair) && found.Count < limit)
        {
            (object a, object b, string path) = pair;
            if (paired.TryGetValue(a, out object? before))
            {
                if (!ReferenceEquals(before, b) && other.Holds(b))
                {
                    found.Add($"{path}: reaches a different object than it did elsewhere");
                }

                continue;
            }

            paired[a] = b;
            if (a.GetType() != b.GetType())
            {
                found.Add($"{path}: {Name(a.GetType())} vs {Name(b.GetType())}");
                continue;
            }

            if (!Holds(a) || !other.Holds(b))
            {
                continue;
            }

            object copyA = _entries[_index[a]].Copy;
            object copyB = other._entries[other._index[b]].Copy;
            if (HashEntries(copyA) is { } entriesA && other.HashEntries(copyB) is { } entriesB)
            {
                // A hash table's slots are filled in the order of its history (freed ones reused), which two loads need not share:
                // its entries are compared as a set, each side in the order of what its keys are.
                if (entriesA.Count != entriesB.Count)
                {
                    found.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: {entriesA.Count} entries vs {entriesB.Count}"));
                    continue;
                }

                for (int i = 0; i < entriesA.Count; i++)
                {
                    CompareValue(entriesA[i].Entry, entriesB[i].Entry, $"{path}{{{entriesA[i].Key}}}", other, pending, found);
                }

                continue;
            }

            if (copyA is Array arrayA)
            {
                Array arrayB = (Array)copyB;
                if (arrayA.Length != arrayB.Length)
                {
                    found.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: length {arrayA.Length} vs {arrayB.Length}"));
                    continue;
                }

                int at = 0;
                IEnumerator<object?> itemsB = arrayB.Cast<object?>().GetEnumerator();
                foreach (object? itemA in arrayA)
                {
                    itemsB.MoveNext();
                    CompareValue(itemA, itemsB.Current, string.Create(CultureInfo.InvariantCulture, $"{path}[{at++}]"), other, pending, found);
                }

                itemsB.Dispose();
                continue;
            }

            foreach (FieldInfo field in StateTypePlan.For(a.GetType()).Restored)
            {
                if (!IsBookkeeping(field))
                {
                    CompareValue(field.GetValue(copyA), field.GetValue(copyB), $"{path}.{field.Name}", other, pending, found);
                }
            }
        }

        return found;
    }

    private void CompareValue(object? a, object? b, string path, StateCheckpoint other, Queue<(object, object, string)> pending, List<string> found)
    {
        if (a is null || b is null)
        {
            if (a is not null || b is not null)
            {
                found.Add($"{path}: {Describe(a)} vs {Describe(b)}");
            }

            return;
        }

        Type type = a.GetType();
        if (type != b.GetType())
        {
            found.Add($"{path}: {Name(type)} vs {Name(b.GetType())}");
            return;
        }

        if (type.IsValueType && StateTypePlan.HoldsReferences(type))
        {
            foreach (FieldInfo field in StateTypePlan.InstanceFields(type))
            {
                if (!IsBookkeeping(field))
                {
                    CompareValue(field.GetValue(a), field.GetValue(b), $"{path}.{field.Name}", other, pending, found);
                }
            }

            return;
        }

        if (type.IsValueType || a is string)
        {
            if (!a.Equals(b))
            {
                found.Add($"{path}: {Describe(a)} vs {Describe(b)}");
            }

            return;
        }

        if (a is Delegate da && b is Delegate db)
        {
            if (da.Method != db.Method)
            {
                found.Add($"{path}: calls {da.Method.Name} vs {db.Method.Name}");
            }
            else if (da.Target is { } ta && db.Target is { } tb)
            {
                pending.Enqueue((ta, tb, $"{path}.Target"));
            }

            return;
        }

        if (Holds(a) || other.Holds(b))
        {
            pending.Enqueue((a, b, path));
        }
    }

    // What two loads of the same state legitimately differ in, inside the BCL's collections: how often each was changed, and where
    // a hash table filed its entries (identity hash codes differ between objects). Its entries -- keys and values, in the order
    // they were added -- are still compared.
    private static bool IsBookkeeping(FieldInfo field)
    {
        if (field.IsDefined(typeof(DerivedStateAttribute)))
        {
            return true;
        }

        Type owner = field.DeclaringType!;
        string space = owner.Namespace ?? string.Empty;
        if (!space.StartsWith("System.Collections", StringComparison.Ordinal))
        {
            return false;
        }

        return field.Name is "_version" or "_buckets" or "_fastModMultiplier" or "_freeList" or "_freeCount" or "_count"
            || (owner.IsNested && owner.IsValueType && field.Name.ToUpperInvariant() is "HASHCODE" or "NEXT");
    }

    // A Dictionary's or HashSet's entries as recorded, the free slots left out, ordered by a description of each key; null for
    // anything else.
    private List<(string Key, object Entry)>? HashEntries(object copy)
    {
        Type type = copy.GetType();
        if (type.Namespace != "System.Collections.Generic" || !type.IsGenericType
            || type.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic) is not { } entriesField
            || type.GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic) is not { } countField
            || type.GetField("_buckets", BindingFlags.Instance | BindingFlags.NonPublic) is null)
        {
            return null;
        }

        int count = (int)countField.GetValue(copy)!;
        List<(string, object)> live = [];
        if (entriesField.GetValue(copy) is not Array original || CopyOf(original) is not Array entries)
        {
            return live;
        }

        for (int i = 0; i < Math.Min(count, entries.Length); i++)
        {
            object entry = entries.GetValue(i)!;
            FieldInfo[] fields = entry.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo next = fields.First(static f => f.Name.Equals("next", StringComparison.OrdinalIgnoreCase));
            if ((int)next.GetValue(entry)! < -1)
            {
                continue; // a freed slot
            }

            FieldInfo key = fields.FirstOrDefault(static f => f.Name == "key") ?? fields.First(static f => f.Name == "Value");
            live.Add((KeyText(key.GetValue(entry)), entry));
        }

        live.Sort(static (x, y) => string.CompareOrdinal(x.Item1, y.Item1));
        return live;
    }

    // What a key is, to order a hash table's entries by: its value, or a recorded object's type and plain fields.
    private string KeyText(object? key)
    {
        if (key is null || key.GetType().IsPrimitive || key is string or Enum)
        {
            return Describe(key);
        }

        object shown = CopyOf(key) ?? key;
        IEnumerable<string> plain = StateTypePlan.For(shown.GetType()).Fields
            .Where(static f => f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum)
            .Select(f => Describe(f.GetValue(shown)));
        return $"{Name(key.GetType())}({string.Join(",", plain)})";
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        string text => $"\"{text}\"",
        _ => value.ToString() ?? Name(value.GetType()),
    };

    // A type as code names it: Outer.Inner, its generic arguments in brackets (a nested type's own and its outer type's together).
    private static string Name(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        static string Plain(Type t) => t.Name.IndexOf('`', StringComparison.Ordinal) is var tick and >= 0 ? t.Name[..tick] : t.Name;
        string name = Plain(type);
        for (Type? outer = type.DeclaringType; outer is not null; outer = outer.DeclaringType)
        {
            name = $"{Plain(outer)}.{name}";
        }

        return type.IsGenericType ? $"{name}<{string.Join(",", type.GetGenericArguments().Select(Name))}>" : name;
    }

    private static bool SameBytes(Array earlier, Array now, long size) =>
        earlier.Length == now.Length
        && size <= int.MaxValue
        && MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(earlier), (int)size)
            .SequenceEqual(MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(now), (int)size));

    private object? CopyOf(object original) => _index.TryGetValue(original, out int at) ? _entries[at].Copy : null;

    private readonly record struct Entry(object Original, object Copy, StateTypePlan Plan);
}

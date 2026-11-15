using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace OpenIGI.Core.Simulation;

/// <summary>
/// How a <see cref="StateCheckpoint"/> handles objects of one type, compiled once: a shallow copy, a copy of every field back
/// into the original (readonly ones too), and a walk over the references it holds, through struct fields and struct arrays.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = StateTypePlan.Why)]
[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = StateTypePlan.Why)]
[UnconditionalSuppressMessage("Trimming", "IL2060", Justification = StateTypePlan.Why)]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = StateTypePlan.Why)]
internal sealed class StateTypePlan
{
    /// <summary>Why the reflection here is sound: checkpoints run in the desktop editor and tests, under the JIT, over live objects.</summary>
    internal const string Why = "Checkpoints run only where dynamic code is supported (the desktop editor and its tests); every type planned is one a live object has, so its fields exist.";

    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<Type, StateTypePlan> Plans = new();
    private static readonly ConcurrentDictionary<Type, bool> HoldsReferencesCache = new();
    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(nameof(MemberwiseClone), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo PushMethod = typeof(StateTypePlan).GetMethod(nameof(Push), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo SizeOfMethod = typeof(StateTypePlan).GetMethod(nameof(SizeOf), BindingFlags.Static | BindingFlags.NonPublic)!;

    private StateTypePlan(Type type)
    {
        Type = type;
        Fields = [.. InstanceFields(type)];
        Restored = [.. Fields.Where(static f => !f.IsDefined(typeof(NotSimulationStateAttribute)))];
        if (type.IsArray)
        {
            Type element = type.GetElementType()!;
            ElementHoldsReferences = HoldsReferences(element);
            ElementIsReference = !element.IsValueType;
            ElementSize = element.IsValueType ? (int)SizeOfMethod.MakeGenericMethod(element).Invoke(null, null)! : IntPtr.Size;
            if (element.IsValueType && ElementHoldsReferences && type.GetArrayRank() == 1)
            {
                Visit = StructArrayVisitor(type, element);
            }

            return;
        }

        if (type.IsValueType)
        {
            // A boxed struct is never recorded; its plan only walks it, for the arrays of more than one rank that box elements.
            Visit = Visitor(type);
            return;
        }

        Clone = CloneMethod();
        CopyInto = Copier(type, Restored);
        Visit = Visitor(type);
    }

    /// <summary>Gets the type.</summary>
    public Type Type { get; }

    /// <summary>Gets every instance field, base types' included.</summary>
    public FieldInfo[] Fields { get; }

    /// <summary>Gets the fields a restore puts back: all but those marked <see cref="NotSimulationStateAttribute"/>.</summary>
    public FieldInfo[] Restored { get; }

    /// <summary>Gets a shallow copy of an object, or null for an array or a struct.</summary>
    public Func<object, object>? Clone { get; }

    /// <summary>Gets what copies a saved copy's fields into the original: (copy, original).</summary>
    public Action<object, object>? CopyInto { get; }

    /// <summary>Gets what pushes every reference an object (or a struct array's elements) holds, or null when it holds none to push.</summary>
    public Action<object, Stack<object>>? Visit { get; }

    /// <summary>Gets whether an array's elements hold references, so the array is walked and never shared.</summary>
    public bool ElementHoldsReferences { get; }

    /// <summary>Gets whether an array's elements are references themselves.</summary>
    public bool ElementIsReference { get; }

    /// <summary>Gets the size of an array's element, in bytes.</summary>
    public int ElementSize { get; }

    /// <summary>Gets the plan for a type, made the first time it is asked for.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The plan.</returns>
    public static StateTypePlan For(Type type) => Plans.GetOrAdd(type, static t => new StateTypePlan(t));

    /// <summary>Gets whether a type is, or holds, a reference: what a walk has to look into.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> for a reference type, or a struct with one in it.</returns>
    /// <remarks>A string counts: an array of them holds references, and is compared and kept as one, never byte for byte.</remarks>
    public static bool HoldsReferences(Type type) => HoldsReferencesCache.GetOrAdd(type, static t =>
        !t.IsValueType
        || (!t.IsPrimitive && !t.IsEnum && !t.IsPointer && InstanceFields(t).Any(static f => !f.FieldType.IsPointer && HoldsReferences(f.FieldType))));

    /// <summary>Gets a type's instance fields, its base types' included.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The fields.</returns>
    public static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        for (Type? at = type; at is not null && at != typeof(object) && at != typeof(ValueType) && at != typeof(Array); at = at.BaseType)
        {
            foreach (FieldInfo field in at.GetFields(Instance))
            {
                yield return field;
            }
        }
    }

    private static int SizeOf<T>() => Unsafe.SizeOf<T>();

    private static void Push(Stack<object> stack, object? value)
    {
        if (value is not null and not string)
        {
            stack.Push(value);
        }
    }

    private static Func<object, object> CloneMethod()
    {
        DynamicMethod method = new("StateClone", typeof(object), [typeof(object)], typeof(StateTypePlan).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, MemberwiseCloneMethod);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object, object>>();
    }

    // (copy, original) => every field of the copy stored into the original: a DynamicMethod, which may store a readonly field.
    private static Action<object, object> Copier(Type type, FieldInfo[] fields)
    {
        DynamicMethod method = new($"StateRestore_{type.Name}", null, [typeof(object), typeof(object)], typeof(StateTypePlan).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        LocalBuilder source = il.DeclareLocal(type);
        LocalBuilder target = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, type);
        il.Emit(OpCodes.Stloc, source);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, type);
        il.Emit(OpCodes.Stloc, target);
        foreach (FieldInfo field in fields)
        {
            il.Emit(OpCodes.Ldloc, target);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Stfld, field);
        }

        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<object, object>>();
    }

    private static Action<object, Stack<object>>? Visitor(Type type)
    {
        ParameterExpression boxed = Expression.Parameter(typeof(object), "o");
        ParameterExpression stack = Expression.Parameter(typeof(Stack<object>), "stack");
        ParameterExpression typed = Expression.Variable(type, "typed");
        List<Expression> body = [Expression.Assign(typed, Expression.Convert(boxed, type))];
        PushFields(typed, type, stack, body);
        return body.Count == 1 ? null
            : Expression.Lambda<Action<object, Stack<object>>>(Expression.Block([typed], body), boxed, stack).Compile();
    }

    private static Action<object, Stack<object>> StructArrayVisitor(Type arrayType, Type element)
    {
        ParameterExpression boxed = Expression.Parameter(typeof(object), "o");
        ParameterExpression stack = Expression.Parameter(typeof(Stack<object>), "stack");
        ParameterExpression array = Expression.Variable(arrayType, "array");
        ParameterExpression index = Expression.Variable(typeof(int), "i");
        LabelTarget done = Expression.Label("done");
        List<Expression> each = [];
        PushFields(Expression.ArrayAccess(array, index), element, stack, each);
        each.Add(Expression.PostIncrementAssign(index));
        Expression body = Expression.Block(
            [array, index],
            Expression.Assign(array, Expression.Convert(boxed, arrayType)),
            Expression.Assign(index, Expression.Constant(0)),
            Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, Expression.ArrayLength(array)),
                    Expression.Block(each),
                    Expression.Break(done)),
                done));
        return Expression.Lambda<Action<object, Stack<object>>>(body, boxed, stack).Compile();
    }

    // Every reference a value holds, pushed: its reference fields, and those inside its struct fields.
    private static void PushFields(Expression instance, Type type, ParameterExpression stack, List<Expression> into)
    {
        foreach (FieldInfo field in InstanceFields(type))
        {
            Type kind = field.FieldType;
            if (kind.IsPointer || kind.IsByRef || kind.IsByRefLike || kind == typeof(string) || field.IsDefined(typeof(NotSimulationStateAttribute)))
            {
                continue;
            }

            if (!kind.IsValueType)
            {
                into.Add(Expression.Call(PushMethod, stack, Expression.Convert(Expression.Field(instance, field), typeof(object))));
            }
            else if (HoldsReferences(kind))
            {
                PushFields(Expression.Field(instance, field), kind, stack, into);
            }
        }
    }
}

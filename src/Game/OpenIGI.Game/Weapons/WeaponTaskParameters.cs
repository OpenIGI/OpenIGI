using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// How many arguments each projectile and weapon task class reads from a <c>DefineWeaponType</c> call,
/// and what it turns them into.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is why the call is not a fixed-length one.</b> <c>0x413F90</c> reads argument 47 as the
/// projectile task type, then hands the call to that class's parameter reader with a cursor starting at
/// 48; the reader consumes as many arguments as it wants and writes the cursor back. The weapon task
/// type is read <i>at the returned cursor</i>, and its own reader then consumes the rest. Retail
/// argument counts are 49, 50, 52 and 53 — a reader that assumed 49 would mis-read six of the
/// twenty-four weapons.
/// </para>
/// <para>
/// Each entry below is transcribed from the class's reader. The two that matter beyond arity:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// The zoom readers (<c>0x4732F0</c> for the MP5SD and the Dragunov, <c>0x470CE0</c> for the
/// binoculars) take a <b>field of view in whole degrees</b> and store its half-angle tangent. The
/// halving is integer division — <c>cdq; sub eax, edx; sar eax, 1</c>, truncating toward zero — so the
/// Dragunov's 5° becomes <c>tan(2°)</c>, not <c>tan(2.5°)</c>.
/// </description>
/// </item>
/// <item>
/// <description>
/// The binoculars store their three angles <b>out of order</b>: the first two land in slots 1 and 2 and
/// the third in slot 0, so the script's <c>5, 90, 70</c> reads as minimum, maximum, default.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class WeaponTaskParameters
{
    private static readonly WeaponTaskParameterSlot[] None = [];

    // A field of view in whole degrees, stored as the tangent of its (integer-halved) half-angle.
    private static readonly WeaponTaskParameterSlot[] SingleZoom =
    [
        new(WeaponTaskParameterKind.Int32, WeaponTaskParameterTransform.HalfAngleTangent, 0),
    ];

    private static readonly Dictionary<string, WeaponTaskParameterSlot[]> Readers =
        new(StringComparer.Ordinal)
        {
            // Projectiles. Only the missile takes anything: the model its rocket is drawn with.
            ["TASKTYPE_MISSILE"] =
            [
                new(WeaponTaskParameterKind.String16, WeaponTaskParameterTransform.None, 0),
            ],

            // Weapons. 0x4732F0, shared by both zoomed guns.
            ["TASKTYPE_GUNMP5SD"] = SingleZoom,
            ["TASKTYPE_GUNDRAGUNOV"] = SingleZoom,

            // 0x473790 — the M203 launcher slung under the M16A2: muzzle velocity, explosion radius,
            // activate radius. The same three appear as level tags with defaults 256, 5 and 1.
            ["TASKTYPE_GUNM16A2"] =
            [
                new(WeaponTaskParameterKind.Real32, WeaponTaskParameterTransform.None, 0),
                new(WeaponTaskParameterKind.Real32, WeaponTaskParameterTransform.None, 1),
                new(WeaponTaskParameterKind.Real32, WeaponTaskParameterTransform.None, 2),
            ],

            // 0x475900 — trigger delay, closing radius, activation radius, explosion radius.
            ["TASKTYPE_PROXIMITYMINE"] =
            [
                new(WeaponTaskParameterKind.Real32, WeaponTaskParameterTransform.None, 0),
                new(WeaponTaskParameterKind.Real32, WeaponTaskParameterTransform.None, 1),
                new(WeaponTaskParameterKind.Real32, WeaponTaskParameterTransform.None, 2),
                new(WeaponTaskParameterKind.Real32, WeaponTaskParameterTransform.None, 3),
            ],

            // 0x470540 — the fraction of a human's maximum health one use restores.
            ["TASKTYPE_MEDIPACK"] =
            [
                new(WeaponTaskParameterKind.Real32, WeaponTaskParameterTransform.None, 0),
            ],

            // 0x470CE0 — minimum, maximum and default field of view, stored 1, 2, 0.
            ["TASKTYPE_BINOCULAR"] =
            [
                new(WeaponTaskParameterKind.Int32, WeaponTaskParameterTransform.HalfAngleTangent, 1),
                new(WeaponTaskParameterKind.Int32, WeaponTaskParameterTransform.HalfAngleTangent, 2),
                new(WeaponTaskParameterKind.Int32, WeaponTaskParameterTransform.HalfAngleTangent, 0),
            ],
        };

    /// <summary>
    /// The degrees-to-radians factor the zoom readers use, as the single-precision constant at
    /// <c>0x53367C</c>.
    /// </summary>
    /// <remarks>
    /// Stored in the executable as a <see langword="float"/> (<c>0x3C8EFA35</c>), not a double, so it is
    /// a little short of the exact value. The multiply and the tangent then happen on the x87 stack at
    /// extended precision before the result is rounded back to a <see langword="float"/>.
    /// </remarks>
    public const float DegreesToRadians = 0.01745329238474369f;

    /// <summary>Gets the number of arguments a task class reads.</summary>
    /// <param name="taskTypeName">The <c>TASKTYPE_*</c> constant naming the class.</param>
    /// <returns>The count, which is zero for a class that reads none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskTypeName"/> is <see langword="null"/>.</exception>
    public static int ArgumentCount(string taskTypeName) => SlotsFor(taskTypeName).Length;

    /// <summary>Reads a task class's parameters, advancing the argument cursor over them.</summary>
    /// <param name="taskTypeName">The <c>TASKTYPE_*</c> constant naming the class.</param>
    /// <param name="taskTypeId">The value that constant resolved to.</param>
    /// <param name="call">The <c>DefineWeaponType</c> call being read.</param>
    /// <param name="cursor">The next argument to read; advanced past the ones consumed.</param>
    /// <returns>The binding, whose values are already decoded.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// A class this table does not know reads nothing, which is what the original does when the class
    /// registers no reader for the slot — the null check on the method pointer in <c>0x413F90</c>.
    /// </remarks>
    public static WeaponTaskBinding Read(
        string taskTypeName,
        int taskTypeId,
        QvmCallContext call,
        ref int cursor)
    {
        ArgumentNullException.ThrowIfNull(call);

        WeaponTaskParameterSlot[] slots = SlotsFor(taskTypeName);
        if (slots.Length == 0)
        {
            return new WeaponTaskBinding(taskTypeName, taskTypeId, [], string.Empty, 0);
        }

        float[] values = new float[slots.Length];
        string text = string.Empty;
        int start = cursor;

        foreach (WeaponTaskParameterSlot slot in slots)
        {
            switch (slot.Kind)
            {
                case WeaponTaskParameterKind.String16:
                    text = call.GetString(cursor);
                    break;

                case WeaponTaskParameterKind.Real32:
                    values[slot.StoreIndex] = call.GetSingle(cursor);
                    break;

                case WeaponTaskParameterKind.Int32:
                default:
                    int degrees = call.GetInt32(cursor);
                    values[slot.StoreIndex] = slot.Transform == WeaponTaskParameterTransform.HalfAngleTangent
                        ? HalfAngleTangent(degrees)
                        : degrees;
                    break;
            }

            cursor++;
        }

        return new WeaponTaskBinding(taskTypeName, taskTypeId, values, text, cursor - start);
    }

    /// <summary>Converts a whole-degree field of view to the tangent of its half-angle.</summary>
    /// <param name="fieldOfViewDegrees">The angle in whole degrees.</param>
    /// <returns>The tangent.</returns>
    /// <remarks>
    /// The halving is <b>integer</b> division truncating toward zero, so an odd angle loses its half
    /// degree: the Dragunov's 5° gives <c>tan(2°)</c>.
    /// </remarks>
    public static float HalfAngleTangent(int fieldOfViewDegrees) =>
        (float)Math.Tan((fieldOfViewDegrees / 2) * (double)DegreesToRadians);

    private static WeaponTaskParameterSlot[] SlotsFor(string taskTypeName)
    {
        ArgumentNullException.ThrowIfNull(taskTypeName);
        return Readers.GetValueOrDefault(taskTypeName, None);
    }
}

using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>Reads the parameters a declared task carries, one reader per way the world code has always read them.</summary>
/// <remarks>
/// The readers are not interchangeable. Each one keeps the exact coercion its callers were written
/// against: which stored types it accepts, whether text loses a trailing line break, whether an empty
/// string counts as missing, and whether a number widens to <see cref="float"/> or <see cref="double"/>.
/// A caller picks the reader by the behaviour its name states, never by which looks closest.
/// </remarks>
internal static class DeclaredTaskValues
{
    /// <summary>Reads a string parameter as stored, or an empty string when it is missing or not a string.</summary>
    internal static string TextOrEmpty(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

    /// <summary>Reads a string parameter with any trailing carriage returns and line feeds removed, or an empty string when it is missing or not a string.</summary>
    internal static string TextTrimmed(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text.TrimEnd('\r', '\n') : string.Empty;

    /// <summary>Reads a string parameter as stored, or <paramref name="fallback"/> when it is missing, not a string, or empty.</summary>
    internal static string TextOrFallback(DeclaredTask task, string name, string fallback) =>
        task.TryGetValue(name, out object? value) && value is string text && text.Length > 0 ? text : fallback;

    /// <summary>Reads the first string parameter whose name contains <paramref name="fragment"/>, ignoring case, or an empty string.</summary>
    /// <remarks>An empty or blank string still counts as the match.</remarks>
    internal static string FirstTextNamed(DeclaredTask task, string fragment)
    {
        for (int i = 0; i < task.Type.Parameters.Count; i++)
        {
            if (task.Type.Parameters[i].Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                && task.GetValue(i) is string text)
            {
                return text;
            }
        }

        return string.Empty;
    }

    /// <summary>Reads the first string parameter whose name contains <paramref name="fragment"/>, ignoring case, and that is not blank, or an empty string.</summary>
    /// <remarks>Unlike <see cref="FirstTextNamed"/>, a blank value is passed over and the search goes on.</remarks>
    internal static string FirstNonBlankTextNamed(DeclaredTask task, string fragment)
    {
        IReadOnlyList<TaskParameter> parameters = task.Type.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                && task.GetValue(i) is string value
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    /// <summary>Reads a parameter stored as a <see cref="bool"/>; anything else, or a missing parameter, reads as <see langword="false"/>.</summary>
    internal static bool BoolStrict(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is bool flag && flag;

    /// <summary>Reads a flag stored as a <see cref="bool"/>, or as a nonzero <see cref="byte"/> or <see cref="int"/>; anything else reads as <see langword="false"/>.</summary>
    internal static bool BoolLenient(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value switch
        {
            bool flag => flag,
            byte number => number != 0,
            int number => number != 0,
            _ => false,
        };

    /// <summary>Reads a parameter stored as an <see cref="int"/>, or <paramref name="fallback"/> for any other type.</summary>
    internal static int IntStrict(DeclaredTask task, string name, int fallback = 0) =>
        task.TryGetValue(name, out object? value) && value is int number ? number : fallback;

    /// <summary>Reads a parameter stored as an <see cref="int"/>, or <see langword="null"/> for any other type.</summary>
    internal static int? IntStrictOrNull(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is int integer ? integer : null;

    /// <summary>Reads an integer stored as a <see cref="short"/>, <see cref="int"/> or <see cref="byte"/>, or <paramref name="fallback"/>.</summary>
    internal static int IntWidening(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? raw) ? raw switch
        {
            short value => value,
            int value => value,
            byte value => value,
            _ => fallback,
        } : fallback;

    /// <summary>Reads an integer stored as an <see cref="int"/> or <see cref="short"/>, or a real truncated toward zero, or <paramref name="fallback"/>.</summary>
    internal static int IntTruncating(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            int integer => integer,
            short small => small,
            float real => (int)real,
            double real => (int)real,
            _ => fallback,
        } : fallback;

    /// <summary>Reads a parameter stored as a <see cref="float"/>, or <paramref name="fallback"/> for any other type.</summary>
    internal static float RealStrict(DeclaredTask task, string name, float fallback = 0f) =>
        task.TryGetValue(name, out object? value) && value is float number ? number : fallback;

    /// <summary>Reads a <see cref="float"/> parameter whose name matches <paramref name="compactName"/> once spaces are removed from both, ignoring case.</summary>
    /// <remarks>Only a stored <see cref="float"/> counts; the first name that matches decides.</remarks>
    internal static float RealStrictIgnoringSpaces(DeclaredTask task, string compactName, float fallback = 0f)
    {
        string wanted = compactName.Replace(" ", string.Empty, StringComparison.Ordinal);
        for (int i = 0; i < task.Type.Parameters.Count; i++)
        {
            string actual = task.Type.Parameters[i].Name.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (actual.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                && task.GetValue(i) is float value)
            {
                return value;
            }
        }

        return fallback;
    }

    /// <summary>Reads a real stored as a <see cref="float"/> or a <see cref="double"/> narrowed to <see cref="float"/>, or <paramref name="fallback"/>.</summary>
    internal static float RealFromFloatOrDouble(DeclaredTask task, string name, float fallback = 0f) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            float number => number,
            double number => (float)number,
            _ => fallback,
        } : fallback;

    /// <summary>Reads a real stored as a <see cref="float"/>, <see cref="double"/> or <see cref="int"/>, as a <see cref="float"/>, or <paramref name="fallback"/> when it is missing or not a number.</summary>
    internal static float RealFromNumber(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            float single => single,
            double real => (float)real,
            int integer => integer,
            _ => fallback,
        } : fallback;

    /// <summary>Reads a real stored as a <see cref="float"/>, <see cref="double"/>, <see cref="int"/> or <see cref="short"/>, as a <see cref="float"/>, or <paramref name="fallback"/>.</summary>
    internal static float RealFromNumberOrShort(DeclaredTask task, string name, float fallback = 0f)
    {
        if (!task.TryGetValue(name, out object? raw))
        {
            return fallback;
        }

        // OBJECTS.QVM declares Explosion fireballs as Int32, while the other explosion tuning
        // values are Real32.  The native parameter reader accepts either numeric stack type; do the
        // same here so the authored fireball count is not silently replaced by zero.
        return raw switch
        {
            float value => value,
            double value => (float)value,
            int value => value,
            short value => value,
            _ => fallback,
        };
    }

    /// <summary>Reads any <see cref="IConvertible"/> parameter converted to <see cref="float"/> with the invariant culture, or <paramref name="fallback"/>.</summary>
    internal static float RealConverted(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is IConvertible convertible
            ? convertible.ToSingle(CultureInfo.InvariantCulture)
            : fallback;

    /// <summary>Reads a real stored as a <see cref="float"/>, <see cref="double"/> or <see cref="int"/>, as a <see cref="double"/>, or <paramref name="fallback"/>.</summary>
    internal static double DoubleFromNumber(DeclaredTask task, string name, double fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            float single => single,
            double real => real,
            int integer => integer,
            _ => fallback,
        } : fallback;

    /// <summary>Reads a parameter stored as a <see cref="Vector3d"/>, or <paramref name="fallback"/> for any other type.</summary>
    internal static Vector3d Vector(DeclaredTask task, string name, Vector3d fallback = default) =>
        task.TryGetValue(name, out object? value) && value is Vector3d vector ? vector : fallback;

    /// <summary>Reads a parameter stored as a <see cref="Vector3f"/>, or the zero vector for any other type.</summary>
    internal static Vector3f Vector32(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3f vector ? vector : default;
}

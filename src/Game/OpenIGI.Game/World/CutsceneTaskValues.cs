using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>Reads the parameters a cutscene, its vehicles and its cameras declare, as the cutscene runtime has always read them.</summary>
/// <remarks>
/// Each reader keeps its own coercion: <c>Real</c> takes a stored float only, <c>Single</c> converts any
/// number, and <c>RealNamed</c> matches the parameter name with its spaces removed.
/// </remarks>
internal static class CutsceneTaskValues
{
    internal static string Text(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;
    internal static bool Bool(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) && value is bool flag && flag;
    internal static float Single(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is IConvertible convertible
            ? convertible.ToSingle(CultureInfo.InvariantCulture)
            : fallback;
    internal static int Int(DeclaredTask task, string name, int fallback = 0) => task.TryGetValue(name, out object? value) && value is int number ? number : fallback;
    internal static float Real(DeclaredTask task, string name, float fallback = 0f) => task.TryGetValue(name, out object? value) && value is float number ? number : fallback;
    internal static float RealNamed(DeclaredTask task, string compactName, float fallback = 0f)
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
    internal static Vector3d Vector(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) && value is Vector3d vector ? vector : default;
    internal static Vector3f Vector32(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) && value is Vector3f vector ? vector : default;
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
}

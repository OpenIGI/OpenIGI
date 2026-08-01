using System.Numerics;

namespace OpenIGI.Core.Configuration;

/// <summary>
/// Declares console variables.
/// </summary>
/// <remarks>
/// <para>
/// A subsystem declares its settings as static fields next to the code that reads them, for example:
/// </para>
/// <code>
/// [CVarContainer]
/// public static class FogCVars
/// {
///     public static readonly NumericCVar&lt;int&gt; Quality =
///         CVar.Int("r_fog_quality", 3, 0, 4, CVarOptions.Archive, "Volumetric fog quality: 0 off .. 4 ultra");
/// }
/// </code>
/// <para>
/// Each factory registers into <see cref="CVarRegistry.Default"/> unless another registry is named,
/// and <c>[CVarContainer]</c> makes the generated module initialiser touch the class so the field
/// initialisers run without anything having to remember to call a registration method.
/// </para>
/// </remarks>
public static class CVar
{
    /// <summary>Declares a whole-number variable.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="minimum">The lowest accepted value, or <see langword="null"/> when unbounded below.</param>
    /// <param name="maximum">The highest accepted value, or <see langword="null"/> when unbounded above.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    /// <param name="registry">The registry to add it to, or <see langword="null"/> for <see cref="CVarRegistry.Default"/>.</param>
    /// <returns>The registered variable.</returns>
    public static NumericCVar<int> Int(
        string name,
        int defaultValue,
        int? minimum = null,
        int? maximum = null,
        CVarOptions flags = CVarOptions.None,
        string help = "",
        CVarRegistry? registry = null) =>
        Register(new NumericCVar<int>(name, defaultValue, minimum, maximum, flags, help), registry);

    /// <summary>Declares a real-valued variable.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="minimum">The lowest accepted value, or <see langword="null"/> when unbounded below.</param>
    /// <param name="maximum">The highest accepted value, or <see langword="null"/> when unbounded above.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    /// <param name="registry">The registry to add it to, or <see langword="null"/> for <see cref="CVarRegistry.Default"/>.</param>
    /// <returns>The registered variable.</returns>
    public static NumericCVar<float> Float(
        string name,
        float defaultValue,
        float? minimum = null,
        float? maximum = null,
        CVarOptions flags = CVarOptions.None,
        string help = "",
        CVarRegistry? registry = null) =>
        Register(new NumericCVar<float>(name, defaultValue, minimum, maximum, flags, help), registry);

    /// <summary>Declares a numeric variable of any type generic math supports.</summary>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="minimum">The lowest accepted value, or <see langword="null"/> when unbounded below.</param>
    /// <param name="maximum">The highest accepted value, or <see langword="null"/> when unbounded above.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    /// <param name="registry">The registry to add it to, or <see langword="null"/> for <see cref="CVarRegistry.Default"/>.</param>
    /// <returns>The registered variable.</returns>
    public static NumericCVar<T> Number<T>(
        string name,
        T defaultValue,
        T? minimum = null,
        T? maximum = null,
        CVarOptions flags = CVarOptions.None,
        string help = "",
        CVarRegistry? registry = null)
        where T : struct, INumber<T> =>
        Register(new NumericCVar<T>(name, defaultValue, minimum, maximum, flags, help), registry);

    /// <summary>Declares a switch.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    /// <param name="registry">The registry to add it to, or <see langword="null"/> for <see cref="CVarRegistry.Default"/>.</param>
    /// <returns>The registered variable.</returns>
    public static BoolCVar Bool(
        string name,
        bool defaultValue,
        CVarOptions flags = CVarOptions.None,
        string help = "",
        CVarRegistry? registry = null) =>
        Register(new BoolCVar(name, defaultValue, flags, help), registry);

    /// <summary>Declares a free-text variable.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    /// <param name="registry">The registry to add it to, or <see langword="null"/> for <see cref="CVarRegistry.Default"/>.</param>
    /// <returns>The registered variable.</returns>
    public static StringCVar Text(
        string name,
        string defaultValue,
        CVarOptions flags = CVarOptions.None,
        string help = "",
        CVarRegistry? registry = null) =>
        Register(new StringCVar(name, defaultValue, flags, help), registry);

    /// <summary>Declares a variable selecting one of a fixed set of named choices.</summary>
    /// <typeparam name="TEnum">The enumeration the variable selects from.</typeparam>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    /// <param name="registry">The registry to add it to, or <see langword="null"/> for <see cref="CVarRegistry.Default"/>.</param>
    /// <returns>The registered variable.</returns>
    public static EnumCVar<TEnum> Choice<TEnum>(
        string name,
        TEnum defaultValue,
        CVarOptions flags = CVarOptions.None,
        string help = "",
        CVarRegistry? registry = null)
        where TEnum : struct, Enum =>
        Register(new EnumCVar<TEnum>(name, defaultValue, flags, help), registry);

    private static TVar Register<TVar>(TVar variable, CVarRegistry? registry)
        where TVar : ICVar =>
        (registry ?? CVarRegistry.Default).Add(variable);
}

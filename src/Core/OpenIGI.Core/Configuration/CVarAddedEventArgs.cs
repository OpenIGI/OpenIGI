namespace OpenIGI.Core.Configuration;

/// <summary>Describes a console variable just registered.</summary>
/// <param name="variable">The variable.</param>
public sealed class CVarAddedEventArgs(ICVar variable) : EventArgs
{
    /// <summary>Gets the variable.</summary>
    public ICVar Variable { get; } = variable;
}

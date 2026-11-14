using System.Reflection;

namespace OpenIGI.Frontend;

/// <summary>Which build this is, as its assembly says it: for the landing page's footer and the credits.</summary>
internal static class BuildStamp
{
    private static readonly string Informational =
        typeof(BuildStamp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

    /// <summary>Gets the version, without the commit a build may carry after it; empty when the assembly names none.</summary>
    public static string Version
    {
        get
        {
            int plus = Informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? Informational : Informational[..plus];
        }
    }

    /// <summary>Gets the first seven characters of the commit the build was made from, or empty when it carries none.</summary>
    public static string Commit
    {
        get
        {
            int plus = Informational.IndexOf('+', StringComparison.Ordinal);
            if (plus < 0)
            {
                return string.Empty;
            }

            string commit = Informational[(plus + 1)..];
            return commit.Length > 7 ? commit[..7] : commit;
        }
    }
}

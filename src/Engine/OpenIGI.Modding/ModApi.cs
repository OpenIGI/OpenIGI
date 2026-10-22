namespace OpenIGI.Modding;

/// <summary>The modding API's version, which a manifest names.</summary>
public static class ModApi
{
    /// <summary>The version of the API this build offers. A mod written against another is refused.</summary>
    /// <remarks>
    /// Version 1 is shipped: its surface is <c>PublicAPI.Shipped.txt</c>, which the analyzer holds the build to. Adding to it
    /// is allowed; changing or removing anything in it is a new version.
    /// </remarks>
    public const int Version = 1;
}

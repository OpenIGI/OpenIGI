using System.Runtime.CompilerServices;

// The types moved out of the desktop executable stay internal: they were never public API, and making
// 26,000 lines public would demand an XML doc on every member of it (CS1591 is an error here) for no
// gain. Only the seam types this project introduces are public. The hosts and title projects that
// consume the renderers are named here instead.
[assembly: InternalsVisibleTo("OpenIGI.Host")]
[assembly: InternalsVisibleTo("OpenIGI.Desktop")]
[assembly: InternalsVisibleTo("OpenIGI.Presentation.Tests")]
[assembly: InternalsVisibleTo("OpenIGI.Desktop.Tests")]

// A title's own renderers live in its own project and draw with the shared presentation helpers -- the
// canvas, the badge sprite, the vector primitives. Promoting each one to public API as it is needed
// would grow the documented surface of this assembly for types nothing outside the port uses; two friend
// entries, one per title, is the bounded version of the same thing.
[assembly: InternalsVisibleTo("OpenIGI.Game.Igi1")]
[assembly: InternalsVisibleTo("OpenIGI.Game.Igi2")]

using System.Runtime.CompilerServices;

// The types moved out of the desktop executable stay internal: they were never public API, and making
// 26,000 lines public would demand an XML doc on every member of it (CS1591 is an error here) for no
// gain. Only the seam types this project introduces are public. The hosts and title projects that
// consume the renderers are named here instead.
[assembly: InternalsVisibleTo("OpenIGI.Host")]
[assembly: InternalsVisibleTo("OpenIGI.Desktop")]
[assembly: InternalsVisibleTo("OpenIGI.Presentation.Tests")]
[assembly: InternalsVisibleTo("OpenIGI.Desktop.Tests")]

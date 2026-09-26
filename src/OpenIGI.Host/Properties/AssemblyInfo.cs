using OpenIGI.Host.Configuration;
using OpenIGI.Host;
using System.Runtime.CompilerServices;

// The host's types were the desktop executable's internals and stay internal: they were never public
// API, and making them public would demand an XML doc on every member (CS1591 is an error here) for no
// gain. Only GameHost and HostOptions — the seam an app composition root uses — are public.
[assembly: InternalsVisibleTo("OpenIGI.Desktop")]
[assembly: InternalsVisibleTo("OpenIGI.Web")]
[assembly: InternalsVisibleTo("OpenIGI.Host.Tests")]
[assembly: InternalsVisibleTo("OpenIGI.Desktop.Tests")]

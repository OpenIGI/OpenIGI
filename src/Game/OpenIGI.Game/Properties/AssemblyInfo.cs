using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenIGI.Game.Tests")]

// The scenario harness reads a mission's cutscene state through the runtime's own testing accessors.
[assembly: InternalsVisibleTo("OpenIGI.Desktop.Tests")]

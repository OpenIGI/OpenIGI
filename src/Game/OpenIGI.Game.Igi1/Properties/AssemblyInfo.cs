using System.Runtime.CompilerServices;

// Project I.G.I.'s gameplay, reached by the host it is still driven by and by the suites that test it.
// The title's own presentation reads its simulation's state as it draws it: a title's two halves may know each other,
// as the layering rules let them reference each other. Nothing outside the title is granted this.
[assembly: InternalsVisibleTo("OpenIGI.Presentation.Igi1")]
[assembly: InternalsVisibleTo("OpenIGI.Game.Tests")]
[assembly: InternalsVisibleTo("OpenIGI.Game.Igi1.Tests")]
[assembly: InternalsVisibleTo("OpenIGI.Desktop.Tests")]
[assembly: InternalsVisibleTo("OpenIGI.Parity.Tests")]

using System.Runtime.CompilerServices;

// This title's renderers are internal. They are reached through the presenter interfaces in
// OpenIGI.Presentation, which is the whole point of moving them here, so nothing outside needs their
// types -- and keeping them internal is what stops a 6,000-line renderer becoming public API with a
// documentation comment on every member. The tests are the one exception, because a test that drives a
// renderer directly is worth more than one that drives it through a seam.
[assembly: InternalsVisibleTo("OpenIGI.Game.Igi1.Tests")]

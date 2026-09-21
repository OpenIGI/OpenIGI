namespace OpenIGI.Game.Igi2.World;

/// <summary>Which parameters one task type names its model, its position and its facing with.</summary>
/// <remarks>
/// <para>
/// <b>Most placeable types agree, and a dozen do not.</b> The great majority of I.G.I. 2's world objects
/// declare <c>Model</c>, <c>Position</c> and <c>Orientation</c> under exactly those names, which is what
/// lets <see cref="Igi2WorldScene"/> walk them all without a class for any of them. But a type whose
/// model moves has a name for where it starts rather than where it is — a <c>Door</c> carries
/// <c>Position start</c>, a <c>Lift</c> carries <c>Start Position</c> — and a type that owns two models
/// qualifies both — a <c>Switch</c> has an <c>On model</c> and an <c>Off model</c>, an <c>SCamera</c> a
/// <c>Holder Model</c> and a <c>Camera Model</c>.
/// </para>
/// <para>
/// The original needs no such table: every one of these types has its own create handler, and each one
/// binds its own parameter block to a <c>ModelObj</c> and registers it in the world's cube tree, so the
/// draw walk sees them all alike whatever their parameters are called. This is the port's equivalent —
/// the parameter names those handlers read, as data, so that one walk still collects every type.
/// </para>
/// </remarks>
/// <param name="Model">The parameter naming the model to draw at rest.</param>
/// <param name="Position">The parameter naming the position, a <c>Vector3d</c> in engine units.</param>
/// <param name="Orientation">
/// The parameter naming the Euler orientation, a <c>Vector3f</c> of alpha, beta and gamma. Empty when
/// the type carries a single angle instead; see <paramref name="Gamma"/>.
/// </param>
/// <param name="Gamma">
/// The parameter naming a lone gamma angle, for a type that turns about the world's up axis and nothing
/// else. Read only when <paramref name="Orientation"/> finds nothing.
/// </param>
/// <param name="AlternateModel">
/// The parameter naming the model to draw instead when <paramref name="AlternateWhen"/> is set. Empty
/// for a type with one model.
/// </param>
/// <param name="AlternateWhen">
/// The boolean parameter that selects <paramref name="AlternateModel"/>. Empty for a type with one model.
/// </param>
/// <param name="MountedModel">
/// The parameter naming a SECOND model this one carries, placed on the first's magic vertex. Empty for
/// a type with one model. See <see cref="Igi2WorldScene.Bindings"/> for the handlers.
/// </param>
/// <param name="MountedAlpha">
/// The parameter naming the mounted model's own tilt, in radians. Empty when it simply inherits the
/// host's facing.
/// </param>
/// <param name="MountedGamma">
/// The parameter naming the mounted model's own turn about the up axis, ADDED to the host's. Empty when
/// it simply inherits the host's facing.
/// </param>
/// <param name="Enumerated">
/// Every model parameter the type's lightmap enumerator walks, in the order it walks them. Empty for a
/// type with one model, which is then index zero. See <see cref="Igi2WorldScene.Bindings"/>.
/// </param>
public readonly record struct Igi2PlacementBinding(
    string Model,
    string Position,
    string Orientation = "",
    string Gamma = "",
    string AlternateModel = "",
    string AlternateWhen = "",
    string MountedModel = "",
    string MountedAlpha = "",
    string MountedGamma = "",
    IReadOnlyList<string>? Enumerated = null);

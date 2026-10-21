using Microsoft.Extensions.Primitives;
using OpenIGI.Releases;
using OpenIGI.Services;

// The update service. Three questions, all answered from the release index:
//
//   GET /v1/channels/{stable|beta}?since=<version>  what is newest, and every release the caller lacks
//   GET /download/{runtime}                         a redirect to the newest launcher (the site's buttons)
//   GET /download/{runtime}?component=game&channel=&flavour=dotnet   ... or to a game build
//   GET /v1/content                                 the optional content packs, each at its newest version
//   GET /v1/content/{pack}[/{version}]              one pack version's manifest: its files and their chunks
//   GET /v1/mods                                    the published mods, each at its newest version (manifests as above)
//   GET /healthz                                    whether it is up, and how many releases it sees
//
// The launcher polls the first every minute; the entity tag makes an unchanged answer a bodiless 304.

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.Services.Configure<ReleaseOptions>(builder.Configuration.GetSection("Releases"));
builder.Services.AddSingleton<ReleaseStore>();
builder.Services.AddHostedService(static services => services.GetRequiredService<ReleaseStore>());
builder.Services.Configure<ContentOptions>(builder.Configuration.GetSection("Content"));
builder.Services.AddSingleton<ContentStore>();
builder.Services.AddHostedService(static services => services.GetRequiredService<ContentStore>());

WebApplication app = builder.Build();

app.MapGet("/v1/channels/{channel}", (string channel, string? since, HttpContext context, ReleaseStore store) =>
{
    if (!ReleaseJson.TryParseChannel(channel, out ReleaseChannel parsed))
    {
        return Results.NotFound();
    }

    ReleaseIndex index = store.Current;

    // One tag per index and question: the same index answers differently for each channel and version.
    string tag = $"\"{index.Tag.Trim('"')}-{parsed.Name()}-{since}\"";
    context.Response.Headers.ETag = tag;
    context.Response.Headers.CacheControl = "public, max-age=30";
    context.Response.Headers.Vary = "Accept-Encoding";

    if (context.Request.Headers.IfNoneMatch.Contains(tag))
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    return Results.Text(ReleaseJson.Write(index.Feed(parsed, since)), "application/json");
});

app.MapGet("/download/{runtime}", (string runtime, string? component, string? channel, string? flavour, ReleaseStore store) =>
{
    // The launcher (the site's buttons): the newest stable one, the only channel it has. A game build is
    // asked for by channel and, for players with .NET installed, by the smaller flavour.
    if (component is null || string.Equals(component, "launcher", StringComparison.OrdinalIgnoreCase))
    {
        return store.Current.NewestLauncher(runtime)?.Find(ReleaseComponent.Launcher, runtime) is { Url: { } launcher }
            ? Results.Redirect(launcher)
            : Results.NotFound();
    }

    if (!string.Equals(component, "game", StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }

    ReleaseChannel parsedChannel = ReleaseChannel.Stable;
    if (channel is not null && !ReleaseJson.TryParseChannel(channel, out parsedChannel))
    {
        return Results.NotFound();
    }

    bool selfContained = !string.Equals(flavour, "dotnet", StringComparison.OrdinalIgnoreCase);

    // Newest release that has the build, so a release missing one platform does not break its button.
    foreach (ReleaseManifest release in store.Current.Feed(parsedChannel, "0.0.0-0").Releases)
    {
        if (release.Find(ReleaseComponent.Game, runtime, selfContained) is { Url: { } url })
        {
            return Results.Redirect(url);
        }
    }

    return Results.NotFound();
});

// The content packs are their own documents, apart from the channel feeds: a launcher too old to know of
// content reads the feeds with a strict enum of components, and must never meet one it does not know there.
app.MapGet("/v1/content", (HttpContext context, ContentStore store) =>
{
    ContentLibrary library = store.Current;
    context.Response.Headers.ETag = library.Tag;
    context.Response.Headers.CacheControl = "public, max-age=30";
    context.Response.Headers.Vary = "Accept-Encoding";

    return context.Request.Headers.IfNoneMatch.Contains(library.Tag)
        ? Results.StatusCode(StatusCodes.Status304NotModified)
        : Results.Text(ReleaseJson.Write(library.Index), "application/json");
});

// The mods, listed apart from the content packs for the same reason: a launcher that reads the content index knows
// only the pack kinds it was built with. Their manifests and chunks are the content store's, under the same routes.
app.MapGet("/" + ModPacks.IndexPath, (HttpContext context, ContentStore store) =>
{
    ContentLibrary library = store.Current;
    context.Response.Headers.ETag = library.Tag;
    context.Response.Headers.CacheControl = "public, max-age=30";
    context.Response.Headers.Vary = "Accept-Encoding";

    return context.Request.Headers.IfNoneMatch.Contains(library.Tag)
        ? Results.StatusCode(StatusCodes.Status304NotModified)
        : Results.Text(ReleaseJson.Write(library.Mods), "application/json");
});

// A published version never changes (the deploy verb refuses to overwrite one), so a named version's
// manifest is cached for good; the newest one, asked for without a version, only briefly.
app.MapGet("/v1/content/{pack}/{version?}", (string pack, string? version, HttpContext context, ContentStore store) =>
{
    if (store.Current.Manifest(pack, version) is not { } json)
    {
        return Results.NotFound();
    }

    context.Response.Headers.CacheControl = version is null ? "public, max-age=30" : "public, max-age=31536000, immutable";
    context.Response.Headers.Vary = "Accept-Encoding";
    return Results.Text(json, "application/json");
});

app.MapGet("/healthz", (ReleaseStore store) =>
    Results.Text($"ok {store.Current.Count} release(s) {store.Current.Tag}\n", "text/plain"));

app.MapGet("/", () => Results.Redirect("https://openigi.com/"));

await app.RunAsync().ConfigureAwait(false);

/// <summary>The service's entry point, named so the tests can host it.</summary>
public partial class Program;

using Microsoft.Extensions.Primitives;
using OpenIGI.Releases;
using OpenIGI.Services;

// The update service. Three questions, all answered from the release index:
//
//   GET /v1/channels/{stable|beta}?since=<version>  what is newest, and every release the caller lacks
//   GET /download/{runtime}?channel=&component=     a redirect to the newest archive (the site's buttons)
//   GET /healthz                                    whether it is up, and how many releases it sees
//
// The launcher polls the first every minute; the entity tag makes an unchanged answer a bodiless 304.

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.Services.Configure<ReleaseOptions>(builder.Configuration.GetSection("Releases"));
builder.Services.AddSingleton<ReleaseStore>();
builder.Services.AddHostedService(static services => services.GetRequiredService<ReleaseStore>());

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

app.MapGet("/download/{runtime}", (string runtime, string? channel, string? component, ReleaseStore store) =>
{
    ReleaseChannel parsedChannel = ReleaseChannel.Stable;
    if (channel is not null && !ReleaseJson.TryParseChannel(channel, out parsedChannel))
    {
        return Results.NotFound();
    }

    ReleaseComponent parsedComponent = ReleaseComponent.Launcher;
    if (component is not null && !Enum.TryParse(component, ignoreCase: true, out parsedComponent))
    {
        return Results.NotFound();
    }

    // Newest release that has the archive, so a release missing one platform does not break its button.
    foreach (ReleaseManifest release in store.Current.Feed(parsedChannel, "0.0.0-0").Releases)
    {
        if (release.Find(parsedComponent, runtime) is { Url: { } url })
        {
            return Results.Redirect(url);
        }
    }

    return Results.NotFound();
});

app.MapGet("/healthz", (ReleaseStore store) =>
    Results.Text($"ok {store.Current.Count} release(s) {store.Current.Tag}\n", "text/plain"));

app.MapGet("/", () => Results.Redirect("https://openigi.com/"));

await app.RunAsync().ConfigureAwait(false);

/// <summary>The service's entry point, named so the tests can host it.</summary>
public partial class Program;

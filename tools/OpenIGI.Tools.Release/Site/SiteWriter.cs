using System.Net;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Writes what the update service used to answer as files, for a static host to serve at the same paths.</summary>
/// <remarks>
/// <para>
/// The launcher asks for <c>v1/channels/{channel}</c>, <c>v1/content</c>, <c>v1/content/{pack}[/{version}]</c> and
/// <c>v1/mods</c>; the site's buttons and <c>install.sh</c> for <c>download/{runtime}</c>. A static host cannot hold a
/// file and a folder under one name, so the two paths that are both (<c>v1/content</c> and each pack's newest
/// manifest) are folders with an <c>index.html</c>, which the host reaches by redirecting to the trailing slash. The
/// launcher reads every answer as text, whatever type the host calls it.
/// </para>
/// <para>
/// The download links are a page that sends a browser on to the newest launcher, and a <c>url</c> file beside it
/// holding the address alone, for a script.
/// </para>
/// </remarks>
internal static class SiteWriter
{
    /// <summary>How many of a channel's newest releases its feed lists.</summary>
    public const int FeedReleases = 10;

    /// <summary>Writes the update files into a site folder.</summary>
    /// <param name="output">The site's root.</param>
    /// <param name="releases">The releases.</param>
    /// <param name="content">The content packs and mods.</param>
    /// <param name="contentRoot">The content store the pictures are copied from, or <see langword="null"/> for none.</param>
    public static void Write(string output, ReleaseIndex releases, ContentLibrary content, string? contentRoot)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(content);

        foreach (ReleaseChannel channel in new[] { ReleaseChannel.Stable, ReleaseChannel.Beta })
        {
            Text(output, $"v1/channels/{channel.Name()}", ReleaseJson.Write(releases.Published(channel, FeedReleases)));
        }

        Text(output, "v1/content/index.html", ReleaseJson.Write(content.Index));
        Text(output, ModPacks.IndexPath, ReleaseJson.Write(content.Mods));
        foreach ((string id, string version, string json) in content.Versions)
        {
            Text(output, $"v1/content/{id}/{version}", json);
            if (content.Manifest(id, null) == json)
            {
                Text(output, $"v1/content/{id}/index.html", json);
            }
        }

        // The pictures the listings name, which sit beside each pack's description in the store.
        if (contentRoot is not null)
        {
            foreach (ContentPack pack in content.Index.Packs.Concat(content.Mods.Packs))
            {
                foreach (string picture in Directory.Exists(Path.Combine(contentRoot, pack.Id))
                             ? Directory.EnumerateFiles(Path.Combine(contentRoot, pack.Id), "thumbnail-*")
                             : [])
                {
                    string target = Path.Combine(output, "content", pack.Id, Path.GetFileName(picture));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(picture, target, overwrite: true);
                }
            }
        }

        if (releases.NewestLauncher() is { } launcher)
        {
            foreach (ReleaseAsset asset in launcher.Assets.Where(static a => a.Component == ReleaseComponent.Launcher && a.SelfContained))
            {
                if (asset.Runtime is not { } runtime || asset.Url is not { } url)
                {
                    continue;
                }

                Text(output, $"download/{runtime}/url", url + "\n");
                string encoded = WebUtility.HtmlEncode(url);
                Text(output, $"download/{runtime}/index.html",
                    $"<!doctype html><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0; url={encoded}\">"
                    + $"<title>OpenIGI Launcher</title><a href=\"{encoded}\">Download the OpenIGI Launcher {WebUtility.HtmlEncode(launcher.Version)}</a>\n");
            }
        }

        Text(output, "healthz", $"ok {releases.Count} release(s) {content.Count} content version(s)\n");
    }

    private static void Text(string root, string path, string text)
    {
        string target = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text.EndsWith('\n') ? text : text + "\n");
    }
}

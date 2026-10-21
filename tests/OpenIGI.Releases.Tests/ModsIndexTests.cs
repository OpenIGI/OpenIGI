using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using OpenIGI.Releases;
using OpenIGI.Tools.Release;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>The community mods index: its rules, who may change what, and building a version to publish.</summary>
public sealed class ModsIndexTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private readonly string _index = Directory.CreateTempSubdirectory("openigi-mods-index-").FullName;

    public void Dispose() => Directory.Delete(_index, recursive: true);

    [Fact]
    public void A_mod_with_its_owners_and_a_version_built_from_a_full_commit_is_in_order()
    {
        Write("mods/guards/index.json", """{ "owners": ["someone"], "repository": "https://github.test/someone/guards" }""");
        Write("mods/guards/1.0.0.json", $$"""{ "source": { "commit": "{{Commit}}", "project": "src/Guards.csproj" } }""");
        Write("mods/voices/index.json", """{ "owners": ["another"] }""");
        Write("mods/voices/2.1.0.json", $$"""{ "files": { "url": "https://files.test/voices.zip", "sha256": "{{new string('a', 64)}}" } }""");

        Assert.Empty(ModsIndex.Check(_index));
    }

    [Theory]
    [InlineData("mods/bad_id/index.json", """{ "owners": ["someone"] }""")]
    [InlineData("mods/local/index.json", """{ "owners": ["someone"] }""")]
    [InlineData("mods/guards/index.json", """{ "owners": [] }""")]
    [InlineData("mods/guards/index.json", """{ "owners": ["someone"], "repository": "git@github.test:someone/guards.git" }""")]
    [InlineData("mods/guards/1.0.json", """{ "files": { "url": "https://files.test/a.zip", "sha256": "aaaa" } }""")]
    [InlineData("mods/guards/1.0.0.json", """{ "source": { "commit": "0123abc", "project": "Guards.csproj" } }""")]
    [InlineData("mods/guards/1.0.0.json", """{ "source": { "commit": "0123456789abcdef0123456789abcdef01234567", "project": "../Guards.csproj" } }""")]
    [InlineData("mods/guards/1.0.0.json", """{ }""")]
    public void An_entry_or_version_that_breaks_a_rule_is_reported(string path, string json)
    {
        Write("mods/guards/index.json", """{ "owners": ["someone"], "repository": "https://github.test/someone/guards" }""");
        Write(path, json);

        Assert.NotEmpty(ModsIndex.Check(_index));
    }

    [Fact]
    public void Only_an_owner_as_the_entry_stood_adds_versions_and_a_published_one_never_changes()
    {
        Write("mods/guards/index.json", """{ "owners": ["someone", "intruder"], "repository": "https://github.test/someone/guards" }""");
        Write("mods/guards/1.0.0.json", $$"""{ "source": { "commit": "{{Commit}}", "project": "Guards.csproj" } }""");
        Write("mods/guards/1.1.0.json", $$"""{ "source": { "commit": "{{Commit}}", "project": "Guards.csproj" } }""");
        ModIndexEntry before = new(["someone"], "https://github.test/someone/guards");
        ModIndexChange[] adding = [new("A", "mods/guards/1.1.0.json"), new("M", "mods/guards/index.json")];

        Assert.Empty(ModsIndex.Check(_index, adding, "someone", _ => before));
        Assert.NotEmpty(ModsIndex.Check(_index, adding, "intruder", _ => before));
        Assert.NotEmpty(ModsIndex.Check(_index, [new("M", "mods/guards/1.0.0.json")], "someone", _ => before));
        Assert.NotEmpty(ModsIndex.Check(_index, [new("M", ".github/workflows/publish.yml")], "someone", _ => before));
        Assert.Equal([("guards", "1.1.0")], ModsIndex.Added(adding));
    }

    [Fact]
    public void Whoever_adds_a_mod_is_among_its_owners()
    {
        Write("mods/voices/index.json", """{ "owners": ["another"] }""");
        Write("mods/voices/1.0.0.json", $$"""{ "files": { "url": "https://files.test/voices.zip", "sha256": "{{new string('a', 64)}}" } }""");
        ModIndexChange[] added = [new("A", "mods/voices/index.json"), new("A", "mods/voices/1.0.0.json")];

        Assert.Empty(ModsIndex.Check(_index, added, "another", static _ => null));
        Assert.NotEmpty(ModsIndex.Check(_index, added, "someone", static _ => null));
    }

    [Fact]
    public void A_mod_of_content_alone_is_built_from_its_archive_when_the_hash_matches()
    {
        byte[] archive = Zip(("mod.json", """{ "id": "voices", "version": "1.0.0", "apiVersion": 1, "summary": "New voices." }"""), ("sounds/a.wav", "sound"));
        Write("mods/voices/index.json", """{ "owners": ["another"] }""");
        Write("mods/voices/1.0.0.json", $$"""{ "files": { "url": "https://files.test/voices.zip", "sha256": "{{Convert.ToHexStringLower(SHA256.HashData(archive))}}" } }""");
        string output = Path.Combine(_index, "out");
        using HttpClient http = new(new Serve(archive));

        ContentPublish.Details details = ModsIndex.Build(_index, "voices", "1.0.0", output, Path.Combine(_index, "work"), http, TextWriter.Null);

        Assert.Equal((ContentKind.Mod, "voices", "1.0.0"), (details.Kind, details.Id, details.Version));
        Assert.True(File.Exists(Path.Combine(output, "sounds", "a.wav")));

        Write("mods/voices/1.0.0.json", $$"""{ "files": { "url": "https://files.test/voices.zip", "sha256": "{{new string('b', 64)}}" } }""");
        Assert.Throws<InvalidDataException>(() => ModsIndex.Build(_index, "voices", "1.0.0", output, Path.Combine(_index, "work"), http, TextWriter.Null));
    }

    private void Write(string path, string text)
    {
        string file = Path.Combine(_index, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private static byte[] Zip(params (string Path, string Text)[] files)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string text) in files)
            {
                using StreamWriter writer = new(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private sealed class Serve(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}

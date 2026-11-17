using System.Text;
using OpenIGI.Releases.Maps;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>
/// A community map's package: what the author made carried, the game's own files named and given back by the player's game,
/// every file checked by its hash, the rules a map is held to, and its author's signature.
/// </summary>
public sealed class MapPackageTests
{
    private const string Folder = "missions/location0/level15";

    private static readonly byte[] Terrain = Encoding.ASCII.GetBytes("the game's own terrain, unchanged");
    private static readonly byte[] Script = Encoding.ASCII.GetBytes("a new mission.qvm");
    private static readonly byte[] Objects = Encoding.ASCII.GetBytes("objects edited by the author");

    /// <summary>A copied level's unchanged file is named, not carried; unpacked, the player's game gives it back.</summary>
    [Fact]
    public void The_games_own_files_are_named_and_given_back()
    {
        string terrainHash = MapPackage.Hash(Terrain);
        MapManifest manifest = Pack(hash => hash == terrainHash ? "missions/location0/level1/terrain/terrain.dat" : null);
        Assert.Equal("missions/location0/level1/terrain/terrain.dat", manifest.Files.Single(f => f.Path.EndsWith("terrain.dat", StringComparison.Ordinal)).Origin);
        Assert.Equal(Script.Length + Objects.Length, manifest.EmbeddedSize);
        Assert.Empty(MapRules.Check(manifest, hash => hash == terrainHash));

        byte[] package = Write(manifest);
        Assert.True(package.Length < Script.Length + Objects.Length + 2048, "the game's file is not carried");

        Dictionary<string, byte[]> written = [];
        MapPacker.Unpack(package, path => path.EndsWith("terrain.dat", StringComparison.Ordinal) ? Terrain : null, (path, bytes) => written[path] = bytes.ToArray());
        Assert.Equal(Terrain, written[$"{Folder}/terrain/terrain.dat"]);
        Assert.Equal(Objects, written[$"{Folder}/objects.qsc"]);
        Assert.Equal(Script, written[$"{Folder}/mission.qvm"]);
    }

    /// <summary>A player whose game's file is not the author's cannot unpack the map: it would not be the map that was made.</summary>
    [Fact]
    public void A_game_file_unlike_the_authors_is_refused()
    {
        string terrainHash = MapPackage.Hash(Terrain);
        byte[] package = Write(Pack(hash => hash == terrainHash ? "missions/location0/level1/terrain/terrain.dat" : null));
        Assert.Throws<InvalidDataException>(() => MapPacker.Unpack(package, _ => Encoding.ASCII.GetBytes("a different terrain"), (_, _) => { }));
    }

    /// <summary>A carried file changed after packing, or a package cut short, does not read.</summary>
    [Fact]
    public void A_damaged_package_does_not_read()
    {
        byte[] package = Write(Pack(_ => null));
        package[^1] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => MapPackage.Read(package));
        Assert.Throws<InvalidDataException>(() => MapPackage.Read(package.AsMemory(0, package.Length - 3)));
        Assert.Throws<InvalidDataException>(() => MapPackage.Read(Encoding.ASCII.GetBytes("not a map at all")));
    }

    /// <summary>The rules: data only, inside the mission's folder, its own script, and none of the game's files carried.</summary>
    [Fact]
    public void The_rules_hold_a_map_to_data_inside_its_folder()
    {
        MapManifest good = Pack(_ => null);
        Assert.Empty(MapRules.Check(good));

        MapManifest code = good with { Files = [.. good.Files, new MapFile($"{Folder}/evil.dll", 4, new string('a', 64))] };
        Assert.Contains(MapRules.Check(code), p => p.Contains("evil.dll", StringComparison.Ordinal));

        MapManifest outside = good with { Files = [.. good.Files, new MapFile("missions/location0/level1/objects.qsc", 4, new string('b', 64))] };
        Assert.Contains(MapRules.Check(outside), p => p.Contains("inside the mission's folder", StringComparison.Ordinal));

        MapManifest climbing = good with { Files = [.. good.Files, new MapFile($"{Folder}/../../../windows/x.dat", 4, new string('c', 64))] };
        Assert.NotEmpty(MapRules.Check(climbing));

        MapManifest unscripted = good with { Files = [.. good.Files.Where(f => !f.Path.EndsWith("mission.qvm", StringComparison.Ordinal))] };
        Assert.Contains(MapRules.Check(unscripted), p => p.Contains("mission.qvm", StringComparison.Ordinal));

        string objectsHash = MapPackage.Hash(Objects);
        Assert.Contains(MapRules.Check(good, hash => hash == objectsHash), p => p.Contains("game's own files", StringComparison.Ordinal));
    }

    /// <summary>A request signed by an installation's key checks out with that key, and not once anything in it changes.</summary>
    [Fact]
    public async Task A_signed_request_checks_out_only_as_it_was_signed()
    {
        using MapIdentity identity = MapIdentity.CreateEphemeral();
        byte[] body = Encoding.ASCII.GetBytes("a package");
        IReadOnlyList<(string Name, string Value)> headers = await SignedRequest.HeadersAsync(identity, "POST", "/v1/maps", 1_800_000_000, body);
        string key = headers.Single(h => h.Name == SignedRequest.KeyHeader).Value;
        string signature = headers.Single(h => h.Name == SignedRequest.SignatureHeader).Value;
        Assert.True(SignedRequest.Verify(key, signature, "POST", "/v1/maps", 1_800_000_000, body));
        Assert.False(SignedRequest.Verify(key, signature, "POST", "/v1/maps", 1_800_000_001, body));
        Assert.False(SignedRequest.Verify(key, signature, "POST", "/v1/maps/other", 1_800_000_000, body));
        Assert.False(SignedRequest.Verify(key, signature, "POST", "/v1/maps", 1_800_000_000, Encoding.ASCII.GetBytes("another")));
        Assert.Equal(16, identity.KeyId.Length);
    }

    private static MapManifest Pack(Func<string, string?> gameFile) => MapPacker.Manifest(
        new MapManifest(MapManifest.CurrentSchema, "night-raid", "Night Raid", "The trainyard by night.", "igi1", "Tester", 15, Folder, []),
        [
            ($"{Folder}/mission.qvm", Script),
            ($"{Folder}/objects.qsc", Objects),
            ($"{Folder}/terrain/terrain.dat", Terrain),
        ],
        gameFile);

    private static byte[] Write(MapManifest manifest)
    {
        Dictionary<string, byte[]> bytes = new(StringComparer.Ordinal)
        {
            [$"{Folder}/mission.qvm"] = Script,
            [$"{Folder}/objects.qsc"] = Objects,
            [$"{Folder}/terrain/terrain.dat"] = Terrain,
        };
        using MemoryStream stream = new();
        MapPackage.Write(manifest, path => bytes[path], stream);
        return stream.ToArray();
    }
}

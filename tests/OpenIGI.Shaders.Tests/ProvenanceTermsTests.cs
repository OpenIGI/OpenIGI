using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// Keeps other products' trademarks and system names out of the source tree.
/// </summary>
/// <remarks>
/// The renderer is described in its own terms and by the public papers its techniques come from. The
/// names this guards against are held only as SHA-256 hashes, so the test does not itself put them into
/// the tree it checks. Every word is hashed whole and as the pieces of an identifier -- camel case and
/// underscores split, neighbouring pieces rejoined -- so a name cannot hide inside a longer identifier.
/// </remarks>
public sealed partial class ProvenanceTermsTests
{
    // Single words, lower case.
    private static readonly HashSet<string> BannedWords =
    [
        "28c9483d0805aa42d0bfe2feab26d4fb95dd995b6412faecb40267182415d271",
        "43dc59a880cb3f1dd686a6fa16aef336e4a197d82b62af9168865b310d397812",
        "e9bdc03db3c2694748428e64457e6dff5da86ebc73f3b31354ddc85a479a68de",
        "709e1d1d1fa00b58bb842c52c3a6cc4f8dd7455810300a4a09ea4b4ce73d629d",
        "9471848c4835a19f0bff2d3529343a8b0149e501fff60573b09f931d55a2738a",
        "ab2318392fee747b2269c5da107bf60ef839825184a3d7f5d9455b6d37f6bde2",
        "efcba552bd72ec1fee3ad23cd1a2d366ee30b4798e03b3a40f01ce68e1c892eb",
        "ba85051d97d6042db2bddf85a3a97e520406753942a0d87ba59c529202482fbc",
        "f1fa3d620a6278465cc907aa6e93381e52f1165e5b225423a06a2d791b09a216",
        "456f45fddc2e725c2948d03317e2262c2ec8e86745af28dc7b2221987b858b50",
    ];

    // Phrases of two or three lower-case words separated by single spaces.
    private static readonly HashSet<string> BannedPhrases =
    [
        "6737da25fcaf92b8707cfbc702f217c5d4c0034eb75a68f16716439d50840a79",
        "75855c8f8122b858d0950d205eedc2b94656a2846f50df9b3fadb5675bbb6e3e",
    ];

    private static readonly string[] TextExtensions =
    [
        ".cs", ".slang", ".wgsl", ".json", ".md", ".ps1", ".sh", ".py", ".props", ".targets", ".csproj",
        ".slnx", ".yml", ".yaml", ".toml", ".txt", ".xml", ".html", ".js", ".css", ".cfg", ".ini",
    ];

    private static readonly string[] SkippedDirectories = ["bin", "obj", "artifacts", ".git", "node_modules", ".venv"];

    /// <summary>No tracked text file names another product's renderer, engine or asset package.</summary>
    [Fact]
    public void No_source_file_names_another_products_systems()
    {
        string root = Path.GetFullPath(Path.Combine(ShaderRepository.ProjectDirectory, "..", "..", ".."));
        List<string> found = [];

        foreach (string top in new[] { "src", "apps", "tools", "tests", "services", "deploy", ".github" })
        {
            string directory = Path.Combine(root, top);
            if (Directory.Exists(directory))
            {
                Scan(root, directory, found);
            }
        }

        foreach (string file in Directory.EnumerateFiles(root).Where(IsText))
        {
            ScanFile(root, file, found);
        }

        Assert.True(found.Count == 0, "Banned name at:" + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    private static void Scan(string root, string directory, List<string> found)
    {
        foreach (string file in Directory.EnumerateFiles(directory).Where(IsText))
        {
            ScanFile(root, file, found);
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                Scan(root, child, found);
            }
        }
    }

    private static bool IsText(string path) =>
        TextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static void ScanFile(string root, string file, List<string> found)
    {
        string[] lines = File.ReadAllLines(file);
        for (int i = 0; i < lines.Length; i++)
        {
            if (LineIsBanned(lines[i]))
            {
                found.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{i + 1}");
            }
        }
    }

    private static bool LineIsBanned(string line)
    {
        List<string> words = [.. Word().Matches(line).Select(match => match.Value)];

        foreach (string word in words)
        {
            if (BannedWords.Contains(Hash(word.ToLowerInvariant())))
            {
                return true;
            }

            // An identifier's pieces, alone and joined with their neighbours: a name split across a camel
            // boundary (two capitals) or glued to the next word is still the name.
            string[] pieces = [.. Piece().Matches(word).Select(match => match.Value.ToLowerInvariant())];
            for (int start = 0; start < pieces.Length; start++)
            {
                string joined = string.Empty;
                for (int end = start; end < Math.Min(pieces.Length, start + 3); end++)
                {
                    joined += pieces[end];
                    if (BannedWords.Contains(Hash(joined)))
                    {
                        return true;
                    }
                }
            }
        }

        for (int i = 0; i < words.Count; i++)
        {
            string pair = i + 1 < words.Count ? $"{words[i]} {words[i + 1]}".ToLowerInvariant() : string.Empty;
            string triple = i + 2 < words.Count ? $"{pair} {words[i + 2].ToLowerInvariant()}" : string.Empty;
            if ((pair.Length > 0 && BannedPhrases.Contains(Hash(pair)))
                || (triple.Length > 0 && BannedPhrases.Contains(Hash(triple))))
            {
                return true;
            }
        }

        return false;
    }

    private static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex Word();

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Piece();
}

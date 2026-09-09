using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Formats.Tests;

/// <summary>How far an I.G.I. 2 model gets through the reader written for I.G.I. 1.</summary>
/// <remarks>Diagnostic, not an assertion: it reports rather than requires.</remarks>
public sealed class DiagIgi2MefProbe(ITestOutputHelper output)
{
    /// <summary>Parses every model of one I.G.I. 2 level and reports what failed.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Report_how_many_models_parse()
    {
        string missions = Path.Combine(GameDataSource.Igi2.RequireRoot(), "missions");
        string archive = Directory
            .EnumerateFiles(missions, "*.res", SearchOption.AllDirectories)
            .First(static p => Path.GetFileName(Path.GetDirectoryName(p))!
                .Equals("models", StringComparison.OrdinalIgnoreCase));

        IresArchive models = IresArchive.Read(File.ReadAllBytes(archive));
        int ok = 0;
        Dictionary<string, int> failures = new(StringComparer.Ordinal);

        foreach (IresEntry entry in models.Entries.Where(static e =>
            e.Name.EndsWith(".mef", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                MefModel model = MefModel.Read(entry.Data, AssetFormatProfile.Igi2);
                if (model.BaseVertexCount > 0)
                {
                    ok++;
                }
            }
            catch (Exception ex)
            {
                string key = ex.Message.Length > 90 ? ex.Message[..90] : ex.Message;
                failures[key] = failures.GetValueOrDefault(key) + 1;
            }
        }

        output.WriteLine($"archive: {archive}");
        output.WriteLine($"parsed with geometry: {ok}");
        foreach ((string message, int count) in failures.OrderByDescending(static kv => kv.Value))
        {
            output.WriteLine($"  {count,4}  {message}");
        }
    }
}

using OpenIGI.Scripting.Qsc;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>
/// Every retail script survives decompiling to QSC and compiling back, byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// This is the bar the editor stands on: a mission saved without changes must be the file it was, and a
/// vanilla export must be loadable by the retail executable. Measured 2026-11-02 on retail installations:
/// all 998 I.G.I. scripts and all 1,788 I.G.I. 2 scripts pass both paths.
/// </para>
/// <para>
/// <c>openigi-config.qvm</c> is skipped. It is OpenIGI's own settings file, not retail data, and copies
/// written by builds that predate the terminating <c>BRK</c> fix do not end the way a compiled script does.
/// </para>
/// </remarks>
public sealed class RetailQscRoundTripTests
{
    /// <summary>I.G.I.'s scripts round-trip through the tree and through source text.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Every_igi1_script_round_trips() => AssertRoundTrips(GameDataSource.Igi1);

    /// <summary>I.G.I. 2's scripts round-trip through the tree and through source text.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_igi2_script_round_trips() => AssertRoundTrips(GameDataSource.Igi2);

    private static void AssertRoundTrips(GameDataSource source)
    {
        IReadOnlyList<string> scripts = source.EnumerateFiles("*.qvm");
        Assert.NotEmpty(scripts);

        List<string> failures = [];
        foreach (string path in scripts)
        {
            if (Path.GetFileName(path).Equals("openigi-config.qvm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            byte[] original = File.ReadAllBytes(path);
            QscScript script = QscDecompiler.Decompile(original);
            if (!original.AsSpan().SequenceEqual(QscCompiler.Compile(script)))
            {
                failures.Add($"{path} (tree)");
                continue;
            }

            QscScript reparsed = QscParser.ParseScript(QscPrinter.Print(script), script.Version);
            QscScript rebuilt = new(reparsed.Statements, script.Version)
            {
                RequestedStackSizeBytes = script.RequestedStackSizeBytes,
                RequestedMaxLocals = script.RequestedMaxLocals,
                FileInfo = script.FileInfo,
            };

            if (!original.AsSpan().SequenceEqual(QscCompiler.Compile(rebuilt)))
            {
                failures.Add($"{path} (text)");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} scripts did not round-trip:\n{string.Join('\n', failures.Take(20))}");
    }
}

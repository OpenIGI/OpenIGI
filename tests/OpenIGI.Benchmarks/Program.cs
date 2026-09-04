using BenchmarkDotNet.Running;

namespace OpenIGI.Benchmarks;

/// <summary>The benchmark host.</summary>
/// <remarks>
/// Run everything with <c>dotnet run -c Release --project tests/OpenIGI.Benchmarks</c>, or one class
/// with <c>--filter *MaterialMap*</c>. Add <c>--job short</c> for a quick read while iterating.
/// BenchmarkDotNet spawns its own optimised process per job, so a Debug launch measures nothing useful
/// and it will say so rather than lie.
/// </remarks>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

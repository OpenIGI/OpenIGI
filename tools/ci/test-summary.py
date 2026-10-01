"""Turns the TRX files a `dotnet test` run left behind into a Markdown table for the job summary.

    python3 tools/ci/test-summary.py <results-directory>

One row per test assembly (passed / failed / skipped), then every failure with its message, so a red run
says what broke without anyone opening the log. Skips are counted but not listed: here they are the tests
that need retail game data or a GPU, and they are expected.
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main(directory: str) -> int:
    files = sorted(glob.glob(os.path.join(directory, "**", "*.trx"), recursive=True))
    if not files:
        print("No test results were written.")
        return 0

    rows, failures = [], []
    totals = [0, 0, 0]
    for path in files:
        root = ET.parse(path).getroot()
        counters = root.find("t:ResultSummary/t:Counters", NS)
        if counters is None:
            continue
        passed = int(counters.get("passed", 0))
        failed = int(counters.get("failed", 0))
        skipped = int(counters.get("notExecuted", 0))
        definitions = root.findall("t:TestDefinitions/t:UnitTest/t:TestMethod", NS)
        storage = definitions[0].get("codeBase", path) if definitions else path
        name = os.path.splitext(os.path.basename(storage))[0]
        rows.append((name, passed, failed, skipped))
        totals = [totals[0] + passed, totals[1] + failed, totals[2] + skipped]

        for result in root.findall("t:Results/t:UnitTestResult", NS):
            if result.get("outcome") == "Failed":
                message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS).strip()
                failures.append((result.get("testName"), message.splitlines()[0] if message else ""))

    verdict = "failed" if totals[1] else "passed"
    print(f"### Tests {verdict}: {totals[0]} passed, {totals[1]} failed, {totals[2]} skipped\n")
    print("| Assembly | Passed | Failed | Skipped |")
    print("|---|---:|---:|---:|")
    for name, passed, failed, skipped in sorted(rows):
        mark = " :x:" if failed else ""
        print(f"| {name}{mark} | {passed} | {failed} | {skipped} |")

    if failures:
        print("\n#### Failures\n")
        for test, message in failures:
            print(f"- `{test}`: {message}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "test-results"))

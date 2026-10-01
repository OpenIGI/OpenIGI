; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
OIGI001 | OpenIGI  | Error    | [ScriptParam] requires a GameTask
OIGI002 | OpenIGI  | Error    | Task type must be partial
OIGI003 | OpenIGI  | Error    | Unknown script parameter type
OIGI004 | OpenIGI  | Error    | Script parameter type does not match the member type
OIGI005 | OpenIGI  | Error    | Script parameters split across files
OIGI006 | OpenIGI  | Error    | Script parameter must be assignable
OIGI007 | OpenIGI  | Error    | Console variable container must be a static class
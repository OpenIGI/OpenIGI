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
OIGI008 | OpenIGI  | Error    | [LevelService] requires a GameTask
OIGI009 | OpenIGI  | Error    | Task type with level services must be partial
OIGI010 | OpenIGI  | Error    | Level service must be a settable reference-type property
# Contributing to OpenIGI

Thanks for helping build a faithful, open Project I.G.I. engine. Please read this before opening a PR.

## Golden rules

1. **Preserve 1:1 behaviour.** The engine has a determinism contract: fixed 30 Hz ticks driven from
   absolute time targets, truncate-toward-zero float→int conversion, a catch-up cap of 10, one render
   per tick, `Last`-shadow edge detection, cursor-patching deletion, and `double` world positions. A
   "cleaner" change that alters any of these is a **bug**, however much nicer it reads.
2. **Keep the core backend-free.** `OpenIGI.Engine` and `OpenIGI.Game` may reference only the
   abstraction projects — `Rendering`, `Audio`, `Input`, `Platform` — and never a concrete backend.
   Backends are wired together in the app composition root.
3. **Document every public member.** The build fails on missing XML docs (`CS1591` is an error) and on
   any warning at all. Run `dotnet build` before pushing.
4. **The binary is the source of truth.** Behavioural facts come from the original executable, not
   from what would be reasonable. Say where a fact came from — the address of the function you read,
   or the measurement you took — in the code comment or the commit message that introduces it.

## Reverse-engineering workflow

The original `IGI.exe` is the authority. Community reverse-engineering projects and the later I.G.I. 2
binaries are useful for generating hypotheses and for naming things, but they are corroborators rather
than sources: I.G.I. 2 is a later version of the same engine with whole systems rewritten, so a name
from its symbols tells you what Innerloop called something in the sequel, not what this game does.
Confirm against the real thing before relying on it.

Prefer confirming against **real game files** (`IGI_GAME_PATH`) over assumptions, and prefer measuring
the running original over reading the decompiler when the two could disagree.

Working notes, design documents and reverse-engineering write-ups are deliberately **not** kept in this
repository — they are a running notebook that goes stale faster than the code it describes, and the
reference captures that go with them are screenshots of a game we cannot redistribute. Keep yours
locally; put the findings that matter into code comments and commit messages, where they stay next to
what they explain.

## Tests

- Add unit tests for logic, and **golden tests** for every format reader.
- Golden tests that need real assets must read `IGI_GAME_PATH` and **skip**, not fail, when it is
  unset. A checkout with no game data has to build and test green.
- **Never commit copyrighted game data.** Only tiny synthetic fixtures, or ones extracted from the
  executable at test time, under `tests/**/fixtures/`. This includes derived data: textures with a
  filter over them are still Innerloop's art.
- Determinism-sensitive systems get a "same inputs, same state hash" test.

## Code style

- `.editorconfig` is enforced by the build. File-scoped namespaces, explicit accessibility,
  `_camelCase` private fields, braces always.
- Favour small single-responsibility types over large ones, and shared helpers over duplication.
- Central Package Management: add package **versions** in `Directory.Packages.props` and reference
  them without a version in the `.csproj`.
- Comments should explain **why**, especially where the code looks wrong but matches the original.
  A divergence from the original needs a comment saying that it is one and what it buys.

## Commits and PRs

- One concern per PR where possible, and keep the build green with tests passing.
- Write commit messages that explain the reasoning, not just the change. Most of what is known about
  this engine lives in its history, so a message that records why something is the way it is is worth
  more than the diff it accompanies.
- Do not add AI assistant attribution trailers to commits.

## Legal

By contributing you agree your work is licensed under **GPL-3.0**. Do not paste original game source,
decompiler output or disassembly into the codebase — describe the behaviour and reimplement it
cleanly. Contributions containing game assets or verbatim decompiler output will not be accepted.

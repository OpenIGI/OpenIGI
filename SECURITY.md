# Security policy

## Supported versions

Security fixes go into the latest release on the stable channel and into the current beta. Older releases are not
patched; the launcher updates to the latest release.

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Report it privately through GitHub instead:
[Report a vulnerability](https://github.com/OpenIGI/OpenIGI/security/advisories/new). Only the maintainers can see the
report.

Please include:

- what the problem is and what an attacker could do with it,
- the OpenIGI version and platform (Windows, Linux, macOS or the browser),
- steps or a proof of concept that reproduces it.

You should get a first reply within a week. Once a fix is released, the advisory is published and you are credited
unless you ask not to be.

## Scope

In scope:

- the engine, the launcher and the browser build,
- multiplayer: the netcode, listen and dedicated servers, and the relays,
- the mod index, mod installation and the modding SDK,
- the update and download channels (openigi.com, data.openigi.com and GitHub releases).

Out of scope:

- cheats in multiplayer that need no vulnerability (aim assistance, wall hacks and the like),
- problems in the original games' files,
- denial of service by flooding a server or relay you do not run.
